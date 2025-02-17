// Main entry point for the Thorfix application
namespace Thorfix;

internal static class Program
{
    private static async Task Main(string[] args)
    { 
        var thorfix = new Thorfix(
            Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "",
            Environment.GetEnvironmentVariable("CLAUDE_API_KEY") ?? "",
            Environment.GetEnvironmentVariable("REPO_OWNER") ?? "",
            Environment.GetEnvironmentVariable("REPO_NAME") ?? "",
            bool.TryParse(Environment.GetEnvironmentVariable("THORFIX_CONTINUOUS_MODE"), out bool continuousMode) && continuousMode
        );

        await thorfix.MonitorAndHandleIssues();
    }
}
