using System.Text;
using System.Text.RegularExpressions;

namespace Thorfix;

public class Patcher
{
    public class PatchHunk
    {
        public int OriginalStart { get; set; }
        public int OriginalLength { get; set; }
        public int NewStart { get; set; }
        public int NewLength { get; set; }
        public List<PatchLine> Lines { get; set; } = new();
    }

    public class PatchLine
    {
        public string Content { get; set; }
        public PatchOperation Operation { get; set; }

        public PatchLine(string content, PatchOperation operation)
        {
            Content = content;
            Operation = operation;
        }
    }

    public enum PatchOperation
    {
        Context,
        Add,
        Remove
    }

    private const int ContextLines = 3; // Number of context lines before and after changes

    public static string CreatePatch(string originalText, string modifiedText)
    {
        var originalLines = originalText.Split('\n');
        var modifiedLines = modifiedText.Split('\n');
        var hunks = GenerateHunks(originalLines, modifiedLines);
        
        var sb = new StringBuilder();
        foreach (var hunk in hunks)
        {
            // Write hunk header
            sb.AppendLine($"@@ -{hunk.OriginalStart + 1},{hunk.OriginalLength} +{hunk.NewStart + 1},{hunk.NewLength} @@");
            
            // Write hunk content
            foreach (var line in hunk.Lines)
            {
                char prefix = line.Operation switch
                {
                    PatchOperation.Context => ' ',
                    PatchOperation.Add => '+',
                    PatchOperation.Remove => '-',
                    _ => throw new ArgumentException("Invalid operation")
                };
                sb.AppendLine($"{prefix}{line.Content}");
            }
        }

        return sb.ToString();
    }

    private static List<PatchHunk> GenerateHunks(string[] originalLines, string[] modifiedLines)
    {
        var diff = new Diff<string>(originalLines, modifiedLines);
        var changes = diff.ComputeDifferences();
        var hunks = new List<PatchHunk>();
        
        int currentHunkStart = -1;
        PatchHunk? currentHunk = null;

        for (int i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            
            if (change.Type != ChangeType.Unchanged || 
                (currentHunk != null && i < currentHunkStart + currentHunk.Lines.Count + ContextLines))
            {
                if (currentHunk == null)
                {
                    // Start a new hunk
                    int start = Math.Max(0, i - ContextLines);
                    currentHunkStart = start;
                    currentHunk = new PatchHunk
                    {
                        OriginalStart = start,
                        NewStart = start
                    };
                    
                    // Add preceding context lines
                    for (int j = start; j < i; j++)
                    {
                        currentHunk.Lines.Add(new PatchLine(changes[j].Symbol, PatchOperation.Context));
                    }
                    hunks.Add(currentHunk);
                }

                // Add the current line
                currentHunk.Lines.Add(new PatchLine(
                    change.Symbol,
                    change.Type switch
                    {
                        ChangeType.Unchanged => PatchOperation.Context,
                        ChangeType.Deleted => PatchOperation.Remove,
                        ChangeType.Inserted => PatchOperation.Add,
                        _ => throw new ArgumentException("Invalid change type")
                    }));
            }
            else if (currentHunk != null)
            {
                // Calculate hunk lengths
                currentHunk.OriginalLength = currentHunk.Lines.Count(l => l.Operation != PatchOperation.Add);
                currentHunk.NewLength = currentHunk.Lines.Count(l => l.Operation != PatchOperation.Remove);
                currentHunk = null;
            }
        }

        // Handle the last hunk
        if (currentHunk != null)
        {
            currentHunk.OriginalLength = currentHunk.Lines.Count(l => l.Operation != PatchOperation.Add);
            currentHunk.NewLength = currentHunk.Lines.Count(l => l.Operation != PatchOperation.Remove);
        }

        return hunks;
    }

    public static async Task ApplyPatchAsync(string sourceFilePath, string patchFilePath, string outputFilePath)
    {
        var sourceLines = await File.ReadAllLinesAsync(sourceFilePath);
        var patchLines = await File.ReadAllLinesAsync(patchFilePath);
        var hunks = ParsePatch(patchLines);
        
        var result = ApplyHunks(sourceLines, hunks);
        await File.WriteAllLinesAsync(outputFilePath, result);
    }

    public static async Task ApplyPatchAsync(string sourceFilePath, string patches)
    {
        var sourceLines = await File.ReadAllLinesAsync(sourceFilePath);
        var patchLines = patches.Split('\n');
        var hunks = ParsePatch(patchLines);
        
        var result = ApplyHunks(sourceLines, hunks);
        await File.WriteAllLinesAsync(sourceFilePath, result);
    }

    private static List<PatchHunk> ParsePatch(string[] patchContent)
    {
        var hunks = new List<PatchHunk>();
        PatchHunk? currentHunk = null;

        var hunkHeaderRegex = new Regex(@"@@ -(\d+),(\d+) \+(\d+),(\d+) @@");

        foreach (var line in patchContent)
        {
            var match = hunkHeaderRegex.Match(line);
            if (match.Success)
            {
                currentHunk = new PatchHunk
                {
                    OriginalStart = int.Parse(match.Groups[1].Value) - 1,
                    OriginalLength = int.Parse(match.Groups[2].Value),
                    NewStart = int.Parse(match.Groups[3].Value) - 1,
                    NewLength = int.Parse(match.Groups[4].Value)
                };
                hunks.Add(currentHunk);
            }
            else if (currentHunk != null && line.Length > 0)
            {
                var operation = line[0] switch
                {
                    ' ' => PatchOperation.Context,
                    '+' => PatchOperation.Add,
                    '-' => PatchOperation.Remove,
                    _ => throw new ArgumentException($"Invalid patch line: {line}")
                };
                currentHunk.Lines.Add(new PatchLine(line[1..], operation));
            }
        }

        return hunks;
    }

    private static string[] ApplyHunks(string[] sourceLines, List<PatchHunk> hunks)
    {
        var result = new List<string>();
        int currentLine = 0;

        foreach (var hunk in hunks)
        {
            // Add unchanged lines before the hunk
            while (currentLine < hunk.OriginalStart)
            {
                result.Add(sourceLines[currentLine]);
                currentLine++;
            }

            // Apply the hunk
            foreach (var line in hunk.Lines)
            {
                switch (line.Operation)
                {
                    case PatchOperation.Context:
                        if (currentLine >= sourceLines.Length || sourceLines[currentLine] != line.Content)
                        {
                            throw new InvalidOperationException($"Context mismatch at line {currentLine + 1}");
                        }
                        
                        result.Add(line.Content);
                        currentLine++;
                        break;

                    case PatchOperation.Add:
                        result.Add(line.Content);
                        break;

                    case PatchOperation.Remove:
                        if (currentLine >= sourceLines.Length || sourceLines[currentLine] != line.Content)
                            throw new InvalidOperationException($"Remove mismatch at line {currentLine + 1}");
                        currentLine++;
                        break;
                }
            }
        }

        // Add any remaining unchanged lines
        while (currentLine < sourceLines.Length)
        {
            result.Add(sourceLines[currentLine]);
            currentLine++;
        }

        return result.ToArray();
    }
}

// Helper classes for diff computation
public enum ChangeType
{
    Unchanged,
    Deleted,
    Inserted
}

public class Change
{
    public ChangeType Type { get; set; }
    public string Symbol { get; set; }

    public Change(ChangeType type, string symbol)
    {
        Type = type;
        Symbol = symbol;
    }
}

public class Diff<T> where T : IEquatable<T>
{
    private readonly T[] _original;
    private readonly T[] _modified;

    public Diff(T[] original, T[] modified)
    {
        _original = original;
        _modified = modified;
    }

    public List<Change> ComputeDifferences()
    {
        var changes = new List<Change>();
        var matrix = ComputeLcsMatrix();
        var i = _original.Length;
        var j = _modified.Length;

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && _original[i - 1].Equals(_modified[j - 1]))
            {
                changes.Insert(0, new Change(ChangeType.Unchanged, _original[i - 1].ToString()));
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || matrix[i, j - 1] >= matrix[i - 1, j]))
            {
                changes.Insert(0, new Change(ChangeType.Inserted, _modified[j - 1].ToString()));
                j--;
            }
            else if (i > 0 && (j == 0 || matrix[i, j - 1] < matrix[i - 1, j]))
            {
                changes.Insert(0, new Change(ChangeType.Deleted, _original[i - 1].ToString()));
                i--;
            }
        }

        return changes;
    }

    private int[,] ComputeLcsMatrix()
    {
        var matrix = new int[_original.Length + 1, _modified.Length + 1];

        for (var i = 1; i <= _original.Length; i++)
        {
            for (var j = 1; j <= _modified.Length; j++)
            {
                if (_original[i - 1].Equals(_modified[j - 1]))
                {
                    matrix[i, j] = matrix[i - 1, j - 1] + 1;
                }
                else
                {
                    matrix[i, j] = Math.Max(matrix[i - 1, j], matrix[i, j - 1]);
                }
            }
        }

        return matrix;
    }
}