using Anthropic.SDK.Common;

namespace Thorfix.Tools;

public class FileSystemTools
{
    
    private static readonly DirectoryInfo RootDirectory = new DirectoryInfo("/app/repository");

    public FileSystemTools()
    {
        
    }
    
    [Function("Reads a file from the filesystem")]
    public async Task<string> ReadFile([FunctionParameter("Path to the file", true)] string filePath)
    {
        var directoryInfo = new DirectoryInfo(filePath);

        if (!directoryInfo.FullName.StartsWith(RootDirectory.FullName))
        {
            throw new InvalidOperationException(
                $"Path was outside the allowed root directory ({RootDirectory.FullName})");
        }
        return await File.ReadAllTextAsync(filePath);
    }

    [Function("List all files in the repository")]
    public Task<string> ListFiles()
    {
        var files = Directory.GetFiles(RootDirectory.FullName, "*", SearchOption.AllDirectories).Select(it => it.Replace(RootDirectory.FullName, ""));
        return Task.FromResult(string.Join("\n", files));
    }
}