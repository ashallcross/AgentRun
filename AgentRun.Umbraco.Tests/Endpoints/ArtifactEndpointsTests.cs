using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Endpoints;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;

namespace AgentRun.Umbraco.Tests.Endpoints;

[TestFixture]
public class ArtifactEndpointsTests
{
    private IInstanceManager _instanceManager = null!;
    private ArtifactEndpoints _endpoints = null!;
    private string _tempDir = null!;

    private static InstanceState CreateTestInstance(
        string instanceId = "inst-001",
        string workflowAlias = "test-wf") => new()
    {
        InstanceId = instanceId,
        WorkflowAlias = workflowAlias,
        Status = InstanceStatus.Running,
        CurrentStepIndex = 0,
        CreatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
        CreatedBy = "admin",
        Steps =
        [
            new StepState { Id = "step-one", Status = StepStatus.Complete }
        ]
    };

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"artifact-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _instanceManager = Substitute.For<IInstanceManager>();
        _instanceManager.GetInstanceFolderPath("test-wf", "inst-001").Returns(_tempDir);
        _endpoints = new ArtifactEndpoints(_instanceManager);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task GetArtifact_ValidFile_Returns200WithContent()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var artifactDir = Path.Combine(_tempDir, "artifacts");
        Directory.CreateDirectory(artifactDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "review-notes.md"), "# Review Notes\nAll good.");

        var result = await _endpoints.GetArtifact("inst-001", "artifacts/review-notes.md", CancellationToken.None);

        var contentResult = result as ContentResult;
        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult!.StatusCode, Is.Null.Or.EqualTo(200));
        Assert.That(contentResult.Content, Is.EqualTo("# Review Notes\nAll good."));
        Assert.That(contentResult.ContentType, Is.EqualTo("text/plain; charset=utf-8"));
    }

    [Test]
    public async Task GetArtifact_InstanceNotFound_Returns404()
    {
        _instanceManager.FindInstanceAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((InstanceState?)null);

        var result = await _endpoints.GetArtifact("unknown", "artifacts/file.md", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("instance_not_found"));
    }

    [Test]
    public async Task GetArtifact_PathTraversal_Returns400()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var result = await _endpoints.GetArtifact("inst-001", "../../../etc/passwd", CancellationToken.None);

        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var error = badRequestResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("invalid_path"));
    }

    [Test]
    public async Task GetArtifact_MissingFile_Returns404()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var result = await _endpoints.GetArtifact("inst-001", "artifacts/nonexistent.md", CancellationToken.None);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("artifact_not_found"));
    }

    [Test]
    public async Task GetArtifact_EmptyFile_Returns200WithEmptyString()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var artifactDir = Path.Combine(_tempDir, "artifacts");
        Directory.CreateDirectory(artifactDir);
        await File.WriteAllTextAsync(Path.Combine(artifactDir, "empty.md"), "");

        var result = await _endpoints.GetArtifact("inst-001", "artifacts/empty.md", CancellationToken.None);

        var contentResult = result as ContentResult;
        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult!.Content, Is.EqualTo(""));
    }

    [Test]
    public async Task GetArtifact_NestedPath_Returns200()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var nestedDir = Path.Combine(_tempDir, "artifacts", "sub");
        Directory.CreateDirectory(nestedDir);
        await File.WriteAllTextAsync(Path.Combine(nestedDir, "deep.md"), "nested content");

        var result = await _endpoints.GetArtifact("inst-001", "artifacts/sub/deep.md", CancellationToken.None);

        var contentResult = result as ContentResult;
        Assert.That(contentResult, Is.Not.Null);
        Assert.That(contentResult!.Content, Is.EqualTo("nested content"));
    }

    [Test]
    public async Task GetArtifact_EmptyFilePath_Returns400()
    {
        var instance = CreateTestInstance();
        _instanceManager.FindInstanceAsync("inst-001", Arg.Any<CancellationToken>())
            .Returns(instance);

        var result = await _endpoints.GetArtifact("inst-001", "", CancellationToken.None);

        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var error = badRequestResult.Value as ErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Error, Is.EqualTo("invalid_path"));
    }
}
