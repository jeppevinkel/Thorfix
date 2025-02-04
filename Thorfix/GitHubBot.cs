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
    private bool _isProcessingIssue;

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
        _isProcessingIssue = false;
    }

    public async Task MonitorAndHandleIssues()
    {
        while (true)
        {
            try
            {
                if (_isProcessingIssue)
                {
                    // Check if there are any open pull requests created by the bot
                    var pullRequests = await _github.PullRequest.GetAllForRepository(_repoOwner, _repoName, new PullRequestRequest
                    {
                        State = ItemStateFilter.Open,
                        Head = $"{_repoOwner}:{_botBranch}"
                    });

                    if (!pullRequests.Any())
                    {
                        _isProcessingIssue = false;
                    }
                    else
                    {
                        // Wait before checking again
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        continue;
                    }
                }

                var issues = await _github.Issue.GetAllForRepository(_repoOwner, _repoName, new RepositoryIssueRequest
                {
                    State = ItemStateFilter.Open,
                    Labels = { "thorfix" },
                });

                foreach (Issue? issue in issues)
                {
                    if (issue.Labels.Any(l => l.Name == "thordone")) continue;
                    
                    Console.WriteLine($"Processing #{issue.Number}");
                    _isProcessingIssue = true;
                    await HandleIssue(issue);
                    Console.WriteLine($"Done with #{issue.Number}");
                    break; // Only process one issue at a time
                }

                await Task.Delay(TimeSpan.FromMinutes(5)); // Check every 5 minutes
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error monitoring issues: {ex}");
                _isProcessingIssue = false;
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }
    }

    // ... [rest of the code remains unchanged]
}