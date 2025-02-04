namespace Thorfix;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // var bot = new GitHubBot(
        //     Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
        //     Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "",
        //     Environment.GetEnvironmentVariable("REPO_OWNER") ?? "",
        //     Environment.GetEnvironmentVariable("REPO_NAME") ?? ""
        // );
        //
        // await bot.MonitorAndHandleIssues();
        
        var thorfix = new Thorfix(
            Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
            Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "",
            Environment.GetEnvironmentVariable("REPO_OWNER") ?? "",
            Environment.GetEnvironmentVariable("REPO_NAME") ?? ""
        );

        await thorfix.MonitorAndHandleIssues();
    }
}