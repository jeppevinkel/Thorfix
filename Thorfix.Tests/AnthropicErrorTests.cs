using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Thorfix.Tests;

[TestClass]
public class AnthropicErrorTests
{
    [TestMethod]
    public void ErrorCodes_HaveCorrectValues()
    {
        // Assert
        Assert.AreEqual(400, (int)AnthropicErrorCode.InvalidRequestError);
        Assert.AreEqual(401, (int)AnthropicErrorCode.AuthenticationError);
        Assert.AreEqual(403, (int)AnthropicErrorCode.PermissionError);
        Assert.AreEqual(404, (int)AnthropicErrorCode.NotFoundError);
        Assert.AreEqual(413, (int)AnthropicErrorCode.RequestTooLarge);
        Assert.AreEqual(429, (int)AnthropicErrorCode.RateLimitError);
        Assert.AreEqual(500, (int)AnthropicErrorCode.ApiError);
        Assert.AreEqual(529, (int)AnthropicErrorCode.OverloadedError);
    }

    [TestMethod]
    public void ErrorCodes_AreUnique()
    {
        // Arrange
        var errorCodes = Enum.GetValues<AnthropicErrorCode>();
        var uniqueValues = new HashSet<int>();

        // Act & Assert
        foreach (var code in errorCodes)
        {
            Assert.IsTrue(uniqueValues.Add((int)code), $"Duplicate value found for {code}");
        }
    }
}