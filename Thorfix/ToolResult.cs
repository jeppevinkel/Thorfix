namespace Thorfix;

public class ToolResult
{
    public string Response { get; init; }
    public bool IsError { get; init; } = false;

    public ToolResult(string response, bool isError = false)
    {
        Response = response;
        IsError = isError;
    }
}