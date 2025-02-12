using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Thorfix.Tests;

[TestClass]
public class ToolResultTests
{
    [TestMethod]
    public void Constructor_Success_DefaultValues()
    {
        // Arrange & Act
        var result = new ToolResult("Test response");

        // Assert
        Assert.AreEqual("Test response", result.Response);
        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void Constructor_Error_ExplicitValues()
    {
        // Arrange & Act
        var result = new ToolResult("Error message", true);

        // Assert
        Assert.AreEqual("Error message", result.Response);
        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    public void Properties_AreImmutable()
    {
        // Arrange
        var result = new ToolResult("Test");

        // Act & Assert
        // The following should not compile if uncommented, verifying immutability:
        // result.Response = "New response";
        // result.IsError = true;
    }
}