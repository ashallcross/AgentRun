using Microsoft.Extensions.Logging.Abstractions;
using Shallai.UmbracoAgentRunner.Engine;
using Shallai.UmbracoAgentRunner.Workflows;

namespace Shallai.UmbracoAgentRunner.Tests.Engine;

[TestFixture]
public class CompletionCheckerTests
{
    private string _tempDir = null!;
    private CompletionChecker _checker = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shallai-completion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _checker = new CompletionChecker(NullLogger<CompletionChecker>.Instance);
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
    public async Task AllFilesPresent_ReturnsPassed()
    {
        // AC #5, #6: all listed files exist → Passed = true
        File.WriteAllText(Path.Combine(_tempDir, "report.md"), "content");
        File.WriteAllText(Path.Combine(_tempDir, "scores.json"), "content");

        var check = new CompletionCheckDefinition { FilesExist = ["report.md", "scores.json"] };

        var result = await _checker.CheckAsync(check, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }

    [Test]
    public async Task MissingFiles_ReturnsFailedWithMissingList()
    {
        // AC #7: missing files → Passed = false with correct list
        File.WriteAllText(Path.Combine(_tempDir, "report.md"), "content");
        // scores.json intentionally missing

        var check = new CompletionCheckDefinition { FilesExist = ["report.md", "scores.json"] };

        var result = await _checker.CheckAsync(check, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.False);
        Assert.That(result.MissingFiles, Has.Count.EqualTo(1));
        Assert.That(result.MissingFiles[0], Is.EqualTo("scores.json"));
    }

    [Test]
    public async Task NullCheck_ReturnsPassed()
    {
        // AC #8: null completion check → passes
        var result = await _checker.CheckAsync(null, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }

    [Test]
    public async Task EmptyFilesExist_ReturnsPassed()
    {
        // AC #8: empty FilesExist list → passes
        var check = new CompletionCheckDefinition { FilesExist = [] };

        var result = await _checker.CheckAsync(check, _tempDir, CancellationToken.None);

        Assert.That(result.Passed, Is.True);
        Assert.That(result.MissingFiles, Is.Empty);
    }
}
