using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using LibGit2Sharp;
using Octokit;
using Thorfix.Tools;
using Credentials = Octokit.Credentials;
using Repository = LibGit2Sharp.Repository;
using Tool = Anthropic.SDK.Common.Tool;

namespace Thorfix;

public class Thorfix
{
    private readonly GitHubClient _github;
    private readonly AnthropicClient _claude;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly IReadOnlyList<Tool> _tools;

    public Thorfix(string githubToken, string claudeApiKey, string repoOwner, string repoName)
    {
        _github = new GitHubClient(new ProductHeaderValue("IssueBot"))
        {
            Credentials = new Credentials(githubToken)
        };
        
        _claude = new AnthropicClient(claudeApiKey);
        _repoOwner = repoOwner;
        _repoName = repoName;

        _tools = Tool.GetAllAvailableTools(true, true, true);
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
                    Labels = { "thorfix" },
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
        Repository.Clone($"https://github.com/{_repoOwner}/{_repoName}.git", $"/app/repository/{_repoName}");

        var messages = new List<Message>()
        {
            new Message(RoleType.User, GenerateContext(issue))
        };

        FileSystemTools fileSystemTools = new FileSystemTools();
        GithubTools githubTools = new GithubTools(_github, issue);

        var tools = new List<Tool>
        {
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ReadFile)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ListFiles)),
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

            foreach (Function? toolCall in res.ToolCalls)
            {
                var response = await toolCall.InvokeAsync<string>();

                messages.Add(new Message(toolCall, response));
            }
        } while (res.ToolCalls?.Count > 0);
        
        Directory.Delete($"/app/repository/{_repoName}");
    }
    
    private static string GenerateContext(Issue issue)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");

        sb.AppendLine("\nUse any tools at your disposal to solve the issue. Your task will be considered finished when you no longer make any tool calls.");

        return sb.ToString();
    }
}