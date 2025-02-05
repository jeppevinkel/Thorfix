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
        Console.WriteLine($"Read the contents of {filePath}");
        if (!IsPathAllowed(filePath))
        {
            return $"Path was outside the allowed root directory ({RootDirectory.FullName})";
        }
        return await File.ReadAllTextAsync(filePath);
    }

    [Function("List all files in the repository")]
    public Task<string> ListFiles()
    {
        var files = Directory.GetFiles(RootDirectory.FullName, "*", SearchOption.AllDirectories).Select(it => it.Replace(RootDirectory.FullName, ""));
        Console.WriteLine(string.Join("\n", files));
        return Task.FromResult(string.Join("\n", files));
    }

    [Function("Apply a patch to a file in the repository")]
    public async Task<string> ModifyFile([FunctionParameter("Path to the file", true)] string filePath, [FunctionParameter("The patch to apply", true)] string patch)
    {
        Console.WriteLine($"Modify the contents of {filePath}");
        if (!IsPathAllowed(filePath))
        {
            return $"Path was outside the allowed root directory ({RootDirectory.FullName})";
        }
        
        try
        {
            await Patcher.ApplyPatchAsync(
                filePath,
                patch
            );

            return $"{filePath} modified successfully.";
        }
        catch (Exception e)
        {
            return $"Error while modifying {filePath}: {e}";
        }
        
    }

    private static bool IsPathAllowed(string filePath)
    {
        var directoryInfo = new DirectoryInfo(filePath);

        return directoryInfo.FullName.StartsWith(RootDirectory.FullName);
    }
}