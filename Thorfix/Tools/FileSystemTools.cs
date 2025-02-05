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
        filePath = GetFullPath(filePath);
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
    public async Task<string> PatchFile([FunctionParameter("Path to the file", true)] string filePath, [FunctionParameter(@"The format of the patches is as following:
@@ -1,6 +1,7 @@
 Line 3
-Line 4
+Modified Line 4
 Line 5
 Line 6
+New Line
 Line 7
 Line 8
Where the numbers after @@ - represent the line numbers in the original file and the numbers after + represent the line numbers in the modified file.", true)] string patch)
    {
        filePath = GetFullPath(filePath);
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

    private static string GetFullPath(string filePath)
    {
        if (filePath.StartsWith('\\') || filePath.StartsWith('/'))
        {
            filePath = filePath[1..];
        }
        return Path.Combine(RootDirectory.FullName, filePath);
    }
}