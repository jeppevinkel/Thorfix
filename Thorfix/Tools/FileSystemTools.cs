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
        if (filePath.StartsWith('\\') || filePath.StartsWith('/'))
        {
            filePath = filePath[1..];
        }
        Console.WriteLine($"{Path.Combine(RootDirectory.FullName, filePath)} [{RootDirectory.FullName}, {filePath}]");
        filePath = Path.Combine(RootDirectory.FullName, filePath);
        Console.WriteLine($"Read the contents of {filePath}");
        if (!IsPathAllowed(filePath))
        {
            return $"Path was outside the allowed root directory ({RootDirectory.FullName})";
        }

        try
        {
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception e)
        {
            var message = e.Message;
            if (message.Contains(RootDirectory.FullName))
            {
                message = message.Replace(RootDirectory.FullName, "");
            }
            return $"Failed to read file: {message}";
        }
    }

    [Function("List all files in the repository")]
    public Task<string> ListFiles()
    {
        var files = Directory.GetFiles(RootDirectory.FullName, "*", SearchOption.AllDirectories).Select(it => it.Replace(RootDirectory.FullName, ""));
        return Task.FromResult(string.Join("\n", files));
    }

    [Function("Apply a patch to a file in the repository")]
    public async Task<string> PatchFile([FunctionParameter("Path to the file", true)] string filePath, [FunctionParameter("The patch to apply in the format defined earlier", true)] string patch)
    {
        filePath = Path.Combine(RootDirectory.FullName, filePath);
        Console.WriteLine($"Modify the contents of {filePath}");
        if (!IsPathAllowed(filePath))
        {
            return $"Path was outside the allowed root directory ({RootDirectory.FullName})";
        }
        
        try
        {
            Console.WriteLine("<START>");
            Console.WriteLine(await File.ReadAllTextAsync(filePath));
            await Patcher.ApplyPatchAsync(
                filePath,
                patch
            );
            Console.WriteLine("<>");
            Console.WriteLine(await File.ReadAllTextAsync(filePath));
            Console.WriteLine("<END>");

            return $"{filePath} modified successfully.";
        }
        catch (Exception e)
        {
            return $"Error while modifying {filePath}: {e.Message}";
        }
        
    }

    private static bool IsPathAllowed(string filePath)
    {
        var directoryInfo = new DirectoryInfo(filePath);

        return directoryInfo.FullName.StartsWith(RootDirectory.FullName);
    }
}