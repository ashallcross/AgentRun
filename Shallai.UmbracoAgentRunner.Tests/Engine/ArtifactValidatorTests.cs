using Microsoft.Extensions.Logging.Abstractions;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class ArtifactValidatorTests
{
    private string _tempDir = null!;
    private ArtifactValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shallai-artifact-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _validator = new ArtifactValidator(NullLogger<ArtifactValidator>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public async Task AllReadsFromPresent_ReturnsPassed()
    {
        // AC #1: all reads_from files exist → validation passes
        File.WriteAllText(Path.Combine(_tempDir, "scan-results.md"), "content");

        var step = new StepDefinition
        {
            Id = "analyse", Name = "Analyse", Agent = "agents/test.md",
            ReadsFrom = ["scan-results.md"]
        };

        var result = await _validator.ValidateInputArtifactsAsync(step, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }

    [Test]
    public async Task MissingReadsFrom_ReturnsFailedWithMissingList()
    {
        // AC #2: missing reads_from → Passed = false
        var step = new StepDefinition
        {
            Id = "analyse", Name = "Analyse", Agent = "agents/test.md",
            ReadsFrom = ["scan-results.md", "config.json"]
        };

        var result = await _validator.ValidateInputArtifactsAsync(step, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.False);
        Assert.That(result.MissingFiles, Has.Count.EqualTo(2));
        Assert.That(result.MissingFiles, Does.Contain("scan-results.md"));
        Assert.That(result.MissingFiles, Does.Contain("config.json"));
    }

    [Test]
    public async Task NullReadsFrom_ReturnsPassed()
    {
        // AC #9: null ReadsFrom → passes immediately
        var step = new StepDefinition
        {
            Id = "analyse", Name = "Analyse", Agent = "agents/test.md",
            ReadsFrom = null
        };

        var result = await _validator.ValidateInputArtifactsAsync(step, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }

    [Test]
    public async Task EmptyReadsFrom_ReturnsPassed()
    {
        // AC #9: empty ReadsFrom list → passes immediately
        var step = new StepDefinition
        {
            Id = "analyse", Name = "Analyse", Agent = "agents/test.md",
            ReadsFrom = []
        };

        var result = await _validator.ValidateInputArtifactsAsync(step, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }
}
