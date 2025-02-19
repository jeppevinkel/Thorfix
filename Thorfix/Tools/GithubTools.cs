using Anthropic.SDK.Common;
using LibGit2Sharp;
using Octokit;
using Branch = LibGit2Sharp.Branch;
using Credentials = LibGit2Sharp.Credentials;
using Repository = LibGit2Sharp.Repository;
using Signature = LibGit2Sharp.Signature;

namespace Thorfix.Tools;

public class GithubTools
{
    private readonly GitHubClient _client;
    private readonly Issue _issue;
    private readonly Repository _repository;
    private readonly Branch? _branch;
    private readonly UsernamePasswordCredentials _credentials;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly string _branchName;

    public GithubTools(GitHubClient client, Issue issue, Repository repository, Branch? branch,
        UsernamePasswordCredentials credentials, string branchName, string repoOwner, string repoName)
    {
        _client = client;
        _issue = issue;
        _repository = repository;
        _branch = branch;
        _credentials = credentials;
        _repoOwner = repoOwner;
        _repoName = repoName;
        _branchName = branchName;
    }

    [Function("Adds a comment to an issue")]
    public async Task<ToolResult> IssueAddComment(
        [FunctionParameter("The markdown comment to add", true)] string comment)
    {
        Console.WriteLine("Add comment to issue");
        if (_issue is null)
        {
            throw new NullReferenceException("Issue is null for some reason");
        }

        if (_client is null)
        {
            throw new NullReferenceException("Github client is null for some reason");
        }

        try
        {
            await _client.Issue.Comment.Create(_repoOwner, _repoName, _issue.Number,
                $"[FROM THOR]\n\n{comment}");
            return new ToolResult("Comment added successfully");
        }
        catch (Exception e)
        {
            return new ToolResult(e.ToString(), true);
        }
    }

    [Function("Converts the current issue into a pull request that targets the working branch")]
    public async Task<ToolResult> ConvertIssueToPullRequest()
    {
        Console.WriteLine($"Convert issue to pull request ({_branchName})");
        try
        {
            PullRequest? pullRequest = await _client.PullRequest.Create(_repoOwner, _repoName,
                new NewPullRequest(_issue.Title, _branchName.Replace("origin/", ""), await GetDefaultBranch(_client, _repoOwner, _repoName))
                {
                    Body = $"Fixes #{_issue.Number}"
                });
            return new ToolResult($"Converted issue #{_issue.Number} into pull request #{pullRequest.Number}");
        }
        catch (Exception e)
        {
            await Console.Error.WriteLineAsync(e.ToString());
            return new ToolResult(e.ToString(), true);
        }
    }

    [Function("Automatically merge a pull request if all checks pass")]
    public async Task<ToolResult> MergePullRequest(int pullRequestNumber)
    {
        try
        {
            var pr = await _client.PullRequest.Get(_repoOwner, _repoName, pullRequestNumber);
            
            if (!pr.Mergeable.GetValueOrDefault())
            {
                return new ToolResult("Pull request has conflicts and cannot be merged automatically", true);
            }

            // Check if the PR has any status checks
            var status = await _client.Repository.Status.GetCombined(_repoOwner, _repoName, pr.Head.Sha);
            if (status.State != CommitState.Success && status.State != CommitState.Pending)
            {
                return new ToolResult($"Pull request status checks are not passing. Current state: {status.State}", true);
            }

            var mergeResult = await _client.PullRequest.Merge(_repoOwner, _repoName, pullRequestNumber,
                new MergePullRequest { CommitTitle = $"Merge pull request #{pullRequestNumber} from {_branchName}" });

            if (mergeResult.Merged)
            {
                // Close the associated issue
                await _client.Issue.Update(_repoOwner, _repoName, _issue.Number, new IssueUpdate
                {
                    State = ItemState.Closed
                });
                
                return new ToolResult($"Successfully merged pull request #{pullRequestNumber} and closed issue #{_issue.Number}");
            }
            
            return new ToolResult($"Failed to merge pull request #{pullRequestNumber}: {mergeResult.Message}", true);
        }
        catch (Exception e)
        {
            return new ToolResult(e.ToString(), true);
        }
    }

    [Function("Commits changes to the repository")]
    public Task<ToolResult> CommitChanges([FunctionParameter("The commit message", true)] string commitMessage)
    {
        Console.WriteLine($"Create commit {commitMessage}");
        try
        {
            StageChanges(_repository);
            
            commitMessage += $"\n#{_issue.Number}";
            _repository.Commit(commitMessage, new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now),
                new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now));

            PushChanges(_repository, _credentials, _branch);

            return Task.FromResult(new ToolResult("Commited changes successfully"));
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:CommitChanges " + e.Message);

            return Task.FromResult(new ToolResult(e.ToString(), true));
        }
    }
    
    public static void StageChanges(Repository repository)
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

    public static void PushChanges(Repository repository, Credentials credentials, Branch? branch = null)
    {
        try
        {
            var pushOptions = new PushOptions
            {
                CredentialsProvider = (_, _, _) => credentials
            };
            repository.Network.Push(branch ?? repository.Head, pushOptions);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:PushChanges " + e.Message);
        }
    }

    public static async Task<string> GetDefaultBranch(GitHubClient github, string owner, string repoName)
    {
        Octokit.Repository? repo = await github.Repository.Get(owner, repoName);
        return repo.DefaultBranch;
    }
}