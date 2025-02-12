using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Thorfix.Tests;

[TestClass]
public class FileModifierTests
{
    private string _testDirectory = null!;
    private string _testFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ThorfixTests");
        _testFilePath = Path.Combine(_testDirectory, "test.txt");
        
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
        Directory.CreateDirectory(_testDirectory);
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
    public async Task ModifyFile_SingleBlock_Success()
    {
        // Arrange
        const string originalContent = "Line 1\nLine 2\nLine 3\n";
        await File.WriteAllTextAsync(_testFilePath, originalContent);

        const string diff = @"<<<<<<< SEARCH
Line 2
=======
Modified Line 2
>>>>>>> REPLACE";

        // Act
        await FileModifier.ModifyFile(_testFilePath, diff);

        // Assert
        var modifiedContent = await File.ReadAllTextAsync(_testFilePath);
        Assert.AreEqual("Line 1\nModified Line 2\nLine 3\n", modifiedContent);
    }

    [TestMethod]
    public async Task ModifyFile_MultipleBlocks_Success()
    {
        // Arrange
        const string originalContent = "Line 1\nLine 2\nLine 3\nLine 4\n";
        await File.WriteAllTextAsync(_testFilePath, originalContent);

        const string diff = @"<<<<<<< SEARCH
Line 2
=======
Modified Line 2
>>>>>>> REPLACE
<<<<<<< SEARCH
Line 4
=======
Modified Line 4
>>>>>>> REPLACE";

        // Act
        await FileModifier.ModifyFile(_testFilePath, diff);

        // Assert
        var modifiedContent = await File.ReadAllTextAsync(_testFilePath);
        Assert.AreEqual("Line 1\nModified Line 2\nLine 3\nModified Line 4\n", modifiedContent);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task ModifyFile_EmptyPath_ThrowsException()
    {
        // Act
        await FileModifier.ModifyFile("", "some diff");
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task ModifyFile_EmptyDiff_ThrowsException()
    {
        // Act
        await FileModifier.ModifyFile(_testFilePath, "");
    }

    [TestMethod]
    [ExpectedException(typeof(FileNotFoundException))]
    public async Task ModifyFile_NonexistentFile_ThrowsException()
    {
        // Act
        await FileModifier.ModifyFile(Path.Combine(_testDirectory, "nonexistent.txt"), "some diff");
    }

    [TestMethod]
    [ExpectedException(typeof(FormatException))]
    public async Task ModifyFile_InvalidDiffFormat_ThrowsException()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "content");

        // Act
        await FileModifier.ModifyFile(_testFilePath, "<<<<<<< SEARCH\nsome content");
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public async Task ModifyFile_SearchContentNotFound_ThrowsException()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "Line 1\nLine 2\n");

        const string diff = @"<<<<<<< SEARCH
Nonexistent Line
=======
Modified Line
>>>>>>> REPLACE";

        // Act
        await FileModifier.ModifyFile(_testFilePath, diff);
    }

    [TestMethod]
    public async Task ModifyFile_MultiLineSearch_Success()
    {
        // Arrange
        const string originalContent = "Line 1\nLine 2\nLine 3\nLine 4\n";
        await File.WriteAllTextAsync(_testFilePath, originalContent);

        const string diff = @"<<<<<<< SEARCH
Line 2\nLine 3
=======
Modified Lines
>>>>>>> REPLACE";

        // Act
        await FileModifier.ModifyFile(_testFilePath, diff);

        // Assert
        var modifiedContent = await File.ReadAllTextAsync(_testFilePath);
        Assert.AreEqual("Line 1\nModified Lines\nLine 4\n", modifiedContent);
    }

    [TestMethod]
    public async Task ModifyFile_PreservesWhitespace_Success()
    {
        // Arrange
        const string originalContent = "    Line 1\n\tLine 2\n  Line 3  \n";
        await File.WriteAllTextAsync(_testFilePath, originalContent);

        const string diff = @"<<<<<<< SEARCH
    Line 1
=======
    Modified Line 1
>>>>>>> REPLACE";

        // Act
        await FileModifier.ModifyFile(_testFilePath, diff);

        // Assert
        var modifiedContent = await File.ReadAllTextAsync(_testFilePath);
        Assert.AreEqual("    Modified Line 1\n\tLine 2\n  Line 3  \n", modifiedContent);
    }
}