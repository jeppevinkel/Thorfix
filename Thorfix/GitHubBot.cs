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
            var filesToProcess = await DecideImportantFiles(issue, repoFiles);
            
            // Load the actual content for each file
            var loadedFiles = new Dictionary<string, string>();
            foreach (var file in filesToProcess)
            {
                var content = await GetFileContents(file.Key);
                if (content != null)
                {
                    loadedFiles[file.Key] = content;
                }
            }
            
            // Generate context for Claude
            var context = GenerateContext(issue, loadedFiles);

            // Get solution from Claude
            var solution = await GetSolutionFromClaude(context);

            // Validate and parse the patches
            var patches = ParsePatches(solution.FileChanges);
            
            // Create branch and apply changes
            await CreateBranchAndApplyChanges(issue, patches, solution.Description);

            // Create pull request
            await CreatePullRequest(issue, solution.Description);

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

    private Dictionary<string, string> ParsePatches(Dictionary<string, string> changes)
    {
        var patches = new Dictionary<string, string>();
        foreach (var change in changes)
        {
            // Create a temporary file with original content
            var tempOriginal = Path.GetTempFileName();
            File.WriteAllText(tempOriginal, change.Value);
            
            // Create patch file
            var tempPatch = Path.GetTempFileName();
            var patchContent = $"diff --git a/{change.Key} b/{change.Key}\n" +
                             $"--- a/{change.Key}\n" +
                             $"+++ b/{change.Key}\n" +
                             change.Value;
            
            File.WriteAllText(tempPatch, patchContent);
            patches[change.Key] = tempPatch;
        }
        return patches;
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
            sb.AppendLine($"\ndiff --git a/{file.Key} b/{file.Key}");
            sb.AppendLine($"--- a/{file.Key}");
            sb.AppendLine($"+++ b/{file.Key}");
            sb.AppendLine(file.Value);
        }

        sb.AppendLine("\nPlease provide:");
        sb.AppendLine("1. A patch for each file that needs to be modified");
        sb.AppendLine("2. A description of the changes made");
        sb.AppendLine("All changes must be provided in git patch format:");
        sb.AppendLine("diff --git a/<filepath> b/<filepath>");
        sb.AppendLine("--- a/<filepath>");
        sb.AppendLine("+++ b/<filepath>");
        sb.AppendLine("<patch content>");
        sb.AppendLine("The description must be provided in the following format:");
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
            Temperature = 1.0m
        });

        var fileChanges = new Dictionary<string, string>();
        var responseText = response.Message.ToString() ?? "<NO CONTENT>";

        var patchMatches = Regex.Matches(responseText, 
            @"diff --git a/(.*?) b/.*?\n(?:.*?\n)*?(?=(?:diff --git|#DESCRIPTION#)|$)", 
            RegexOptions.Singleline);
        
        foreach (Match match in patchMatches)
        {
            if (match.Groups.Count > 1)
            {
                fileChanges[match.Groups[1].Value] = match.Value;
            }
        }

        var description = Regex.Match(responseText, @"#DESCRIPTION#\n(.*?)\n#ENDDESCRIPTION#", 
            RegexOptions.Singleline).Groups[1].Value;

        return (fileChanges, description);
    }

    private async Task CreateBranchAndApplyChanges(Issue issue, Dictionary<string, string> patches, string description)
    {
        // Get reference to main branch
        var main = await _github.Git.Reference.Get(_repoOwner, _repoName, "heads/master");
        
        // Create new branch
        var branchRef = $"refs/heads/{_botBranch}-{issue.Number}";

        try
        {
            await _github.Git.Reference.Get(_repoOwner, _repoName, branchRef);
        }
        catch (Exception)
        {
            await _github.Git.Reference.Create(_repoOwner, _repoName, 
                new NewReference(branchRef, main.Object.Sha));
        }

        // Apply patches to each file
        foreach (var patch in patches)
        {
            var existingFile = await _github.Repository.Content.GetAllContents(
                _repoOwner, _repoName, patch.Key);

            // Apply patch using git apply
            var patchedContent = ApplyPatch(existingFile[0].Content, patch.Value);

            await _github.Repository.Content.UpdateFile(
                _repoOwner,
                _repoName,
                patch.Key,
                new UpdateFileRequest(
                    $"Fix for issue #{issue.Number}",
                    patchedContent,
                    existingFile[0].Sha,
                    branchRef.Replace("refs/heads/", "")
                ){Committer = new Committer("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now)}
            );
        }
    }

    private string ApplyPatch(string originalContent, string patchFile)
    {
        // Create temporary files
        var tempOriginal = Path.GetTempFileName();
        File.WriteAllText(tempOriginal, originalContent);
        
        // Apply patch using git apply
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"apply {patchFile} --unsafe-paths",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(tempOriginal)
            }
        };
        
        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Failed to apply patch: {process.StandardError.ReadToEnd()}");
        }

        // Read patched content
        var patchedContent = File.ReadAllText(tempOriginal);
        
        // Cleanup
        File.Delete(tempOriginal);
        File.Delete(patchFile);
        
        return patchedContent;
    }

    private async Task CreatePullRequest(Issue issue, string description)
    {
        var pr = new NewPullRequest(
            $"Fix for issue #{issue.Number}",
            $"{_botBranch}-{issue.Number}",
            "master"
        )
        {
            Body = $"This PR addresses issue #{issue.Number}\n\n{description}"
        };

        await _github.PullRequest.Create(_repoOwner, _repoName, pr);
    }

    // Other methods remain unchanged...
}