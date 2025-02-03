using System.Text.RegularExpressions;
using Anthropic.SDK.Messaging;

namespace Thorfix;

using Octokit;
using System.Text;
using Anthropic;
using Anthropic.SDK;

public class GitHubBot
{
    private readonly GitHubClient _github;
    private readonly AnthropicClient _claude;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _botBranch;

    public GitHubBot(string githubToken, string claudeApiKey, string repoOwner, string repoName)
    {
        _github = new GitHubClient(new ProductHeaderValue("IssueBot"))
        {
            Credentials = new Credentials(githubToken)
        };
        
        _claude = new AnthropicClient(claudeApiKey);
        _repoOwner = repoOwner;
        _repoName = repoName;
        _botBranch = "bot-fixes";
    }

    public async Task MonitorAndHandleIssues()
    {
        while (true)
        {
            try
            {
                var issues = await _github.Issue.GetAllForRepository(_repoOwner, _repoName, new RepositoryIssueRequest
                {
                    State = ItemStateFilter.Open,
                    Labels = { "thorfix" },
                });

                foreach (Issue? issue in issues)
                {
                    if (issue.Labels.Any(l => l.Name == "thordone")) continue;
                    
                    Console.WriteLine($"Processing #{issue.Number}");
                    await HandleIssue(issue);
                    Console.WriteLine($"Done with #{issue.Number}");
                }

                await Task.Delay(TimeSpan.FromMinutes(5)); // Check every 5 minutes
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring issues: {ex}");
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }

    private async Task HandleIssue(Issue issue)
    {
        try
        {
            // Get repository content
            var repoFiles = await GetRepositoryContents();
            
            // Load the important files
            repoFiles = await DecideImportantFiles(issue, repoFiles);
            
            // Generate context for Claude
            var context = GenerateContext(issue, repoFiles);

            // Get solution from Claude
            var solution = await GetSolutionFromClaude(context);

            // Create branch and apply changes
            await CreateBranchAndApplyChanges(issue, solution);

            // Create pull request
            await CreatePullRequest(issue, solution);

            // Comment on issue
            await _github.Issue.Comment.Create(_repoOwner, _repoName, issue.Number,
                "I've created a pull request with a proposed solution. Please review.");

            // Close issue
            var issueUpdate = issue.ToUpdate();
            issueUpdate.State = ItemState.Closed;
            await _github.Issue.Update(_repoOwner, _repoName, issue.Number, issueUpdate);
        }
        catch (Exception ex)
        {
            await _github.Issue.Comment.Create(_repoOwner, _repoName, issue.Number,
                $"I encountered an error while trying to fix this issue: {ex.Message}");
            throw;
        }
    }

    private async Task<Dictionary<string, string>> GetRepositoryContents(string? path = null)
    {
        var contents = new Dictionary<string, string>();
        
        IReadOnlyList<RepositoryContent>? files;
        if (path is not null)
        {
            files = await _github.Repository.Content.GetAllContents(_repoOwner, _repoName, path);
            
        }
        else
        {
            files = await _github.Repository.Content.GetAllContents(_repoOwner, _repoName);
        }

        foreach (var file in files)
        {
            if (file.Type == ContentType.File)
            {
                contents[file.Path] = "";
            } else if (file.Type == ContentType.Dir)
            {
                contents = contents.Concat(await GetRepositoryContents(file.Path)).ToDictionary(it=>it.Key, it=>it.Value);
            }
        }

        return contents;
    }

    private async Task<string?> GetFileContents(string path)
    {
        try
        {
            var files = await _github.Repository.Content.GetAllContents(_repoOwner, _repoName, path);

            if (!files.Any())
            {
                return null;
            }

            RepositoryContent? file = files[0];
            return file?.Type == ContentType.File ? file.Content : null;
        }
        catch (NotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file contents for {path}: {ex.Message}");
            throw;
        }
    }
    
    private async Task<Dictionary<string, string>> DecideImportantFiles(Issue issue, Dictionary<string, string> repoFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");
        sb.AppendLine("\nRepository files:");

        foreach (var file in repoFiles)
        {
            sb.AppendLine($"\n#FILE {file.Key}#");
        }

        sb.AppendLine("\nPlease provide:");
        sb.AppendLine("1. A list of files that need to be read/modified");
        sb.AppendLine("All files must be provided in the following format.");
        sb.AppendLine("#FILE <filepath>#");
        
        var message = new Message
        {
            Role = RoleType.User,
            Content = [new TextContent(){Text = sb.ToString()}]
        };

        MessageResponse? response = await _claude.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = "claude-3-5-sonnet-20241022",
            Messages = new List<Message> { message },
            MaxTokens = 4096,
            Stream = false,
            Temperature = 0.3m
        });

        // Parse Claude's response to extract file changes and description
        // This is a simplified example - you'd need to implement proper parsing based on Claude's response format
        var filesToLoad = new Dictionary<string, string>();
        var description = response.Message.ToString() ?? "<NO CONTENT>";

        foreach (Match match in Regex.Matches(description, @"#FILE (.*?)#"))
        {
            if (match.Groups.Count > 1)
            {
                filesToLoad[match.Groups[1].Value] = await GetFileContents(match.Groups[1].Value) ?? "<EMPTY>";
            }
        }

        return filesToLoad;
    }

    private string GenerateContext(Issue issue, Dictionary<string, string> repoFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");
        sb.AppendLine("\nRepository files:");

        foreach (var file in repoFiles)
        {
            sb.AppendLine($"\n#FILE {file.Key}#");
            sb.AppendLine("#CONTENT#");
            sb.AppendLine(file.Value);
            sb.AppendLine("#ENDCONTENT#");
        }

        sb.AppendLine("\nPlease provide:");
        sb.AppendLine("1. A list of files that need to be modified");
        sb.AppendLine("2. The new content for each file");
        sb.AppendLine("3. A description of the changes made");
        sb.AppendLine("All changed files must be provided in the following format. The file should contain the entirety of the content, not just the changes.");
        sb.AppendLine("#FILE <filepath>#");
        sb.AppendLine("#CONTENT#");
        sb.AppendLine("<file content>");
        sb.AppendLine("#ENDCONTENT#");
        sb.AppendLine("The description must be provided in the following format.");
        sb.AppendLine("#DESCRIPTION#");
        sb.AppendLine("<description>");
        sb.AppendLine("#ENDDESCRIPTION#");

        return sb.ToString();
    }

    private async Task<(Dictionary<string, string> FileChanges, string Description)> GetSolutionFromClaude(string context)
    {
        var message = new Message
        {
            Role = RoleType.User,
            Content = [new TextContent(){Text = context}]
        };

        var response = await _claude.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = "claude-3-5-sonnet-20241022",
            Messages = new List<Message> { message },
            MaxTokens = 4096,
            Stream = false,
            Temperature = 0.3m
        });

        // Parse Claude's response to extract file changes and description
        // This is a simplified example - you'd need to implement proper parsing based on Claude's response format
        var fileChanges = new Dictionary<string, string>();
        var description = response.Message.ToString() ?? "<NO CONTENT>";

        foreach (Match match in Regex.Matches(description, @"#FILE (.*?)#\n#CONTENT#\n(.*?)\n#ENDCONTENT#", RegexOptions.Singleline))
        {
            if (match.Groups.Count > 2)
            {
                fileChanges[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }
        
        Console.WriteLine("Changes to the following files were made:");

        foreach (var fileChange in fileChanges)
        {
            Console.WriteLine($"{fileChange.Key}:");
        }

        description = Regex.Match(description, @"#DESCRIPTION#\n(.*?)\n#ENDDESCRIPTION#", RegexOptions.Singleline).Groups[1].Value;

        return (fileChanges, description);
    }

    private async Task CreateBranchAndApplyChanges(Issue issue, 
        (Dictionary<string, string> FileChanges, string Description) solution)
    {
        // Get reference to main branch
        var main = await _github.Git.Reference.Get(_repoOwner, _repoName, "heads/master");
        
        // Create new branch
        var branchRef = $"refs/heads/{_botBranch}-{issue.Number}";

        try
        {
            await _github.Git.Reference.Get(_repoOwner, _repoName, branchRef);
        }
        catch (Exception e)
        {
            await _github.Git.Reference.Create(_repoOwner, _repoName, 
                new NewReference(branchRef, main.Object.Sha));
        }

        // Apply changes to each file
        foreach (var change in solution.FileChanges)
        {
            var existingFile = await _github.Repository.Content.GetAllContents(
                _repoOwner, _repoName, change.Key);

            await _github.Repository.Content.UpdateFile(
                _repoOwner,
                _repoName,
                change.Key,
                new UpdateFileRequest(
                    $"Fix for issue #{issue.Number}",
                    change.Value,
                    existingFile[0].Sha,
                    branchRef.Replace("refs/heads/", "")
                ){Committer = new Committer("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now)}
            );
        }
    }

    private async Task CreatePullRequest(Issue issue, 
        (Dictionary<string, string> FileChanges, string Description) solution)
    {
        var pr = new NewPullRequest(
            $"Fix for issue #{issue.Number}",
            $"{_botBranch}-{issue.Number}",
            "master"
        )
        {
            Body = $"This PR addresses issue #{issue.Number}\n\n{solution.Description}"
        };

        await _github.PullRequest.Create(_repoOwner, _repoName, pr);
    }
}