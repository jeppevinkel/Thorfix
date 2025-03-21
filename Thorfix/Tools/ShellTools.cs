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

            var output = string.Join('\n', result.StandardOutput, result.StandardOutput);
            
            Console.WriteLine($"Output: {output}");
        
            return new ToolResult(output, !result.IsSuccess);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return new ToolResult(e.ToString(), true);
        }
    }
}