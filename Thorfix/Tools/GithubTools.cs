using Anthropic.SDK.Common;
using LibGit2Sharp;
using Octokit;
using Branch = LibGit2Sharp.Branch;
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
    
    public GithubTools(GitHubClient client, Issue issue, Repository repository, Branch? branch, UsernamePasswordCredentials credentials, string branchName, string repoOwner, string repoName)
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
    public async Task<ToolResult> IssueAddComment([FunctionParameter("The markdown comment to add", true)] string comment)
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
        Console.WriteLine("Convert issue to pull request");
        try
        {
            PullRequest? pullRequest = await _client.PullRequest.Create(_repoOwner, _repoName, new NewPullRequest(_issue.Id, _branchName, "master"));
            return new ToolResult("Converted the issue into a pull request");
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
            commitMessage += $"\n#{_issue.Number}";
            _repository.Commit(commitMessage, new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now),
                new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now));
            
            PushChanges(_repository, _branch);

            return Task.FromResult(new ToolResult("Commited changes successfully"));
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:CommitChanges " + e.Message);
            
            return Task.FromResult(new ToolResult(e.ToString(), true));
        }
    }
    
    public void PushChanges(Repository repository, Branch? branch = null)
    {
        try
        {
            var pushOptions = new PushOptions
            {
                CredentialsProvider = (_, _, _) => _credentials
            };
            repository.Network.Push(branch ?? repository.Head, pushOptions);
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception:RepoActions:PushChanges " + e.Message);
        }
    }
}