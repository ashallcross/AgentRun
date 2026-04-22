using AgentRun.Umbraco.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class BodyContentExtractorTests
{
    // ---------- AC2: D3 priority-order tests ----------

    [Test]
    public void Tier1ContentRowsWinsOverTier2BodyText()
    {
        // Both contentRows (Tier 1) and bodyText (Tier 2) present — Tier 1 must win.
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("richTextRow", ("content", "Umbraco.RichText", (object)"<p>From contentRows</p>")));
        var bodyTextProp = MakeRichTextProperty("bodyText", "<p>From bodyText direct</p>");
        var node = MakeNode(1, contentRows, bodyTextProp);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("From contentRows"));
        Assert.That(result.BodyText, Does.Not.Contain("From bodyText direct"));
    }

    [Test]
    public void Tier2BodyContentWinsOverTier2Body()
    {
        // Both Tier 2 properties — first in list (bodyContent) wins over later (body).
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>WINNER prose</p>");
        var bodyProp = MakeRichTextProperty("body", "<p>LOSER prose</p>");
        var node = MakeNode(1, bodyContent, bodyProp);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("WINNER prose"));
        Assert.That(result.BodyText, Does.Not.Contain("LOSER"));
    }

    [Test]
    public void Tier3EditorAliasFallbackPicksFirstRichText()
    {
        // No Tier 1 or Tier 2 alias matches, but a custom-aliased RichText property
        // exists — Tier 3 picks it up in property-declaration order.
        var textBox = MakeProperty("headline", "Umbraco.TextBox", "Not a body");
        var customRte = MakeRichTextProperty("articleProse", "<p>Tier 3 RTE content</p>");
        var node = MakeNode(1, textBox, customRte);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("Tier 3 RTE content"));
    }

    [Test]
    public void NoTierMatches_ReturnsNullBodyText()
    {
        // Layout-only node: TextBox + ContentPicker only, no body-copy property.
        var textBox = MakeProperty("headline", "Umbraco.TextBox", "Some headline");
        var picker = MakeProperty("relatedLink", "Umbraco.ContentPicker", null);
        picker.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(false);
        var node = MakeNode(1, textBox, picker);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.Null);
        Assert.That(result.Metadata, Is.Null);
        Assert.That(result.UntruncatedLength, Is.Null);
    }

    // ---------- AC2: extraction correctness ----------

    [Test]
    public void BlockListWithRichTextRow_ExtractsProse()
    {
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("richTextRow",
                ("content", "Umbraco.RichText", (object)"<p>First paragraph with <strong>bold</strong> prose.</p>")));
        var node = MakeNode(1, contentRows);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("First paragraph with bold prose."));
    }

    [Test]
    public void BlockListWithTextRow_ExtractsProse()
    {
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("textRow",
                ("body", "Umbraco.TextArea", (object)"Plain text body copy.")));
        var node = MakeNode(1, contentRows);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("Plain text body copy."));
    }

    [Test]
    public void BlockListWithMixedPropsAndNonProse_SkipsNonProseRows()
    {
        // Three blocks: prose + image (no prose) + more prose. Non-prose rows
        // silently skipped; prose concatenated in order.
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("richTextRow", ("content", "Umbraco.RichText", (object)"<p>Opening paragraph.</p>")),
            new TestBlock("imageRow", ("image", "Umbraco.MediaPicker3", (object)"media-ref")),
            new TestBlock("richTextRow", ("content", "Umbraco.RichText", (object)"<p>Closing paragraph.</p>")));
        var node = MakeNode(1, contentRows);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("Opening paragraph."));
        Assert.That(result.BodyText, Does.Contain("Closing paragraph."));
        Assert.That(result.BodyText, Does.Not.Contain("media-ref"));
    }

    [Test]
    public void DirectRichTextProperty_ExtractsProse()
    {
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>Direct article body.</p>");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("Direct article body."));
    }

    // ---------- AC3: truncation tests ----------

    [Test]
    public void ShortContent_NoTruncationMarker()
    {
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>Short</p>");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Not.Contain("[...truncated"));
        Assert.That(result.UntruncatedLength, Is.EqualTo("Short".Length));
    }

    [Test]
    public void LongContent_TruncationMarkerAppended()
    {
        var longProse = new string('a', 20000);
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>" + longProse + "</p>");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain(" [...truncated at 15000 of 20000 chars]"));
        Assert.That(result.UntruncatedLength, Is.EqualTo(20000));
    }

    [Test]
    public void ExactAtCap_NoTruncationMarker()
    {
        var exactProse = new string('a', 1000);
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>" + exactProse + "</p>");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 1000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Not.Contain("[...truncated"));
        Assert.That(result.BodyText!.Length, Is.EqualTo(1000));
    }

    [Test]
    public void VerySmallCap_MarkerStillAppended()
    {
        var bodyContent = MakeRichTextProperty("bodyContent", "<p>First content here</p>");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 5, NullLogger.Instance);

        Assert.That(result.BodyText, Does.StartWith("First"));
        Assert.That(result.BodyText, Does.Contain("[...truncated at 5 of"));
    }

    // ---------- AC4: metadata extraction ----------

    [Test]
    public void Headings_AllLevelsExtractedInDocumentOrder()
    {
        var html = "<h2>Introduction</h2><h3>Details</h3><h1>Top Level</h1><h6>Deep</h6>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata, Is.Not.Null);
        Assert.That(result.Metadata!.Headings, Has.Count.EqualTo(4));
        Assert.That(result.Metadata.Headings[0].Level, Is.EqualTo(2));
        Assert.That(result.Metadata.Headings[0].Text, Is.EqualTo("Introduction"));
        Assert.That(result.Metadata.Headings[1].Level, Is.EqualTo(3));
        Assert.That(result.Metadata.Headings[2].Level, Is.EqualTo(1));
        Assert.That(result.Metadata.Headings[3].Level, Is.EqualTo(6));
    }

    [Test]
    public void Headings_AcrossMultipleBlockRowsPreservesOrder()
    {
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("richTextRow", ("content", "Umbraco.RichText",
                (object)"<h2>First</h2><p>body</p>")),
            new TestBlock("richTextRow", ("content", "Umbraco.RichText",
                (object)"<h2>Second</h2>")));
        var node = MakeNode(1, contentRows);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata!.Headings.Select(h => h.Text).ToList(),
            Is.EqualTo(new[] { "First", "Second" }));
    }

    [Test]
    public void Links_ExtractsTextAndTarget()
    {
        var html = "<p><a href=\"/about\">About us</a> and <a href=\"https://example.com\">External</a></p>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata!.Links, Has.Count.EqualTo(2));
        Assert.That(result.Metadata.Links[0].Text, Is.EqualTo("About us"));
        Assert.That(result.Metadata.Links[0].Target, Is.EqualTo("/about"));
        Assert.That(result.Metadata.Links[1].Text, Is.EqualTo("External"));
        Assert.That(result.Metadata.Links[1].Target, Is.EqualTo("https://example.com"));
    }

    [TestCase("<img src=\"/hero.jpg\" alt=\"Hero banner\">", "Hero banner")]
    [TestCase("<img src=\"/deco.jpg\" alt=\"\">", "")]
    public void AltTexts_PresentAltPreserved(string html, string expectedAlt)
    {
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata!.AltTexts, Has.Count.EqualTo(1));
        Assert.That(result.Metadata.AltTexts[0].Alt, Is.EqualTo(expectedAlt));
    }

    [Test]
    public void AltTexts_MissingAltAttributeYieldsNull()
    {
        var html = "<img src=\"/missing-alt.jpg\">";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata!.AltTexts, Has.Count.EqualTo(1));
        Assert.That(result.Metadata.AltTexts[0].Alt, Is.Null);
    }

    [Test]
    public void ImageCount_IncludesAllImages()
    {
        var html = "<p><img src=\"/a.jpg\" alt=\"A\"><img src=\"/b.jpg\" alt=\"\"><img src=\"/c.jpg\"></p>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata!.ImageCount, Is.EqualTo(3));
        Assert.That(result.Metadata.AltTexts, Has.Count.EqualTo(3));
    }

    [Test]
    public void Metadata_ExtractedFromFullHtmlEvenWhenBodyTextCapped()
    {
        // Build HTML longer than the cap, with a heading AFTER the cap
        // boundary. Metadata must still capture the heading (structural
        // data is uncapped per D2).
        var filler = new string('a', 2000);
        var html = "<p>" + filler + "</p><h2>Heading After Cap</h2>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 500, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("[...truncated"));
        Assert.That(result.Metadata!.Headings, Has.Count.EqualTo(1));
        Assert.That(result.Metadata.Headings[0].Text, Is.EqualTo("Heading After Cap"));
    }

    // ---------- AC5 + AC6: null vs empty distinction ----------

    [Test]
    public void ContentTypeWithNoBodyProperty_BodyTextAndMetadataNull()
    {
        var headline = MakeProperty("headline", "Umbraco.TextBox", "Just a headline");
        var node = MakeNode(1, headline);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.Null);
        Assert.That(result.Metadata, Is.Null);
        Assert.That(result.UntruncatedLength, Is.Null);
    }

    [Test]
    public void BodyPropertyPresentButEmpty_BodyTextEmptyStringAndMetadataEmpty()
    {
        var bodyContent = MakeRichTextProperty("bodyContent", "");
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo(string.Empty));
        Assert.That(result.Metadata, Is.Not.Null);
        Assert.That(result.Metadata!.Headings, Is.Empty);
        Assert.That(result.Metadata.Links, Is.Empty);
        Assert.That(result.Metadata.AltTexts, Is.Empty);
        Assert.That(result.Metadata.ImageCount, Is.EqualTo(0));
        Assert.That(result.UntruncatedLength, Is.EqualTo(0));
    }

    [Test]
    public void BlockListWithAllEmptyBlocks_BodyTextEmptyString()
    {
        // Block list present but all blocks have empty prose properties.
        var contentRows = MakeBlockListProperty("contentRows",
            new TestBlock("richTextRow", ("content", "Umbraco.RichText", (object)"")),
            new TestBlock("richTextRow", ("content", "Umbraco.RichText", (object)"")));
        var node = MakeNode(1, contentRows);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo(string.Empty));
        Assert.That(result.Metadata, Is.Not.Null);
    }

    // ---------- AC7: AngleSharp failure resilience ----------

    [Test]
    public void HtmlEntitiesDecodedInBodyText()
    {
        var html = "<p>Five &amp; ten &lt; twenty</p>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("Five & ten < twenty"));
    }

    [Test]
    public void ParagraphsPreservedAsDoubleNewline()
    {
        var html = "<p>First paragraph.</p><p>Second paragraph.</p>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("First paragraph."));
        Assert.That(result.BodyText, Does.Contain("Second paragraph."));
    }

    [Test]
    public void WhitespaceCollapsedInBodyText()
    {
        // Horizontal runs of whitespace collapse to a single space; a single newline
        // collapses likewise. (A DOUBLE newline in source HTML is treated as a
        // paragraph break per the extractor's design — covered separately.)
        var html = "<p>Lots    of      whitespace\n\tand\ttabs</p>";
        var bodyContent = MakeRichTextProperty("bodyContent", html);
        var node = MakeNode(1, bodyContent);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("Lots of whitespace and tabs"));
    }

    // ---------- Post-2026-04-22 code review patches ----------

    [Test]
    public void ParagraphBreaksPreserved_ForTypicalBlockLevelHtml()
    {
        // Regression for Blind Hunter #1: </p><p> in source HTML was previously
        // collapsed into a single space-separated run. Block-level closing tags
        // must become paragraph breaks so Readability's sentence-level analysis
        // sees paragraph structure.
        var html = "<p>First paragraph.</p><p>Second paragraph.</p><p>Third.</p>";
        var node = MakeNode(1, MakeRichTextProperty("bodyContent", html));

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("First paragraph.\n\nSecond paragraph.\n\nThird."));
    }

    [Test]
    public void ParagraphBreaksPreserved_ForDoubleBr()
    {
        // <br><br> is the legacy editor idiom for paragraph breaks in RTE
        // payloads that predate <p> wrapping. Double+ <br> should collapse
        // to a single paragraph break; single <br> stays a space.
        var html = "Line one<br><br>Line two<br>still line two";
        var node = MakeNode(1, MakeRichTextProperty("bodyContent", html));

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("Line one\n\nLine two still line two"));
    }

    [Test]
    public void ScriptAndStyleBlockContentStrippedFromBodyText()
    {
        // Regression for Blind Hunter #10 / Edge Case Hunter #7: tag-strip regex
        // previously left the TEXT content of <script> and <style> in body_text
        // (only the tag delimiters were removed). Script/style subtrees must be
        // dropped wholesale before tag-stripping.
        var html = "<p>Real prose.</p><script>alert('hello');</script><style>.x { color: red; }</style><p>More prose.</p>";
        var node = MakeNode(1, MakeRichTextProperty("bodyContent", html));

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Not.Contain("alert"));
        Assert.That(result.BodyText, Does.Not.Contain("color: red"));
        Assert.That(result.BodyText, Does.Contain("Real prose."));
        Assert.That(result.BodyText, Does.Contain("More prose."));
    }

    [Test]
    public void SurrogatePair_NotSplitByTruncationBoundary()
    {
        // Regression for Edge Case Hunter #2: slicing body_text by char index
        // could produce an unpaired high surrogate when a supplementary-plane
        // character (emoji, CJK ideograph past BMP) straddled bodyMaxChars.
        // Truncation must back off to the previous grapheme boundary so the
        // resulting string round-trips through UTF-8 without replacement chars.
        var emoji = "😀😀😀😀😀";
        var html = $"<p>{emoji}</p>";
        var node = MakeNode(1, MakeRichTextProperty("bodyContent", html));

        var result = BodyContentExtractor.Extract(node, 5, NullLogger.Instance);

        Assert.That(result.BodyText, Is.Not.Null);
        var utf8 = System.Text.Encoding.UTF8.GetBytes(result.BodyText!);
        var roundTripped = System.Text.Encoding.UTF8.GetString(utf8);
        Assert.That(roundTripped, Is.EqualTo(result.BodyText),
            "body_text must round-trip through UTF-8 without replacement characters (no unpaired surrogates)");
    }

    [Test]
    public void EmptyBlockList_FallsThroughToTier2RichText()
    {
        // Regression for Edge Case Hunter #4 / Decision 3.1 fix: an empty-but-
        // present Tier 1 Block List must NOT win over a populated Tier 2 RTE.
        // HasValue returns true for an empty block list on some Umbraco runtime
        // versions, so HasNonEmptyBody must inspect block count directly for
        // BlockList / BlockGrid editor aliases.
        var emptyBlockList = MakeBlockListProperty("contentRows"); // zero blocks
        var populatedRte = MakeRichTextProperty("bodyContent", "<p>Real prose</p>");
        var node = MakeNode(1, emptyBlockList, populatedRte);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo("Real prose"));
    }

    [Test]
    public void EmptyBlockList_OnlyProperty_ReturnsEmptyNotNullState()
    {
        // Related to EmptyBlockList test above — when the empty Block List is
        // the ONLY body-copy property on the node, we fall through to the
        // last-resort tier that returns the property anyway. Result state:
        // BodyText = "" + empty metadata (D11 empty semantics), NOT null
        // (which would imply "no body property exists at all").
        var emptyBlockList = MakeBlockListProperty("contentRows"); // zero blocks
        var node = MakeNode(1, emptyBlockList);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Is.EqualTo(string.Empty));
        Assert.That(result.Metadata, Is.Not.Null);
        Assert.That(result.Metadata!.Headings, Is.Empty);
        Assert.That(result.Metadata.ImageCount, Is.EqualTo(0));
    }

    [Test]
    public void NestedBlockList_RecursivelyExtracted()
    {
        // Regression for Edge Case Hunter #5: a Block Grid row containing a
        // nested Block List (common "column of cards" pattern) was previously
        // dropped because the inner switch had no case for nested blocks.
        // Recursion depth is bounded per MaxBlockRecursionDepth.
        var innerBlock = MakeBlockListProperty(
            "nestedBlocks",
            new TestBlock("richTextRow",
                ("content", "Umbraco.RichText", (object)"<p>Nested prose</p>")));

        var outerBlock = MakeBlockListPropertyWithNested("contentRows", innerBlock);
        var node = MakeNode(1, outerBlock);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("Nested prose"));
    }

    [Test]
    public void MetadataListsCappedAtOneThousand_PreservingTrueImageCount()
    {
        // Regression for Edge Case Hunter #1: body_metadata was uncapped and a
        // pathological image-heavy page could push the handle beyond sane size.
        // Lists are now capped per MaxMetadataItemsPerList (1000); ImageCount
        // still reflects the true total so accessibility audits can detect the
        // truncation.
        var imgs = string.Join(
            "",
            Enumerable.Range(1, 1200).Select(i => $"<img src=\"/m/{i}.jpg\" alt=\"{i}\">"));
        var html = $"<div>{imgs}</div>";
        var node = MakeNode(1, MakeRichTextProperty("bodyContent", html));

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.Metadata, Is.Not.Null);
        Assert.That(result.Metadata!.AltTexts.Count, Is.EqualTo(1000));
        Assert.That(result.Metadata.ImageCount, Is.EqualTo(1200),
            "ImageCount must reflect the true image total even when alt_texts is truncated");
    }

    [Test]
    public void PerBlockReflectionFailure_DoesNotKillRemainingBlocks()
    {
        // Regression for Edge Case Hunter #13: a single misbehaving block
        // previously poisoned the whole block-list extraction via the outer
        // catch. Each block's reflection walk is now wrapped so one bad block
        // is skipped and siblings are still extracted.
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns("Umbraco.BlockList");
        prop.Alias.Returns("contentRows");
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);

        var validBlock = new TestBlockItem(MakeElement(new TestBlock("richTextRow",
            ("content", "Umbraco.RichText", (object)"<p>Sibling still extracted</p>"))));

        // "Broken" = plain object whose Content property doesn't resolve to
        // IPublishedElement. The FirstOrDefault filter returns null → element
        // is null → the continue path fires and the valid sibling still runs.
        var brokenBlock = new object();

        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<object> { brokenBlock, validBlock });

        var node = MakeNode(1, prop);

        var result = BodyContentExtractor.Extract(node, 15000, NullLogger.Instance);

        Assert.That(result.BodyText, Does.Contain("Sibling still extracted"));
    }

    [Test]
    public void MaxBodyMaxChars_IsGenerousButNotIntMaxValue()
    {
        // Regression for Edge Case Hunter #10 — the upper bound must be a real
        // int, not int.MaxValue, so GetContentTool's clamp rejects pathological
        // configs. 10 MB of characters is the committed contract.
        Assert.That(BodyContentExtractor.MaxBodyMaxChars, Is.EqualTo(10_000_000));
        Assert.That(BodyContentExtractor.MaxBodyMaxChars, Is.LessThan(int.MaxValue / 2));
    }

    // Helper for the nested-block-list test — wraps a nested block-list
    // property inside an outer block-list's element.
    private static IPublishedProperty MakeBlockListPropertyWithNested(
        string alias,
        IPublishedProperty nestedBlockListProperty)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns("Umbraco.BlockList");
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);

        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("columnRow");
        element.ContentType.Returns(contentType);
        element.Properties.Returns(new[] { nestedBlockListProperty });

        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new List<object> { new TestBlockItem(element) });
        return prop;
    }

    // ---------- Test helpers ----------

    private static IPublishedContent MakeNode(int id, params IPublishedProperty[] properties)
    {
        var node = Substitute.For<IPublishedContent>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns("testType");
        node.Id.Returns(id);
        node.ContentType.Returns(contentType);
        node.Properties.Returns(properties);
        return node;
    }

    private static IPublishedProperty MakeProperty(string alias, string editorAlias, object? value)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns(editorAlias);
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value is not null);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        return prop;
    }

    private static IPublishedProperty MakeRichTextProperty(string alias, string html)
    {
        // RichText property GetValue commonly returns an IHtmlEncodedString; for
        // test purposes the plain string is sufficient because BodyContentExtractor
        // falls back to .ToString(). HasValue is true when the string is non-null
        // (matching Umbraco's behaviour where empty-string RTE values still "have"
        // a value — the property exists on the content type).
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns("Umbraco.RichText");
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(html);
        return prop;
    }

    private sealed record TestBlock(string ContentTypeAlias, params (string Alias, string EditorAlias, object? Value)[] Properties);

    private static IPublishedProperty MakeBlockListProperty(string alias, params TestBlock[] blocks)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns("Umbraco.BlockList");
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(true);

        var blockItems = blocks.Select(b => (object)new TestBlockItem(MakeElement(b))).ToList();
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(blockItems);
        return prop;
    }

    // BlockListItem<T> exposes a `Content` property of type IPublishedElement —
    // the reflection walk in BodyContentExtractor's CollectHtmlFromBlockList
    // looks for any property named "Content" whose type is assignable to
    // IPublishedElement. This test-local class satisfies that contract.
    private sealed class TestBlockItem
    {
        public IPublishedElement Content { get; }
        public TestBlockItem(IPublishedElement content) { Content = content; }
    }

    private static IPublishedElement MakeElement(TestBlock block)
    {
        var element = Substitute.For<IPublishedElement>();
        var contentType = Substitute.For<IPublishedContentType>();
        contentType.Alias.Returns(block.ContentTypeAlias);
        element.ContentType.Returns(contentType);

        var props = block.Properties.Select(p => MakeElementProperty(p.Alias, p.EditorAlias, p.Value)).ToArray();
        element.Properties.Returns(props);
        return element;
    }

    private static IPublishedProperty MakeElementProperty(string alias, string editorAlias, object? value)
    {
        var prop = Substitute.For<IPublishedProperty>();
        var propType = Substitute.For<IPublishedPropertyType>();
        propType.EditorAlias.Returns(editorAlias);
        prop.Alias.Returns(alias);
        prop.PropertyType.Returns(propType);
        // Empty string is still "has value" (unsaved-draft semantics); null is "no value".
        prop.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value is not null);
        prop.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(value);
        return prop;
    }
}
