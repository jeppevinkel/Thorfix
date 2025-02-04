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
    public async Task IssueAddComment([FunctionParameter("The markdown comment to add", true)] string comment)
    {
        await _client.Issue.Comment.Create(_issue.Repository.Id, _issue.Number,
            $"[FROM THOR]\n\n{comment}");
    }
}