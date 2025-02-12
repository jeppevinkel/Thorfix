using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thorfix.Tools;

namespace Thorfix.Tests.Tools;

[TestClass]
public class FileSystemToolsTests
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "ThorfixTests");
    private FileSystemTools _fileSystemTools = null!;

    [TestInitialize]
    public void Setup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
        Directory.CreateDirectory(_testDirectory);
        _fileSystemTools = new FileSystemTools();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [TestMethod]
    public async Task ReadFile_ValidPath_ReturnsContent()
    {
        // Arrange
        var testFilePath = Path.Combine(_testDirectory, "test.txt");
        var expectedContent = "Test content";
        await File.WriteAllTextAsync(testFilePath, expectedContent);

        // Act
        var result = await _fileSystemTools.ReadFile(testFilePath);

        // Assert
        Assert.IsFalse(result.IsError);
        Assert.AreEqual(expectedContent, result.Result);
    }

    [TestMethod]
    public async Task ReadFile_InvalidPath_ReturnsError()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = await _fileSystemTools.ReadFile(nonExistentPath);

        // Assert
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Result.Contains("Failed to read file"));
    }

    [TestMethod]
    public async Task WriteFile_ValidPath_CreatesFile()
    {
        // Arrange
        var testFilePath = Path.Combine(_testDirectory, "writeTest.txt");
        var content = "Test write content";

        // Act
        var result = await _fileSystemTools.WriteFile(testFilePath, content);

        // Assert
        Assert.IsFalse(result.IsError);
        Assert.IsTrue(File.Exists(testFilePath));
        var writtenContent = await File.ReadAllTextAsync(testFilePath);
        Assert.AreEqual(content, writtenContent);
    }

    [TestMethod]
    public async Task WriteFile_InvalidPath_ReturnsError()
    {
        // Arrange
        var invalidPath = Path.Combine(_testDirectory, "invalid/path/file.txt");
        var content = "Test content";

        // Act
        var result = await _fileSystemTools.WriteFile(invalidPath, content);

        // Assert
        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public async Task ListFiles_ValidDirectory_ReturnsFileList()
    {
        // Arrange
        var testFiles = new[]
        {
            Path.Combine(_testDirectory, "file1.txt"),
            Path.Combine(_testDirectory, "file2.txt"),
            Path.Combine(_testDirectory, "subdir", "file3.txt")
        };

        Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir"));
        foreach (var file in testFiles)
        {
            await File.WriteAllTextAsync(file, "test content");
        }

        // Act
        var result = await _fileSystemTools.ListFiles();

        // Assert
        Assert.IsFalse(result.IsError);
        foreach (var file in testFiles)
        {
            Assert.IsTrue(result.Result.Contains(Path.GetFileName(file)));
        }
    }

    [TestMethod]
    public async Task ModifyFile_ValidPatch_ModifiesFile()
    {
        // Arrange
        var testFilePath = Path.Combine(_testDirectory, "modify.txt");
        var originalContent = "Line 1\nLine 2\nLine 3\n";
        await File.WriteAllTextAsync(testFilePath, originalContent);

        var diff = @"<<<<<<< SEARCH
Line 2
=======
Modified Line 2
>>>>>>> REPLACE";

        // Act
        var result = await _fileSystemTools.ModifyFile(testFilePath, diff);

        // Assert
        Assert.IsFalse(result.IsError);
        var modifiedContent = await File.ReadAllTextAsync(testFilePath);
        Assert.IsTrue(modifiedContent.Contains("Modified Line 2"));
        Assert.IsFalse(modifiedContent.Contains("Line 2"));
    }

    [TestMethod]
    public void IsPathAllowed_PathOutsideRoot_ReturnsFalse()
    {
        // This test requires accessing a private method, which might need to be made internal or public for testing
        // or tested through public methods that use it
        var path = Path.Combine(Path.GetTempPath(), "outside.txt");
        var result = _fileSystemTools.ReadFile(path).Result;
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Result.Contains("outside the allowed root directory"));
    }
}