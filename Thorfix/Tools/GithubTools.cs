using Anthropic.SDK.Common;
using Octokit;

namespace Thorfix.Tools;

public class GithubTools
{
    private readonly GitHubClient _client;
    private readonly Issue _issue;
    private readonly string _repoOwner;
    private readonly string _repoName;
    
    public GithubTools(GitHubClient client, Issue issue, string repoOwner, string repoName)
    {
        _client = client;
        _issue = issue;
        _repoOwner = repoOwner;
        _repoName = repoName;
    }
    
    [Function("Adds a comment to an issue")]
    public async Task<string> IssueAddComment([FunctionParameter("The markdown comment to add", true)] string comment)
    {
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
            return "Comment added successfully";
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
}