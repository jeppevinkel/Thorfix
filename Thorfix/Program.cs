namespace Thorfix;

class Program
{
    static async Task Main(string[] args)
    {
        var bot = new GitHubBot(
            Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
            Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "",
            Environment.GetEnvironmentVariable("REPO_OWNER") ?? "",
            Environment.GetEnvironmentVariable("REPO_NAME") ?? ""
        );

        await bot.MonitorAndHandleIssues();
    }
}