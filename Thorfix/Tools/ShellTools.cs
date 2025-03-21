using Anthropic.SDK.Common;
using CliWrap;
using CliWrap.Buffered;

namespace Thorfix.Tools;

public class ShellTools
{
    [Function("Runs a shell command on the system")]
    public async Task<ToolResult> RunShellCommand(
        [FunctionParameter("The command to run", true)]
        string command)
    {
        Console.WriteLine($"Run command: {command}");
        try
        {
            BufferedCommandResult result = await Cli.Wrap(command).ExecuteBufferedAsync();
        
            return new ToolResult(result.StandardOutput + '\n' + result.StandardOutput, !result.IsSuccess);
        }
        catch (Exception e)
        {
            return new ToolResult(e.ToString(), true);
        }
    }
}