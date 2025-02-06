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
                    IssueComment? lastComment = comments?.LastOrDefault();

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
                        Directory.Delete($"/app/repository/{_repoName}", true);
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
        Branch? thorfixBranch = repository.Branches.FirstOrDefault(it => it.UpstreamBranchCanonicalName.Contains($"thorfix/{issue.Number}"));
        // Branch? thorfixBranch = repository.Branches[$"origin/thorfix/{issue.Number}"];
        if (thorfixBranch is not null)
        {
            Console.WriteLine(thorfixBranch.FriendlyName);
            Console.WriteLine(thorfixBranch.CanonicalName);
            thorfixBranch = repository.Branches[thorfixBranch.FriendlyName];
            Commands.Checkout(repository, thorfixBranch);
        }
        else
        {
            Console.WriteLine("Creating branch.");
            var newBranchName = await GenerateBranchName(issue);
            thorfixBranch = CreateRemoteBranch(repository, $"thorfix/{issue.Number}-{newBranchName}", "master");
            // thorfixBranch = repository.Branches[$"thorfix/{issue.Number}-{newBranchName}"];
            Commands.Checkout(repository, thorfixBranch);
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
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.PatchFile)),
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
        PushChanges(repository, thorfixBranch);
    }

    public void StageChanges(Repository repository)
    {
        try
        {
            PrintStatus(repository);
            Commands.Stage(repository, "*");
            PrintStatus(repository);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception:RepoActions:StageChanges " + ex.Message);
        }
    }
    
    public void PrintStatus(Repository repository)
    {
        try
        {
            var status = repository.RetrieveStatus();
            Console.WriteLine("Status: " + status);
            
            var changes = repository.Diff.Compare<TreeChanges>();
            foreach (var change in changes)
            {
                Console.WriteLine($"{change.Status} {change.Path}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception:RepoActions:PrintStatus " + ex.Message);
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
            var pushOptions = new PushOptions
            {
                CredentialsProvider = (_, _, _) => _usernamePasswordCredentials
            };
            if (branch is not null)
            {
                repository.Network.Push(branch, pushOptions);
            }
            else
            {
                repository.Network.Push(repository.Head, pushOptions);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:PushChanges " + e.Message);
        }
    }

    private void PushAllBranches(Repository localRepository, bool exceptMain = true)
    {
        var pushOptions = new PushOptions
        {
            CredentialsProvider = (_, _, _) => _usernamePasswordCredentials
        };
        
        foreach (Branch branch in localRepository.Branches)
        {
            try
            {
                Remote branchRemote = localRepository.Network.Remotes[branch.RemoteName];
                string pushRefSpec = string.Format("+{0}:{0}", branch.CanonicalName.Replace("refs/remotes/origin/", "refs/heads/"));
                localRepository.Network.Push(branchRemote, pushRefSpec, pushOptions);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception:RepoActions:PushChanges " + e.Message);
            }
        }
    }
    
    private Branch? CreateRemoteBranch(Repository repository, string branchName, string sourceBranchName)
    {
        Branch? sourceBranch = repository.Branches[$"origin/{sourceBranchName}"];
        Branch? remoteBranch = repository.Branches[$"origin/{branchName}"];
        
        var pushOptions = new PushOptions
        {
            CredentialsProvider = (_, _, _) => _usernamePasswordCredentials
        };
        
        if (remoteBranch == null)
        {
            Branch? localBranch = repository.CreateBranch(branchName, sourceBranch.Tip);
            
            Remote? remote = repository.Network.Remotes.First();
            repository.Branches.Update(localBranch, b => b.Remote = remote.Name, b => b.UpstreamBranch = localBranch.CanonicalName);
            repository.Network.Push(localBranch, pushOptions);
            return localBranch;
        }

        Console.WriteLine($"Can't create branch '{branchName}' because it already exists.");
        return null;
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

    private async Task<string> GenerateBranchName(Issue issue)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");
        sb.AppendLine("Come up with a short concise and descriptive name for a branch to work on this issue.");
        sb.AppendLine("You must respond with the branch name and nothing else. The name should now contain spaces.");
        sb.AppendLine("The name should not contain spaces.");
        sb.AppendLine("The only allowed special characters are dashes and underscores.");
        sb.AppendLine("Don't respond with anything other than the branch name.");
        
        var messages = new List<Message>()
        {
            new(RoleType.User, sb.ToString())
        };
        
        var parameters = new MessageParameters()
        {
            Messages = messages,
            MaxTokens = 4048,
            Model = AnthropicModels.Claude35Sonnet,
            Stream = false,
            Temperature = 1.0m,
        };

        MessageResponse? res = await _claude.Messages.GetClaudeMessageAsync(parameters);

        var branchName = res.Message.ToString();

        return branchName.Replace(' ', '-');
    }
}