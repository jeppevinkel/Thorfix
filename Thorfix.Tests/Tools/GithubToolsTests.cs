using LibGit2Sharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Octokit;
using Thorfix.Tools;
using Branch = LibGit2Sharp.Branch;
using Commit = LibGit2Sharp.Commit;
using Index = LibGit2Sharp.Index;
using Repository = LibGit2Sharp.Repository;
using Signature = LibGit2Sharp.Signature;

namespace Thorfix.Tests.Tools;

[TestClass]
public class GithubToolsTests
{
    private Mock<GitHubClient> _mockGithubClient = null!;
    private Mock<IIssuesClient> _mockIssuesClient = null!;
    private Mock<IIssueCommentsClient> _mockCommentsClient = null!;
    private Mock<IPullRequestsClient> _mockPullRequestsClient = null!;
    private Mock<Repository> _mockRepository = null!;
    private Issue _testIssue = null!;
    private GithubTools _githubTools = null!;

    [TestInitialize]
    public void Setup()
    {
        // Setup GitHub client mocks
        _mockGithubClient = new Mock<GitHubClient>(new ProductHeaderValue("test"));
        _mockIssuesClient = new Mock<IIssuesClient>();
        _mockCommentsClient = new Mock<IIssueCommentsClient>();
        _mockPullRequestsClient = new Mock<IPullRequestsClient>();

        _mockGithubClient.Setup(x => x.Issue).Returns(_mockIssuesClient.Object);
        _mockIssuesClient.Setup(x => x.Comment).Returns(_mockCommentsClient.Object);
        _mockGithubClient.Setup(x => x.PullRequest).Returns(_mockPullRequestsClient.Object);

        // Setup test issue
        _testIssue = new Issue(
            id: 1,
            nodeId: "test-node",
            url: "https://api.github.com/repos/test/test/issues/1",
            htmlUrl: "https://github.com/test/test/issues/1",
            commentsUrl: "https://api.github.com/repos/test/test/issues/1/comments",
            eventsUrl: "https://api.github.com/repos/test/test/issues/1/events",
            number: 1,
            state: ItemState.Open,
            title: "Test Issue",
            body: "Test body",
            closedBy: null,
            user: new User(),
            labels: new List<Label>(),
            assignee: null,
            assignees: new List<User>(),
            milestone: null,
            comments: 0,
            pullRequest: null,
            closedAt: null,
            createdAt: DateTimeOffset.Now,
            updatedAt: DateTimeOffset.Now,
            repository: null,
            locked: false,
            activeLockReason: null);

        // Setup repository mock
        _mockRepository = new Mock<Repository>();

        // Create test issue
        _testIssue = new Issue(
            // id: 1,
            // nodeId: "test-node",
            // url: "https://api.github.com/repos/test/test/issues/1",
            // htmlUrl: "https://github.com/test/test/issues/1",
            // eventsUrl: "https://github.com/test/test/issues/1",
            // number: 1,
            // state: ItemState.Open,
            // title: "Test Issue",
            // body: "Test body",
            // closedBy: null,
            // user: new User(),
            // labels: new List<Label>(),
            // assignee: null,
            // assignees: new List<User>(),
            // milestone: null,
            // comments: 0,
            // pullRequest: null,
            // closedAt: null,
            // createdAt: DateTimeOffset.Now,
            // updatedAt: DateTimeOffset.Now,
            // repository: null
            );

        // Create test credentials
        var credentials = new UsernamePasswordCredentials
        {
            Username = "test",
            Password = "test"
        };

        // Create GithubTools instance
        _githubTools = new GithubTools(
            _mockGithubClient.Object,
            _testIssue,
            _mockRepository.Object,
            null,
            credentials,
            "test-branch",
            "test-owner",
            "test-repo"
        );
    }

    [TestMethod]
    public async Task IssueAddComment_Success()
    {
        // Arrange
        const string comment = "Test comment";
        _mockCommentsClient
            .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new IssueComment());

        // Act
        var result = await _githubTools.IssueAddComment(comment);

        // Assert
        Assert.IsFalse(result.IsError);
        Assert.AreEqual("Comment added successfully", result.Response);
        _mockCommentsClient.Verify(
            x => x.Create("test-owner", "test-repo", 1, $"[FROM THOR]\n\n{comment}"),
            Times.Once);
    }

    [TestMethod]
    public async Task IssueAddComment_Failure()
    {
        // Arrange
        const string comment = "Test comment";
        _mockCommentsClient
            .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _githubTools.IssueAddComment(comment);

        // Assert
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Response.Contains("API Error"));
    }

    [TestMethod]
    public async Task ConvertIssueToPullRequest_Success()
    {
        // Arrange
        _mockPullRequestsClient
            .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewPullRequest>()))
            .ReturnsAsync(new PullRequest());

        // Act
        var result = await _githubTools.ConvertIssueToPullRequest();

        // Assert
        Assert.IsFalse(result.IsError);
        Assert.AreEqual("Converted the issue into a pull request", result.Response);
        _mockPullRequestsClient.Verify(
            x => x.Create("test-owner", "test-repo", 
                It.Is<NewPullRequest>(pr => 
                    pr.Base == "master" && 
                    pr.Head == "test-branch" && 
                    pr.Title == "1")),
            Times.Once);
    }

    [TestMethod]
    public async Task ConvertIssueToPullRequest_Failure()
    {
        // Arrange
        _mockPullRequestsClient
            .Setup(x => x.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NewPullRequest>()))
            .ThrowsAsync(new Exception("API Error"));

        // Act
        var result = await _githubTools.ConvertIssueToPullRequest();

        // Assert
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Response.Contains("API Error"));
    }

    [TestMethod]
    public async Task CommitChanges_Success()
    {
        // Arrange
        const string commitMessage = "Test commit";
        _mockRepository.Setup(x => x.Commit(
            It.IsAny<string>(),
            It.IsAny<Signature>(),
            It.IsAny<Signature>()
        )).Returns(new Mock<Commit>().Object);

        // Act
        var result = await _githubTools.CommitChanges(commitMessage);

        // Assert
        Assert.IsFalse(result.IsError);
        Assert.AreEqual("Commited changes successfully", result.Response);
        _mockRepository.Verify(x => x.Commit(
            $"{commitMessage}\n#1",
            It.Is<Signature>(s => s.Name == "Thorfix" && s.Email == "thorfix@jeppdev.com"),
            It.Is<Signature>(s => s.Name == "Thorfix" && s.Email == "thorfix@jeppdev.com")),
            Times.Once);
    }

    [TestMethod]
    public async Task CommitChanges_Failure()
    {
        // Arrange
        const string commitMessage = "Test commit";
        _mockRepository.Setup(x => x.Commit(
            It.IsAny<string>(),
            It.IsAny<Signature>(),
            It.IsAny<Signature>()
        )).Throws(new Exception("Git error"));

        // Act
        var result = await _githubTools.CommitChanges(commitMessage);

        // Assert
        Assert.IsTrue(result.IsError);
        Assert.IsTrue(result.Response.Contains("Git error"));
    }

    [TestMethod]
    public void StageChanges_Success()
    {
        // Arrange
        _mockRepository.Setup(x => x.Index).Returns(new Mock<Index>().Object);

        // Act & Assert (no exception thrown)
        GithubTools.StageChanges(_mockRepository.Object);
    }

    [TestMethod]
    public void PushChanges_Success()
    {
        // Arrange
        var mockNetwork = new Mock<Network>();
        _mockRepository.Setup(x => x.Network).Returns(mockNetwork.Object);
        var credentials = new UsernamePasswordCredentials
        {
            Username = "test",
            Password = "test"
        };

        // Act & Assert (no exception thrown)
        GithubTools.PushChanges(_mockRepository.Object, credentials);
    }
}