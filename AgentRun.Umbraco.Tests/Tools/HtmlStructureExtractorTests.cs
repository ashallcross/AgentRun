using System.Text;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class HtmlStructureExtractorTests
{
    private HtmlStructureExtractor _extractor = null!;
    private Uri _sourceUri = null!;

    [SetUp]
    public void SetUp()
    {
        _extractor = new HtmlStructureExtractor();
        _sourceUri = new Uri("https://example.com/page");
    }

    [Test]
    public void Extract_EmptyBody_ReturnsZeroValuedStructure()
    {
        var body = Array.Empty<byte>();

        var result = _extractor.Extract(body, _sourceUri, unmarkedLength: 0, truncated: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.Null);
            Assert.That(result.MetaDescription, Is.Null);
            Assert.That(result.Headings.H1, Is.Empty);
            Assert.That(result.Headings.H2, Is.Empty);
            Assert.That(result.Headings.H3H6Count, Is.EqualTo(0));
            Assert.That(result.Headings.Sequence, Is.Empty);
            Assert.That(result.WordCount, Is.EqualTo(0));
            Assert.That(result.Images.Total, Is.EqualTo(0));
            Assert.That(result.Links.Internal, Is.EqualTo(0));
            Assert.That(result.Links.External, Is.EqualTo(0));
            Assert.That(result.Forms.FieldCount, Is.EqualTo(0));
            Assert.That(result.SemanticElements.Main, Is.False);
            Assert.That(result.Lang, Is.Null);
        });
    }

    [Test]
    public void Extract_ValidHtml_PopulatesTitleMetaHeadingsAndLang()
    {
        var html = """
            <!DOCTYPE html>
            <html lang="en-GB">
              <head>
                <title>Example Page</title>
                <meta name="Description" content="A page about things.">
              </head>
              <body>
                <h1>Welcome</h1>
                <h2>Section A</h2>
                <h3>Sub</h3>
                <p>One two three four.</p>
              </body>
            </html>
            """;
        var body = Encoding.UTF8.GetBytes(html);

        var result = _extractor.Extract(body, _sourceUri, body.Length, truncated: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Example Page"));
            Assert.That(result.MetaDescription, Is.EqualTo("A page about things."));
            Assert.That(result.Headings.H1, Is.EqualTo(new[] { "Welcome" }));
            Assert.That(result.Headings.H2, Is.EqualTo(new[] { "Section A" }));
            Assert.That(result.Headings.H3H6Count, Is.EqualTo(1));
            Assert.That(result.Headings.Sequence, Is.EqualTo(new[] { "h1", "h2", "h3" }));
            // WordCount includes heading text AND paragraph text (body.TextContent
            // collects all descendant text once script/style are stripped). This
            // page: "Welcome" (1) + "Section A" (2) + "Sub" (1) + "One two three four." (4) = 8.
            Assert.That(result.WordCount, Is.EqualTo(8));
            Assert.That(result.Lang, Is.EqualTo("en-GB"));
        });
    }

    [Test]
    public void Extract_AnchorsClassifiedAsInternalOrExternal_ByHost()
    {
        var html = """
            <html>
              <body>
                <a href="/about">Internal relative</a>
                <a href="https://example.com/contact">Internal absolute</a>
                <a href="https://other.example/page">External</a>
                <a href="mailto:hello@example.com">Mail</a>
              </body>
            </html>
            """;
        var body = Encoding.UTF8.GetBytes(html);

        var result = _extractor.Extract(body, _sourceUri, body.Length, truncated: false);

        // The 9.1b invariant: internal + external == total anchors with href.
        Assert.Multiple(() =>
        {
            Assert.That(result.Links.Internal, Is.EqualTo(2));
            // Non-http(s) mailto: + cross-host https counted as external.
            Assert.That(result.Links.External, Is.EqualTo(2));
            Assert.That(result.Links.AnchorTexts, Does.Contain("Internal relative"));
            Assert.That(result.Links.AnchorTexts, Does.Contain("External"));
            Assert.That(result.Links.AnchorTextsTruncated, Is.False);
        });
    }

    [Test]
    public void Extract_MalformedHtml_ReturnsMinimalStructureWithoutThrowing()
    {
        // Story 10.7a F1 — AngleSharp's HTML5 parser is tolerant of malformed
        // input by design, but we characterise the contract at this layer so a
        // future parser swap can't silently drop exception-swallowing behaviour.
        // Inputs: nested/unterminated tags, stray entities, malformed declarations.
        var html = "<html><head><title>Broken<body><h1>Hi<div><<<>>><p>text</p";
        var body = Encoding.UTF8.GetBytes(html);

        // Act — must not throw; must return a non-null structure.
        var result = _extractor.Extract(body, _sourceUri, body.Length, truncated: false);

        Assert.That(result, Is.Not.Null);
        // Parser does a best-effort reconstruction; the exact field values are
        // AngleSharp-version-dependent, so we assert only the shape invariants.
        Assert.Multiple(() =>
        {
            Assert.That(result.Headings, Is.Not.Null);
            Assert.That(result.Links, Is.Not.Null);
            Assert.That(result.Images, Is.Not.Null);
            Assert.That(result.Forms, Is.Not.Null);
            Assert.That(result.SemanticElements, Is.Not.Null);
            Assert.That(result.WordCount, Is.GreaterThanOrEqualTo(0));
        });
    }
}
