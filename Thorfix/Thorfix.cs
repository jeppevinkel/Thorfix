using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using LibGit2Sharp;
using Octokit;
using Thorfix.Tools;
using Branch = LibGit2Sharp.Branch;
using Credentials = Octokit.Credentials;
using Repository = LibGit2Sharp.Repository;
using Signature = LibGit2Sharp.Signature;
using Tool = Anthropic.SDK.Common.Tool;

namespace Thorfix;

public class Thorfix
{
    private readonly GitHubClient _github;
    private readonly AnthropicClient _claude;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly UsernamePasswordCredentials _usernamePasswordCredentials;

    public Thorfix(string githubToken, string claudeApiKey, string repoOwner, string repoName)
    {
        _github = new GitHubClient(new ProductHeaderValue("IssueBot"))
        {
            Credentials = new Credentials(githubToken)
        };

        _claude = new AnthropicClient(claudeApiKey);
        _repoOwner = repoOwner;
        _repoName = repoName;

        _usernamePasswordCredentials = new UsernamePasswordCredentials()
        {
            Username = "thorfix",
            Password = githubToken,
        };
    }

    public async Task MonitorAndHandleIssues(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var issues = await _github.Issue.GetAllForRepository(_repoOwner, _repoName, new RepositoryIssueRequest
                {
                    State = ItemStateFilter.Open,
                    Labels = {"thorfix"},
                });

                foreach (Issue? issue in issues)
                {
                    var comments = await _github.Issue.Comment.GetAllForIssue(_repoOwner, _repoName, issue.Number);
                    IssueComment? lastComment = comments?[^1];

                    if (lastComment?.Body.Contains("[FROM THOR]") ?? false)
                    {
                        continue;
                    }

                    if (issue.Labels.Any(l => l.Name == "thordone")) continue;

                    Console.WriteLine($"Processing #{issue.Number}");
                    try
                    {
                        await HandleIssue(issue);
                    }
                    catch (Exception ex)
                    {
                        await Console.Error.WriteLineAsync($"Failed with #{issue.Number}: {ex}");
                    }
                    finally
                    {
                        Directory.Delete($"/app/repository/{_repoName}");
                        Console.WriteLine($"Done with #{issue.Number}");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken); // Check every 5 minutes
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring issues: {ex}");
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
        }
    }

    private async Task HandleIssue(Issue issue)
    {
        using var repository = new Repository(Repository.Clone($"https://github.com/{_repoOwner}/{_repoName}.git",
            $"/app/repository/{_repoName}"));
        Branch? thorfixBranch = repository.Branches[$"origin/thorfix/{issue.Number}"];
        if (thorfixBranch is not null)
        {
            thorfixBranch = Commands.Checkout(repository, thorfixBranch);
        }
        else
        {
            thorfixBranch = repository.CreateBranch($"thorfix/{issue.Number}");
            thorfixBranch = Commands.Checkout(repository, thorfixBranch);
        }

        var messages = new List<Message>()
        {
            new Message(RoleType.User, GenerateContext(issue))
        };

        FileSystemTools fileSystemTools = new FileSystemTools();
        GithubTools githubTools = new GithubTools(_github, issue, _repoOwner, _repoName);

        var tools = new List<Tool>
        {
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ReadFile)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ListFiles)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.PathFile)),
            Tool.GetOrCreateTool(githubTools, nameof(GithubTools.IssueAddComment)),
        };

        var parameters = new MessageParameters()
        {
            Messages = messages,
            MaxTokens = 4048,
            Model = AnthropicModels.Claude35Sonnet,
            Stream = false,
            Temperature = 1.0m,
            Tools = tools
        };

        MessageResponse? res;
        do
        {
            res = await _claude.Messages.GetClaudeMessageAsync(parameters);

            messages.Add(res.Message);

            foreach (Function? toolCall in res.ToolCalls)
            {
                var response = await toolCall.InvokeAsync<string>();

                messages.Add(new Message(toolCall, response));
            }
        } while (res.ToolCalls?.Count > 0);

        StageChanges(repository);
        CommitChanges(repository, $"Thorfix: {issue.Number}");
        PushChanges(repository);
    }

    public void StageChanges(Repository repository)
    {
        try
        {
            Commands.Stage(repository, "*");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception:RepoActions:StageChanges " + ex.Message);
        }
    }

    public void CommitChanges(Repository repository, string commitMessage)
    {
        try
        {
            repository.Commit(commitMessage, new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now),
                new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now));
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:CommitChanges " + e.Message);
        }
    }

    public void PushChanges(Repository repository, Branch? branch = null)
    {
        try
        {
            Remote remote = repository.Network.Remotes["origin"];
            var options = new PushOptions();
            options.CredentialsProvider = (_, _, _) => _usernamePasswordCredentials;
            if (branch is not null)
            {
                repository.Network.Push(branch, options);
            }
            else
            {
                repository.Network.Push(remote, repository.Head.FriendlyName, options);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:PushChanges " + e.Message);
        }
    }

    private static string GenerateContext(Issue issue)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");

        sb.AppendLine(
            "\nUse any tools at your disposal to solve the issue. Your task will be considered finished when you no longer make any tool calls.");
        sb.AppendLine(@"The tool to modify files uses patches to define the modifications.
The format of the patches is as following:
@@ -1,6 +1,7 @@
 Line 3
-Line 4
+Modified Line 4
 Line 5
 Line 6
+New Line
 Line 7
 Line 8
Where the numbers after @@ - represent the line numbers in the original file and the numbers after + represent the line numbers in the modified file.");

        return sb.ToString();
    }
}