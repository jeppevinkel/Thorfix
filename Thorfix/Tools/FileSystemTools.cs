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
    public async Task<string> ModifyFile([FunctionParameter("Path to the file", true)] string filePath, [FunctionParameter(@"One or more SEARCH/REPLACE blocks following this exact format:
  ```
  <<<<<<< SEARCH
  [exact content to find]
  =======
  [new content to replace with]
  >>>>>>> REPLACE
  ```
  Critical rules:
  1. SEARCH content must match the associated file section to find EXACTLY:
     * Match character-for-character including whitespace, indentation, line endings
     * Include all comments, docstrings, etc.
  2. SEARCH/REPLACE blocks will ONLY replace the first match occurrence.
     * Including multiple unique SEARCH/REPLACE blocks if you need to make multiple changes.
     * Include *just* enough lines in each SEARCH section to uniquely match each set of lines that need to change.
     * When using multiple SEARCH/REPLACE blocks, list them in the order they appear in the file.
  3. Keep SEARCH/REPLACE blocks concise:
     * Break large SEARCH/REPLACE blocks into a series of smaller blocks that each change a small portion of the file.
     * Include just the changing lines, and a few surrounding lines if needed for uniqueness.
     * Do not include long runs of unchanging lines in SEARCH/REPLACE blocks.
     * Each line must be complete. Never truncate lines mid-way through as this can cause matching failures.
  4. Special operations:
     * To move code: Use two SEARCH/REPLACE blocks (one to delete from original + one to insert at new location)
     * To delete code: Use empty REPLACE section", true)] string diff)
    {
        filePath = GetFullPath(filePath);
        Console.WriteLine($"Modify the contents of {filePath}");
        if (!IsPathAllowed(filePath))
        {
            return $"Path was outside the allowed root directory ({RootDirectory.FullName})";
        }
        
        try
        {
            FileModifier.ModifyFile(filePath, diff);

            return $"{filePath} modified successfully.";
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error while modifying {filePath}: {e.Message}");
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
