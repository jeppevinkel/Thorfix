using System.Text.RegularExpressions;

namespace Thorfix;

public class FileModifier
{
    private const string SearchMarker = "<<<<<<< SEARCH";
    private const string DividerMarker = "=======";
    private const string ReplaceMarker = ">>>>>>> REPLACE";

    public static void ModifyFile(string path, string diffContent)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty", nameof(path));

        if (string.IsNullOrEmpty(diffContent))
            throw new ArgumentException("Diff content cannot be empty", nameof(diffContent));

        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        // Read the original file content
        var fileContent = File.ReadAllText(path);
        var modifiedContent = fileContent;

        // Parse and apply each SEARCH/REPLACE block
        var blocks = ParseDiffBlocks(diffContent);
        modifiedContent = blocks.Aggregate(modifiedContent, ApplyDiffBlock);

        // Write the modified content back to the file
        File.WriteAllText(path, modifiedContent);
    }

    private static List<(string Search, string Replace)> ParseDiffBlocks(string diffContent)
    {
        var blocks = new List<(string Search, string Replace)>();
        var lines = diffContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        var i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Trim() == SearchMarker)
            {
                var searchLines = new List<string>();
                i++;

                // Collect SEARCH content
                while (i < lines.Length && lines[i].Trim() != DividerMarker)
                {
                    searchLines.Add(lines[i]);
                    i++;
                }

                if (i >= lines.Length)
                    throw new FormatException("Invalid diff format: Missing divider marker");

                var replaceLines = new List<string>();
                i++;

                // Collect REPLACE content
                while (i < lines.Length && lines[i].Trim() != ReplaceMarker)
                {
                    replaceLines.Add(lines[i]);
                    i++;
                }

                if (i >= lines.Length)
                    throw new FormatException("Invalid diff format: Missing replace marker");

                blocks.Add((
                    string.Join(Environment.NewLine, searchLines),
                    string.Join(Environment.NewLine, replaceLines)
                ));
            }
            i++;
        }

        return blocks;
    }

    private static string ApplyDiffBlock(string content, (string Search, string Replace) block)
    {
        if (string.IsNullOrEmpty(block.Search))
            throw new ArgumentException("Search content cannot be empty");

        // Escape special regex characters but maintain newlines
        var escapedSearch = Regex.Escape(block.Search)
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");

        // Replace only the first occurrence
        var regex = new Regex(escapedSearch, RegexOptions.Multiline);
        Match match = regex.Match(content);

        if (!match.Success)
            throw new InvalidOperationException($"Search content not found in file: {block.Search}");

        return regex.Replace(content, block.Replace, 1);
    }
}