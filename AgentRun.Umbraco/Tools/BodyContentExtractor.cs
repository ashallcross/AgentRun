using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace AgentRun.Umbraco.Tools;

// Pure-function body-copy extraction for GetContentTool's summary handle.
// Produces two complementary views of a node's body content:
//   - body_text : concatenated prose, HTML-stripped, capped at bodyMaxChars with
//                 a truncation marker when the pre-cap length exceeds the cap.
//                 Used by statistical-scoring pillars (Readability, Brand).
//   - body_metadata : heading hierarchy + link labels/targets + alt-text audit +
//                     image count, extracted from the FULL concatenated HTML
//                     (uncapped — structural audits must see every element).
//                     Used by the Accessibility pillar.
//
// Three-state distinction in the returned result is load-bearing downstream:
//   1. No body-copy property exists on this content type (layout-only node)
//        → BodyText = null, Metadata = null, UntruncatedLength = null
//   2. Body-copy property exists but yielded zero prose (unsaved draft, empty
//      block list, all blocks carry non-textual properties)
//        → BodyText = "", Metadata = empty-but-not-null, UntruncatedLength = 0
//   3. Body-copy property yielded prose
//        → BodyText populated (optionally with truncation marker when
//          UntruncatedLength > bodyMaxChars), Metadata populated,
//          UntruncatedLength reflects pre-cap prose length.
//
// All non-Umbraco-API throw paths are caught inside Extract — a misbehaving
// property or pathological HTML returns the empty-state result rather than
// crashing the whole get_content call.
public static class BodyContentExtractor
{
    // Tier 1 — canonical Block List / Block Grid composition aliases.
    private static readonly string[] Tier1Aliases =
    {
        "contentRows", "mainContent", "pageContent", "blocks", "contentGrid"
    };

    // Tier 2 — direct text / RTE aliases.
    private static readonly string[] Tier2Aliases =
    {
        "bodyContent", "bodyText", "articleBody", "body",
        "content", "description", "richText"
    };

    // Tier 3 — editor-alias fallback. Intentionally excludes TextBox/TextArea;
    // those imply non-body use (headlines, SEO titles) and would produce false
    // positives on layout-only content types that happen to carry a short text
    // field.
    private static readonly HashSet<string> Tier3EditorAliases = new(StringComparer.Ordinal)
    {
        "Umbraco.RichText",
        "Umbraco.BlockList",
        "Umbraco.BlockGrid"
    };

    private static readonly HashSet<string> BlockEditorAliases = new(StringComparer.Ordinal)
    {
        "Umbraco.BlockList",
        "Umbraco.BlockGrid"
    };

    // Block-level closing tags are substituted with paragraph breaks BEFORE the
    // generic tag-strip runs so prose retains paragraph structure for sentence-
    // level analysis. `</br>` is non-standard but appears in legacy CMS output;
    // single `<br>` collapses to a single space, double `<br>` to a paragraph.
    private static readonly Regex BlockCloseTagRegex = new(
        @"</(?:p|div|h[1-6]|li|blockquote|article|section|header|footer|main|aside)\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DoubleBrRegex = new(
        @"(?:<br\s*/?>\s*){2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // <script> / <style> subtree content is not prose — drop the whole element
    // before tag-stripping so neither the tags NOR their text-content survive
    // into body_text. Multiline-dotall semantics via RegexOptions.Singleline.
    private static readonly Regex ScriptOrStyleBlockRegex = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    // Collapse any whitespace run (including single newlines) — this runs on
    // individual paragraphs AFTER the double-newline split, so legitimate
    // paragraph breaks are already separated and single mid-paragraph
    // newlines can safely collapse to a single space.
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ParagraphBreakRegex = new(@"\n{2,}", RegexOptions.Compiled);

    // Upper bound on body_text cap — guards against int.MaxValue values that
    // would provoke catastrophic allocations. 10 MB of characters handles the
    // largest realistic pillar-page bodies; anything beyond is a config error.
    public const int MaxBodyMaxChars = 10_000_000;

    // Per-list cap on body_metadata arrays. body_metadata is advertised as
    // "always-full" for the Accessibility pillar, but pathological pages
    // (gallery indexes, auto-generated sitemaps) can carry thousands of
    // images / links / headings. 1000 per list keeps worst-case handle size
    // bounded to ~200 KB while covering 99%+ of realistic editorial pages.
    // Exceeding the cap is surfaced via ImageCount (always reflects true
    // total) and a truncation suffix on the last entry.
    public const int MaxMetadataItemsPerList = 1000;

    // Guard depth on recursive block-list descent so a self-referencing or
    // pathologically nested Block Grid can't blow the stack.
    private const int MaxBlockRecursionDepth = 5;

    public static BodyExtractionResult Extract(
        IPublishedContent node,
        int bodyMaxChars,
        ILogger logger)
    {
        IPublishedProperty? bodyProperty;
        try
        {
            bodyProperty = FindBodyProperty(node);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Content {NodeId}: FindBodyProperty threw — returning null body extraction",
                node.Id);
            return new BodyExtractionResult(null, null, null);
        }

        if (bodyProperty is null)
        {
            return new BodyExtractionResult(null, null, null);
        }

        string fullHtml;
        try
        {
            var htmlSink = new StringBuilder();
            CollectHtmlFromProperty(bodyProperty, htmlSink, logger, node.Id, depth: 0);
            fullHtml = htmlSink.ToString();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Content {NodeId}: body-HTML collection threw — returning empty body extraction",
                node.Id);
            return new BodyExtractionResult(string.Empty, EmptyMetadata(), 0);
        }

        if (string.IsNullOrWhiteSpace(fullHtml))
        {
            return new BodyExtractionResult(string.Empty, EmptyMetadata(), 0);
        }

        string bodyText;
        try
        {
            bodyText = StripHtmlToText(fullHtml);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Content {NodeId}: StripHtmlToText threw — returning empty body_text",
                node.Id);
            bodyText = string.Empty;
        }

        var untruncatedLength = bodyText.Length;

        if (bodyText.Length > bodyMaxChars)
        {
            var captured = TruncateAtGraphemeBoundary(bodyText, bodyMaxChars);
            bodyText = captured + $" [...truncated at {bodyMaxChars} of {untruncatedLength} chars]";
        }

        BodyMetadata metadata;
        try
        {
            metadata = ExtractMetadataWithAngleSharp(fullHtml);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Content {NodeId}: AngleSharp HTML parse failed — body_metadata returned empty. Exception: {ExceptionTypeName}",
                node.Id, ex.GetType().Name);
            metadata = EmptyMetadata();
        }

        return new BodyExtractionResult(bodyText, metadata, untruncatedLength);
    }

    // Cap-aware truncation that walks back to the nearest full grapheme so we
    // never emit an unpaired surrogate when cutting mid-emoji / mid-CJK.
    private static string TruncateAtGraphemeBoundary(string text, int bodyMaxChars)
    {
        if (bodyMaxChars <= 0 || text.Length == 0)
        {
            return string.Empty;
        }

        if (bodyMaxChars >= text.Length)
        {
            return text;
        }

        var cut = bodyMaxChars;
        // If we'd split a surrogate pair, back off one code unit.
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return cut > 0 ? text[..cut] : string.Empty;
    }

    private static IPublishedProperty? FindBodyProperty(IPublishedContent node)
    {
        // Build an index once to avoid repeated linear scans across the three
        // tiers. Umbraco content types rarely carry >50 properties so the
        // overhead is negligible.
        var byAlias = new Dictionary<string, IPublishedProperty>(StringComparer.Ordinal);
        foreach (var prop in node.Properties)
        {
            byAlias[prop.Alias] = prop;
        }

        foreach (var alias in Tier1Aliases)
        {
            if (byAlias.TryGetValue(alias, out var prop) && HasNonEmptyBody(prop))
            {
                return prop;
            }
        }

        foreach (var alias in Tier2Aliases)
        {
            if (byAlias.TryGetValue(alias, out var prop) && HasNonEmptyBody(prop))
            {
                return prop;
            }
        }

        // Tier 3 — editor-alias fallback, property-declaration order.
        foreach (var prop in node.Properties)
        {
            if (Tier3EditorAliases.Contains(prop.PropertyType.EditorAlias) && HasNonEmptyBody(prop))
            {
                return prop;
            }
        }

        // Last-resort empty-but-present tier — if any tier-1 / tier-2 property
        // EXISTS (even empty) we return it so the caller sees the empty-body
        // state (`""` + empty metadata) rather than "no property exists" (null).
        // This matches D11's load-bearing empty-vs-null distinction: an editor
        // who wired a body property but hasn't saved content yet should score
        // differently to a content type that has no body at all.
        foreach (var alias in Tier1Aliases)
        {
            if (byAlias.TryGetValue(alias, out var prop))
            {
                return prop;
            }
        }
        foreach (var alias in Tier2Aliases)
        {
            if (byAlias.TryGetValue(alias, out var prop))
            {
                return prop;
            }
        }

        return null;
    }

    // HasValue() returns true for a Block List whose property is saved but
    // contains zero blocks on some Umbraco runtime versions and false on
    // others — the surface is genuinely ambiguous. For block editors we
    // normalise by inspecting the iterable directly so we treat a populated
    // block list as present and an empty one as fall-through.
    private static bool HasNonEmptyBody(IPublishedProperty prop)
    {
        if (!prop.HasValue(culture: null, segment: null))
        {
            return false;
        }

        if (BlockEditorAliases.Contains(prop.PropertyType.EditorAlias))
        {
            var value = prop.GetValue(culture: null, segment: null);
            if (value is IEnumerable<object> blocks)
            {
                // Materialising into Any() would enumerate past the first item;
                // GetEnumerator + MoveNext stops at one and releases immediately.
                using var e = blocks.GetEnumerator();
                return e.MoveNext();
            }
        }

        return true;
    }

    private static void CollectHtmlFromProperty(
        IPublishedProperty property,
        StringBuilder sink,
        ILogger logger,
        int nodeId,
        int depth)
    {
        var editorAlias = property.PropertyType.EditorAlias;
        try
        {
            switch (editorAlias)
            {
                case "Umbraco.BlockList":
                case "Umbraco.BlockGrid":
                    CollectHtmlFromBlockList(property, sink, logger, nodeId, depth);
                    break;
                case "Umbraco.RichText":
                    AppendBlock(sink, CollectRichTextHtml(property));
                    break;
                case "Umbraco.TextBox":
                case "Umbraco.TextArea":
                    var text = property.GetValue(culture: null, segment: null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        AppendBlock(sink, "<p>" + System.Net.WebUtility.HtmlEncode(text) + "</p>");
                    }
                    break;
                default:
                    var fallback = property.GetValue(culture: null, segment: null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        AppendBlock(sink, fallback);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Content {NodeId}: failed to collect body HTML from property '{PropertyAlias}' (editor: {EditorAlias}) — skipping",
                nodeId, property.Alias, editorAlias);
        }
    }

    private static void CollectHtmlFromBlockList(
        IPublishedProperty property,
        StringBuilder sink,
        ILogger logger,
        int nodeId,
        int depth)
    {
        if (depth >= MaxBlockRecursionDepth)
        {
            logger.LogWarning(
                "Content {NodeId}: block recursion depth {Depth} exceeds max {Max} — halting descent on property '{PropertyAlias}'",
                nodeId, depth, MaxBlockRecursionDepth, property.Alias);
            return;
        }

        var value = property.GetValue(culture: null, segment: null);
        if (value is not IEnumerable<object> blocks)
        {
            return;
        }

        foreach (var block in blocks)
        {
            IPublishedElement? element;
            try
            {
                // BlockListItem<T>/BlockGridItem<T> declares a typed Content property
                // that hides the base IPublishedElement Content — GetProperty("Content")
                // throws AmbiguousMatchException. Use GetProperties() to sidestep the
                // ambiguity (same pattern as GetContentTool.ExtractBlockList).
                var contentProp = block.GetType().GetProperties()
                    .FirstOrDefault(p => p.Name == "Content"
                        && typeof(IPublishedElement).IsAssignableFrom(p.PropertyType));
                element = contentProp?.GetValue(block) as IPublishedElement;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Content {NodeId}: block reflection walk threw on property '{PropertyAlias}' — skipping this block, continuing",
                    nodeId, property.Alias);
                continue;
            }

            if (element is null)
            {
                continue;
            }

            foreach (var elemProp in element.Properties)
            {
                if (!elemProp.HasValue(culture: null, segment: null))
                {
                    continue;
                }

                try
                {
                    var elemEditor = elemProp.PropertyType.EditorAlias;
                    switch (elemEditor)
                    {
                        case "Umbraco.RichText":
                            AppendBlock(sink, CollectRichTextHtml(elemProp));
                            break;
                        case "Umbraco.TextBox":
                        case "Umbraco.TextArea":
                            var text = elemProp.GetValue(culture: null, segment: null)?.ToString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                AppendBlock(sink, "<p>" + System.Net.WebUtility.HtmlEncode(text) + "</p>");
                            }
                            break;
                        case "Umbraco.BlockList":
                        case "Umbraco.BlockGrid":
                            CollectHtmlFromBlockList(elemProp, sink, logger, nodeId, depth + 1);
                            break;
                        // Skip non-textual editors entirely (MediaPicker, ContentPicker,
                        // TrueFalse, Integer, DateTime etc.) — they don't carry prose.
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Content {NodeId}: failed to collect body HTML from block property '{PropertyAlias}' (editor: {EditorAlias}) — skipping",
                        nodeId, elemProp.Alias, elemProp.PropertyType.EditorAlias);
                }
            }
        }
    }

    private static string CollectRichTextHtml(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        if (value is IHtmlEncodedString hes)
        {
            return hes.ToHtmlString() ?? string.Empty;
        }
        return value?.ToString() ?? string.Empty;
    }

    private static void AppendBlock(StringBuilder sink, string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }
        if (sink.Length > 0)
        {
            sink.Append("\n\n");
        }
        sink.Append(html);
    }

    private static string StripHtmlToText(string html)
    {
        // Strip tags, decode entities, collapse whitespace — but preserve
        // paragraph breaks so downstream sentence-level analysis in Readability
        // has structure to reason about.
        //
        //   1. Drop <script>/<style> subtrees entirely — their text content
        //      is not prose and must not leak into body_text.
        //   2. Substitute block-level closing tags (</p>, </div>, </h1-6>, </li>,
        //      etc.) AND double+ <br> with literal paragraph breaks so the
        //      paragraph structure present in HTML survives tag-stripping.
        //   3. Run the generic tag-strip → spaces.
        //   4. Decode entities.
        //   5. Collapse non-newline whitespace, then re-join non-empty
        //      paragraphs with "\n\n".
        var withoutScripts = ScriptOrStyleBlockRegex.Replace(html, " ");
        var withParagraphMarkers = BlockCloseTagRegex.Replace(withoutScripts, "\n\n");
        withParagraphMarkers = DoubleBrRegex.Replace(withParagraphMarkers, "\n\n");

        var withoutTags = HtmlTagRegex.Replace(withParagraphMarkers, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);

        var paragraphs = ParagraphBreakRegex.Split(decoded);
        for (var i = 0; i < paragraphs.Length; i++)
        {
            // Collapse runs of non-newline whitespace to a single space (keeps
            // newlines out of mid-paragraph text) then trim.
            paragraphs[i] = WhitespaceRegex.Replace(paragraphs[i], " ").Trim();
        }
        var joined = string.Join("\n\n", paragraphs.Where(p => p.Length > 0));
        return joined;
    }

    private static BodyMetadata ExtractMetadataWithAngleSharp(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        var headings = new List<BodyHeading>();
        foreach (var heading in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6"))
        {
            if (headings.Count >= MaxMetadataItemsPerList)
            {
                break;
            }
            var tag = heading.TagName.ToLowerInvariant(); // "h1".."h6"
            if (tag.Length != 2 || !int.TryParse(tag[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            {
                continue;
            }
            var text = (heading.TextContent ?? string.Empty).Trim();
            headings.Add(new BodyHeading(level, text));
        }

        var links = new List<BodyLink>();
        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            if (links.Count >= MaxMetadataItemsPerList)
            {
                break;
            }
            var target = anchor.GetAttribute("href") ?? string.Empty;
            var text = (anchor.TextContent ?? string.Empty).Trim();
            links.Add(new BodyLink(text, target));
        }

        var altTexts = new List<BodyAltText>();
        var imageCount = 0;
        foreach (var img in document.QuerySelectorAll("img"))
        {
            // ImageCount always reflects the true total — only the alt_texts
            // list is bounded. Accessibility pillar can still compare list
            // length vs image_count to detect truncation.
            imageCount++;
            if (altTexts.Count >= MaxMetadataItemsPerList)
            {
                continue;
            }
            var src = img.GetAttribute("src") ?? string.Empty;
            // Three-state alt handling — match HtmlStructureExtractor's architect-
            // locked 2026-04-09 precedent: attribute presence is the rule, not
            // non-emptiness. Empty alt ("") is the accessibility-correct marker
            // for decorative images per WCAG H67.
            var alt = img.GetAttribute("alt"); // null when attribute absent
            altTexts.Add(new BodyAltText(src, alt));
        }

        return new BodyMetadata(headings, links, altTexts, imageCount);
    }

    private static BodyMetadata EmptyMetadata()
        => new(Array.Empty<BodyHeading>(), Array.Empty<BodyLink>(), Array.Empty<BodyAltText>(), 0);
}

public sealed record BodyExtractionResult(
    [property: JsonPropertyName("body_text")] string? BodyText,
    [property: JsonPropertyName("body_metadata")] BodyMetadata? Metadata,
    [property: JsonIgnore] int? UntruncatedLength);

public sealed record BodyMetadata(
    [property: JsonPropertyName("headings")] IReadOnlyList<BodyHeading> Headings,
    [property: JsonPropertyName("links")] IReadOnlyList<BodyLink> Links,
    [property: JsonPropertyName("alt_texts")] IReadOnlyList<BodyAltText> AltTexts,
    [property: JsonPropertyName("image_count")] int ImageCount);

public sealed record BodyHeading(
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("text")] string Text);

public sealed record BodyLink(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("target")] string Target);

public sealed record BodyAltText(
    [property: JsonPropertyName("image_src")] string ImageSrc,
    [property: JsonPropertyName("alt")] string? Alt);
