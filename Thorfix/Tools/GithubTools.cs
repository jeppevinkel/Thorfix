using Anthropic.SDK.Common;
using Octokit;

namespace Thorfix.Tools;

public class GithubTools
{
    private readonly GitHubClient _client;
    private readonly Issue _issue;
    
    public GithubTools(GitHubClient client, Issue issue)
    {
        _client = client;
        _issue = issue;
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

        var repositoryId = _issue.Repository.Id;

        try
        {
            await _client.Issue.Comment.Create(repositoryId, _issue.Number,
                $"[FROM THOR]\n\n{comment}");
            return "Comment added successfully";
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
}