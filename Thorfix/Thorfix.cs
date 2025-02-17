using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly bool _continuousMode;
    private readonly int _prMergeDelayMinutes;

    public Thorfix(string githubToken, string claudeApiKey, string repoOwner, string repoName, bool continuousMode = false)
    {
        _github = new GitHubClient(new ProductHeaderValue("IssueBot"))
        {
            Credentials = new Credentials(githubToken)
        };

        _claude = new AnthropicClient(claudeApiKey);
        _repoOwner = repoOwner;
        _repoName = repoName;
        _continuousMode = continuousMode;
        _prMergeDelayMinutes = int.TryParse(Environment.GetEnvironmentVariable("THORFIX_PR_MERGE_DELAY_MINUTES"), out int delay) ? delay : 6;

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

                bool handledIssue = false;
                foreach (Issue? issue in issues)
                {
                    var comments = await _github.Issue.Comment.GetAllForIssue(_repoOwner, _repoName, issue.Number);
                    IssueComment? lastComment = comments?.LastOrDefault();

                    if (lastComment?.Body.Contains("[FROM THOR]") ?? false)
                    {
                        continue;
                    }

                    if (issue.Labels.Any(l => l.Name == "thordone")) continue;

                    handledIssue = true;

                    Console.WriteLine($"Processing #{issue.Number}");
                    try
                    {
                        if (Directory.Exists($"/app/repository/{_repoName}"))
                        {
                            Directory.Delete($"/app/repository/{_repoName}", true);
                        }

                        await HandleIssue(issue);
                        
                        if (_continuousMode)
                        {
                            // Create a follow-up issue to continue development
                            await CreateFollowUpIssue(issue);
                        }
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

                // Create a new issue if no applicable issues were found
                if (!handledIssue && _continuousMode)
                {
                    using var repository = new Repository(Repository.Clone($"https://github.com/{_repoOwner}/{_repoName}.git",
                        $"/app/repository/{_repoName}"));
                    await CreateFollowUpIssue();
                    Directory.Delete($"/app/repository/{_repoName}", true);
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }

                await Task.Delay(_continuousMode ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5), cancellationToken);
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
        Branch? thorfixBranch;
        Branch? trackingBranch =
            repository.Branches.FirstOrDefault(it =>
                it.UpstreamBranchCanonicalName.Contains($"thorfix/{issue.Number}"));

        string? branchName;

        if (trackingBranch is not null)
        {
            Console.WriteLine(trackingBranch.FriendlyName);

            branchName = $"thorfix/{issue.Number}-{trackingBranch.FriendlyName.Replace("origin/", "")}";

            thorfixBranch = repository.Head;
            repository.Branches.Update(thorfixBranch, b => b.TrackedBranch = trackingBranch.CanonicalName);

            var pullOptions = new PullOptions()
            {
                MergeOptions = new MergeOptions()
                {
                    FastForwardStrategy = FastForwardStrategy.Default
                }
            };

            MergeResult mergeResult = Commands.Pull(
                repository,
                new Signature("Thorfix", "thorfix@jeppdev.com", DateTimeOffset.Now),
                pullOptions
            );
        }
        else
        {
            Console.WriteLine("Creating branch.");
            var newBranchName = await GenerateBranchName(issue);
            branchName = $"thorfix/{issue.Number}-{newBranchName}";
            thorfixBranch = CreateRemoteBranch(repository, branchName, "master");
            Commands.Checkout(repository, thorfixBranch);
        }

        var messages = new List<Message>()
        {
            new(RoleType.User, await GenerateContext(issue))
        };

        FileSystemTools fileSystemTools = new FileSystemTools();
        GithubTools githubTools = new GithubTools(_github, issue, repository, thorfixBranch,
            _usernamePasswordCredentials, branchName, _repoOwner, _repoName);

        var tools = new List<Tool>
        {
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ReadFile)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ListFiles)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.WriteFile)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.DeleteFile)),
            Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ModifyFile)),
            Tool.GetOrCreateTool(githubTools, nameof(GithubTools.IssueAddComment)),
            Tool.GetOrCreateTool(githubTools, nameof(GithubTools.CommitChanges)),
        };

        var parameters = new MessageParameters()
        {
            Messages = messages,
            MaxTokens = 4048,
            Model = AnthropicModels.Claude35Sonnet,
            Stream = false,
            Temperature = 1.0m,
            Tools = tools,
            PromptCaching = PromptCacheType.Messages | PromptCacheType.Tools
        };

        MessageResponse? res;
        bool isComplete = false;
        int iterations = 0;

        while (!isComplete || iterations++ > 10)
        {
            res = await GetClaudeMessageAsync(parameters);
            parameters.Messages.Add(res.Message);

            // Process tool calls
            foreach (Function? toolCall in res.ToolCalls)
            {
                var result = await toolCall.InvokeAsync<ToolResult>();
                parameters.Messages.Add(new Message(toolCall, result.Response, result.IsError));
            }

            if (res.ToolCalls?.Count == 0)
            {
                // No more tool calls - let's check if the changes satisfy the requirements
                var changes = repository.Diff.Compare<TreeChanges>();

                // print number of changes and changed files
                Console.WriteLine($"Number of changes: {changes.Count()}");
                foreach (TreeEntryChanges? change in changes)
                {
                    Console.WriteLine($"{change.Status} {change.Path}");
                }

                // Test if the code can be built
                if (changes.Any())
                {
                    Console.WriteLine("Testing if code can be built...");
                    try
                    {
                        var buildProcess = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "dotnet",
                                Arguments = "build",
                                WorkingDirectory = $"/app/repository/{_repoName}",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        buildProcess.Start();
                        var output = await buildProcess.StandardOutput.ReadToEndAsync();
                        var error = await buildProcess.StandardError.ReadToEndAsync();
                        await buildProcess.WaitForExitAsync();

                        if (buildProcess.ExitCode != 0)
                        {
                            Console.WriteLine("Build failed!");
                            Console.WriteLine($"Build output: {output}");
                            Console.WriteLine($"Build errors: {error}");

                            var outputText = !string.IsNullOrWhiteSpace(error) ? error : output;

                            // Add build failure comment
                            var failureComment = new StringBuilder();
                            failureComment.AppendLine("⚠️ Build Failure");
                            failureComment.AppendLine();
                            failureComment.AppendLine("The changes I made resulted in build failures:");
                            failureComment.AppendLine();
                            failureComment.AppendLine("```");
                            failureComment.AppendLine(outputText);
                            failureComment.AppendLine("```");
                            failureComment.AppendLine();
                            failureComment.AppendLine("I'll review and fix these build errors before proceeding.");

                            await githubTools.IssueAddComment(failureComment.ToString());

                            // We have changes - let's verify them
                            parameters.Messages.Add(new Message(RoleType.User,
                                $"The build failed with the following output: {outputText}"));

                            // Reset changes and continue the loop
                            // repository.Reset(ResetMode.Hard);
                            continue;
                        }

                        Console.WriteLine("Build successful!");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during build test: {ex.Message}");
                        throw;
                    }
                }

                // if (changes.Any())
                // {
                    // We have changes - let's verify them
                    parameters.Messages.Add(new Message(RoleType.User,
                        "Please review the changes made and confirm if they complete the requirements from the original issue. " +
                        "Do not suggest making unit tests unless explicitly requested. " +
                        "If they do, respond with just '[COMPLETE]'. If not, continue making necessary changes. " +
                        "Original issue description: " + issue.Body));

                    var verificationResponse = await GetClaudeMessageAsync(parameters);
                    parameters.Messages.Add(verificationResponse.Message);

                    // Process tool calls
                    foreach (Function? toolCall in verificationResponse.ToolCalls)
                    {
                        var result = await toolCall.InvokeAsync<ToolResult>();
                        parameters.Messages.Add(new Message(toolCall, result.Response, result.IsError));
                    }

                    var content = verificationResponse.Message.ToString()?.Trim();
                    Console.WriteLine($"Verification result: {content}");
                    if (content != null && content.Contains("[COMPLETE]", StringComparison.OrdinalIgnoreCase))
                    {
                        isComplete = true;
                        if (changes.Any())
                        {
                            GithubTools.StageChanges(repository);
                            CommitChanges(repository, $"Thorfix: #{issue.Number}");
                            GithubTools.PushChanges(repository, _usernamePasswordCredentials, thorfixBranch);
                        }

                        // Convert to pull request and handle merging
                        var convertResult = await githubTools.ConvertIssueToPullRequest();
                        if (convertResult.IsError)
                        {
                            await githubTools.IssueAddComment($"Failed to create pull request: {convertResult.Response}");
                            continue;
                        }

                        // Extract PR number from the success message
                        var prNumberMatch = Regex.Match(convertResult.Response, @"pull request #(\d+)");
                        if (!prNumberMatch.Success)
                        {
                            await githubTools.IssueAddComment("Failed to parse pull request number from response");
                            continue;
                        }

                        var prNumber = int.Parse(prNumberMatch.Groups[1].Value);
                        
                        if (_continuousMode)
                        {
                            try
                            {
                                // Add a comment about automatic merging
                                await _github.Issue.Comment.Create(_repoOwner, _repoName, issue.Number,
                                    "[FROM THOR]\n\nContinuous mode: Attempting automatic merge of this pull request. 🔄");
                                
                                // Add a configurable delay to account for potential build pipelines
                                await Task.Delay(TimeSpan.FromMinutes(_prMergeDelayMinutes));

                                // Get the full PR details needed for merge checks
                                var fullPullRequest = await _github.PullRequest.Get(_repoOwner, _repoName, prNumber);
                                
                                if (await CanMergePullRequest(fullPullRequest))
                                {
                                    // Create the merge
                                    var mergePullRequest = new MergePullRequest
                                    {
                                        CommitTitle = $"Thorfix: Auto-merge PR #{fullPullRequest.Number}",
                                        CommitMessage = $"Auto-merged by Thorfix continuous mode\n\nResolves #{issue.Number}",
                                        MergeMethod = PullRequestMergeMethod.Merge
                                    };

                                    // Try to merge the pull request
                                    await _github.PullRequest.Merge(_repoOwner, _repoName, prNumber, mergePullRequest);
                                    
                                    // Close the issue if merge was successful
                                    await _github.Issue.Update(_repoOwner, _repoName, issue.Number, new IssueUpdate
                                    {
                                        State = ItemState.Closed
                                    });

                                    await _github.Issue.Comment.Create(_repoOwner, _repoName, issue.Number,
                                        "[FROM THOR]\n\nContinuous mode: Successfully merged pull request and closed issue. ✅");
                                    
                                    // Add the thordone label
                                    await _github.Issue.Labels.AddToIssue(_repoOwner, _repoName, issue.Number, new[] { "thordone" });
                                }
                            }
                            catch (Exception ex)
                            {
                                await _github.Issue.Comment.Create(_repoOwner, _repoName, issue.Number,
                                    $"[FROM THOR]\n\nContinuous mode: Failed to auto-merge pull request. ⚠️\nError: {ex.Message}\n\nPlease review and merge manually.");
                            }
                        }
                        else
                        {
                            await githubTools.IssueAddComment(
                                "This issue has been deemed completed.");
                        }
                    }
                    else
                    {
                        // Add a detailed comment explaining why the changes don't meet requirements
                        var commentBuilder = new StringBuilder();
                        commentBuilder.AppendLine("⚠️ Code Review Results: Requirements Not Yet Met");
                        commentBuilder.AppendLine();
                        commentBuilder.AppendLine(
                            "I've reviewed the current code changes against the original requirements:");
                        commentBuilder.AppendLine();
                        commentBuilder.AppendLine("**Original Requirements:**");
                        commentBuilder.AppendLine(issue.Body);
                        commentBuilder.AppendLine();
                        commentBuilder.AppendLine("**Feedback on Current Implementation:**");
                        var feedbackContent = verificationResponse.Message.ToString()?.Trim() ?? "";
                        commentBuilder.AppendLine(feedbackContent);
                        commentBuilder.AppendLine();
                        commentBuilder.AppendLine("**Next Steps:**");
                        commentBuilder.AppendLine(
                            "1. ✍️ I will make additional modifications to address the gaps identified above");
                        commentBuilder.AppendLine("2. 🔍 Changes will be re-evaluated against requirements");
                        commentBuilder.AppendLine();
                        commentBuilder.AppendLine("I'll continue iterating until all requirements are met.");

                        await githubTools.IssueAddComment(commentBuilder.ToString());
                        
                        parameters.Messages.Add(new Message(RoleType.User,
                            "All requirements were not yet met. Continue working on the code."));

                        // Reset the changes since we're not done
                        // repository.Reset(ResetMode.Hard);
                    }
                // }
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private async Task<MessageResponse> GetClaudeMessageAsync(MessageParameters parameters)
    {
        var triesLeft = 3;
        while (triesLeft-- > 0)
        {
            try
            {
                return await _claude.Messages.GetClaudeMessageAsync(parameters);
            }
            catch (HttpRequestException requestException)
            {
                if ((int) requestException.StatusCode! != (int) AnthropicErrorCode.OverloadedError) throw;
                if (triesLeft <= 0)
                {
                    throw;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
                return await _claude.Messages.GetClaudeMessageAsync(parameters);
            }
        }

        throw new Exception("Failed to get Claude message after 3 tries");
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
            repository.Branches.Update(localBranch, b => b.Remote = remote.Name,
                b => b.UpstreamBranch = localBranch.CanonicalName);
            repository.Network.Push(localBranch, pushOptions);
            return localBranch;
        }

        Console.WriteLine($"Can't create branch '{branchName}' because it already exists.");
        return null;
    }

    private async Task<string> GenerateContext(Issue issue)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a software development bot. Your task is to fix the following issue:");
        sb.AppendLine($"Issue Title: {issue.Title}");
        sb.AppendLine($"Issue Description: {issue.Body}");

        // Get all comments on the issue
        var comments = await _github.Issue.Comment.GetAllForIssue(_repoOwner, _repoName, issue.Number);
        if (comments.Any())
        {
            sb.AppendLine("\nPrevious conversation history:");
            foreach (var comment in comments)
            {
                if (comment.Body.Contains("[FROM THOR]"))
                {
                    // Remove the [FROM THOR] marker and add as assistant message
                    sb.AppendLine("Assistant: " +
                                  comment.Body.Replace("[FROM THOR]\n\n", "").Replace("[FROM THOR]", "").Trim());
                }
                else
                {
                    sb.AppendLine("User: " + comment.Body.Trim());
                }

                sb.AppendLine(); // Add blank line between messages
            }
        }

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

        MessageResponse? res = await GetClaudeMessageAsync(parameters);

        var branchName = res.Message?.ToString().Trim() ?? "";

        return branchName.Replace(' ', '-');
    }

    private async Task<bool> CanMergePullRequest(PullRequest pullRequest)
    {
        try
        {
            // Check if PR is mergeable
            if (pullRequest.Mergeable != true)
            {
                await _github.Issue.Comment.Create(_repoOwner, _repoName, pullRequest.Number,
                    "[FROM THOR]\n\nCannot auto-merge: Pull request has conflicts that need to be resolved manually. 🔄");
                return false;
            }

            // Get PR status/checks
            var status = await _github.Repository.Status.GetAll(_repoOwner, _repoName, pullRequest.Head.Sha);
            
            // If there are any status checks and they're not all successful, don't merge
            if (status.Any() && status.Any(s => s.State != CommitState.Success))
            {
                var failedChecks = string.Join(", ", status.Where(s => s.State != CommitState.Success).Select(s => s.Context));
                await _github.Issue.Comment.Create(_repoOwner, _repoName, pullRequest.Number,
                    $"[FROM THOR]\n\nCannot auto-merge: The following checks are not passing: {failedChecks}");
                return false;
            }

            // Get required reviews
            try
            {
                BranchProtectionSettings? requiredReviews =
                    await _github.Repository.Branch.GetBranchProtection(_repoOwner, _repoName, pullRequest.Base.Ref);

                if (requiredReviews?.RequiredPullRequestReviews != null)
                {
                    var reviews = await _github.PullRequest.Review.GetAll(_repoOwner, _repoName, pullRequest.Number);
                    var approvalCount = reviews.Count(r => r.State == PullRequestReviewState.Approved);

                    if (approvalCount < requiredReviews.RequiredPullRequestReviews.RequiredApprovingReviewCount)
                    {
                        await _github.Issue.Comment.Create(_repoOwner, _repoName, pullRequest.Number,
                            "[FROM THOR]\n\nCannot auto-merge: Pull request requires additional approvals. 🔄");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                // ignored
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking merge status: {ex.Message}");
            await _github.Issue.Comment.Create(_repoOwner, _repoName, pullRequest.Number,
                $"[FROM THOR]\n\nError checking merge status: {ex.Message}");
            return false;
        }
    }

    private async Task CreateFollowUpIssue(Issue? completedIssue = null)
    {
        try
        {
            var fileSystemTools = new FileSystemTools();
            ToolResult allFiles = await fileSystemTools.ListFiles();
            var relevantFiles =  allFiles.Response.Split('\n').Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
                .ToList();

            // Let Claude analyze the codebase and create a new issue
            var messages = new List<Message>
            {
                new(RoleType.User,
                    "You are a software development bot. Your task is to create a new issue for improving the codebase.\n\n" +
                    "Using the file system tools available to you (ListFiles and ReadFile), analyze the codebase and suggest " +
                    "the next most important improvement. This could be:\n" +
                    "- New functionality\n" +
                    "- Improvements to existing code\n" +
                    "- Better error handling\n" +
                    "- Documentation improvements\n" +
                    "- Performance enhancements\n" +
                    "- Security improvements\n\n" +
                    "Your response should contain:\n" +
                    "1. A clear title for the improvement task\n" +
                    "2. A detailed description of what needs to be done and why it's important\n" +
                    "3. The first line of your response should be the title of the issue, with the description on subsequent lines\n" +
                    "4. Just these two items - no other text or explanations\n\n" +
                    "Available files:\n" + string.Join("\n", relevantFiles) + "\n\n" +
                    "Use the tools to read and analyze files as needed.")
            };

            var tools = new List<Tool>
            {
                Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ListFiles)),
                Tool.GetOrCreateTool(fileSystemTools, nameof(FileSystemTools.ReadFile))
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

            MessageResponse res = await GetClaudeMessageAsync(parameters);
            messages.Add(res.Message);
            
            // Process any tool calls first
            while (res.ToolCalls?.Count > 0)
            {
                foreach (Function? toolCall in res.ToolCalls)
                {
                    var result = await toolCall.InvokeAsync<ToolResult>();
                    messages.Add(new Message(toolCall, result.Response, result.IsError));
                }
                
                res = await GetClaudeMessageAsync(parameters);
                messages.Add(res.Message);
            }
            
            var suggestionLines = res.Message.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // First line is title, rest is description
            var newIssueTitle = suggestionLines[0].Trim();
            var newIssueBody = string.Join("\n", suggestionLines.Skip(1)).Trim();

            var newIssue = new NewIssue(newIssueTitle)
            {
                Body = newIssueBody
            };
            
            // Add the thorfix label to the new issue
            newIssue.Labels.Add("thorfix");
            
            var createdIssue = await _github.Issue.Create(_repoOwner, _repoName, newIssue);
            
            // Add a comment to the completed issue linking to the follow-up
            if (completedIssue is not null)
            {
                await _github.Issue.Comment.Create(_repoOwner, _repoName, completedIssue.Number,
                    $"[FROM THOR]\n\nIn continuous mode: Created follow-up issue #{createdIssue.Number} for further improvements. 🔄");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating follow-up issue: {ex.Message}");
            // Don't throw - we don't want to break the main flow if follow-up creation fails
        }
    }

    private string RemoveEmptyLines(string lines)
    {
        return Regex.Replace(lines, @"^\s*$\n|\r", string.Empty, RegexOptions.Multiline).TrimEnd();
    }
}