using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AgentRun.Umbraco.Tools;

// Story 10.7a Track A: extracted from FetchUrlTool. Owns AngleSharp-backed
// parsing of HTML response bodies into the structured-fact shape that the
// CQA scanner + Accessibility Quick Scan workflows consume. FetchUrlTool
// stays the transport + validation owner and becomes a coordinator over
// this extractor + FetchCacheWriter.
//
// The "what to surface" policy lives here; the "what it means" judgement
// belongs to the calling workflow's scanner prompt (FR23 / NFR25 — runner
// stays workflow-generic). The 200-entry heading cap and 100-entry anchor
// cap are defensive ceilings against pathological inputs, not workflow rules.
public class HtmlStructureExtractor : IHtmlStructureExtractor
{
    private const int HeadingSequenceCap = 200;
    private const int AnchorTextsCap = 100;

    private static readonly char[] WhitespaceChars = { ' ', '\t', '\n', '\r', '\f', '\v' };

    public StructuredHtmlContent Extract(byte[] body, Uri sourceUri, int unmarkedLength, bool truncated)
    {
        // AngleSharp's HTML5 parser is the trust boundary for hostile input.
        var parser = new HtmlParser();
        using var stream = new MemoryStream(body, 0, unmarkedLength, writable: false);
        using var document = parser.ParseDocument(stream);

        var rawTitle = document.Title;
        var title = string.IsNullOrWhiteSpace(rawTitle) ? null : rawTitle.Trim();

        // P2 (Story 9.1b code-review fix pass): case-insensitive attribute match —
        // `<meta name="Description">` (capital D) is common CMS output and CSS
        // attribute values are case-sensitive in AngleSharp's selector engine
        // unless the `i` flag is set.
        var metaDescription = document
            .QuerySelector("meta[name=description i]")
            ?.GetAttribute("content");

        // Story 9.4 Task 1.B: single heading walk produces h1, h2, h3_h6_count,
        // AND the ordered tag sequence (capped at 200 entries via early break —
        // a pathological 50k-heading page must not allocate the full sequence
        // list). The h1/h2/h3_h6_count tallies are uncapped to preserve the
        // pre-9.4 CQA contract; only `sequence` is bounded.
        var h1 = new List<string>();
        var h2 = new List<string>();
        var h3h6Count = 0;
        var headingSequence = new List<string>();
        foreach (var heading in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6"))
        {
            var tag = heading.TagName.ToLowerInvariant(); // "h1".."h6"
            switch (tag)
            {
                case "h1": h1.Add(heading.TextContent.Trim()); break;
                case "h2": h2.Add(heading.TextContent.Trim()); break;
                default:   h3h6Count++; break;
            }
            if (headingSequence.Count < HeadingSequenceCap)
                headingSequence.Add(tag);
        }

        // P3 (Story 9.1b code-review fix pass): strip <script> and <style>
        // descendants of <body> before computing word count, otherwise inline
        // JSON-LD / minified analytics inflates word_count by thousands and
        // breaks the deterministic-facts promise (the same source HTML can
        // produce wildly different word counts depending on inline-script weight).
        var bodyText = string.Empty;
        if (document.Body is { } docBody)
        {
            foreach (var node in docBody.QuerySelectorAll("script, style").ToList())
                node.Remove();
            bodyText = docBody.TextContent ?? string.Empty;
        }
        var wordCount = bodyText.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries).Length;

        var imgs = document.QuerySelectorAll("img");
        var imgTotal = imgs.Length;
        // Architect-locked 2026-04-09: attribute existence is the rule, not non-emptiness.
        // <img alt=""> is the accessibility-correct decorative-image marker.
        var imgWithAlt = imgs.Count(i => i.GetAttribute("alt") != null);
        var imgMissingAlt = imgTotal - imgWithAlt;

        // P4 (Story 9.1b code-review fix pass): respect <base href> when
        // resolving relative anchor URLs. Host comparison still uses the
        // source URL's host (D3 — strict host equality is intentional and
        // www-vs-apex stays parser-side strict; opinionated SEO judgements
        // are the analyser's job, not the parser's).
        var resolveBase = sourceUri;
        var baseHref = document.QuerySelector("base[href]")?.GetAttribute("href");
        if (!string.IsNullOrEmpty(baseHref) && Uri.TryCreate(sourceUri, baseHref, out var parsedBase))
            resolveBase = parsedBase;

        var sourceHost = sourceUri.Host;
        var anchors = document.QuerySelectorAll("a[href]");
        var internalLinks = 0;
        var externalLinks = 0;
        // Story 9.4 Task 1.B: collect anchor text samples within the existing
        // anchor walk. Cap at 100 entries via early break on the COLLECTION only
        // — internal/external tallies stay uncapped so the 9.1b invariant
        // `internal + external == document.QuerySelectorAll("a[href]").Length`
        // continues to hold. Original case is preserved (the scanner prompt
        // does case-insensitive matching itself); empty-text anchors are skipped
        // from the sample list but still counted in internal/external.
        var anchorTexts = new List<string>();
        var anchorTextsTruncated = false;
        foreach (var a in anchors)
        {
            var href = a.GetAttribute("href") ?? "";
            if (Uri.TryCreate(resolveBase, href, out var resolved)
                && (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
            {
                if (string.Equals(resolved.Host, sourceHost, StringComparison.OrdinalIgnoreCase))
                    internalLinks++;
                else
                    externalLinks++;
            }
            else
            {
                // Non-http(s) (mailto:, tel:, javascript:, fragment-only) or malformed.
                // Classified as external so the invariant
                // internal + external == document.QuerySelectorAll("a[href]").Length holds.
                externalLinks++;
            }

            if (!anchorTextsTruncated)
            {
                var text = CollapseWhitespace(a.TextContent ?? string.Empty);
                if (text.Length > 0)
                {
                    if (anchorTexts.Count < AnchorTextsCap)
                        anchorTexts.Add(text);
                    if (anchorTexts.Count >= AnchorTextsCap)
                        anchorTextsTruncated = true;
                }
            }
        }

        var forms = BuildForms(document);
        var semanticElements = BuildSemanticElements(document);
        var langRaw = document.DocumentElement?.GetAttribute("lang");
        var lang = string.IsNullOrWhiteSpace(langRaw) ? null : langRaw;

        return new StructuredHtmlContent(
            title,
            metaDescription,
            new StructuredHeadings(h1, h2, h3h6Count, headingSequence),
            wordCount,
            new StructuredImages(imgTotal, imgWithAlt, imgMissingAlt),
            new StructuredLinks(internalLinks, externalLinks, anchorTexts, anchorTextsTruncated),
            forms,
            semanticElements,
            lang);
    }

    private static StructuredForms BuildForms(IDocument document)
    {
        int fieldCount = 0;
        int fieldsWithLabel = 0;
        foreach (var field in document.QuerySelectorAll("input, textarea, select"))
        {
            if (field.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
            {
                var type = (field.GetAttribute("type") ?? "text").ToLowerInvariant();
                if (type is "hidden" or "submit" or "button" or "reset" or "image")
                    continue;
            }
            fieldCount++;
            if (IsFieldLabelled(field, document))
                fieldsWithLabel++;
        }
        return new StructuredForms(
            FieldCount: fieldCount,
            FieldsWithLabel: fieldsWithLabel,
            FieldsMissingLabel: fieldCount - fieldsWithLabel);
    }

    private static StructuredSemanticElements BuildSemanticElements(IDocument document)
        => new(
            Main:   HasSemanticElement(document, "main",   "main"),
            Nav:    HasSemanticElement(document, "nav",    "navigation"),
            Header: HasSemanticElement(document, "header", "banner"),
            Footer: HasSemanticElement(document, "footer", "contentinfo"));

    private static bool HasSemanticElement(IDocument document, string element, string role)
    {
        if (document.QuerySelector(element) != null)
            return true;
        // ARIA `role` is a space-separated token list per spec — use ~= for case-insensitive token match.
        return document.QuerySelector($"[role~=\"{role}\" i]") != null;
    }

    private static string CollapseWhitespace(string raw)
    {
        // Trim + whitespace-collapse, **preserving original case** (the scanner
        // prompt does case-insensitive matching itself; preserving case lets
        // future workflows do case-sensitive analysis if they need it).
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var parts = raw.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        return string.Join(' ', parts);
    }

    private static bool IsFieldLabelled(IElement field, IDocument document)
    {
        // (a) field has an id and a <label for="<id>"> exists
        var id = field.GetAttribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            foreach (var label in document.QuerySelectorAll("label[for]"))
            {
                if (string.Equals(label.GetAttribute("for"), id, StringComparison.Ordinal))
                    return true;
            }
        }

        // (b) field is a descendant of a <label> element
        var ancestor = field.ParentElement;
        while (ancestor != null)
        {
            if (ancestor.TagName.Equals("LABEL", StringComparison.OrdinalIgnoreCase))
                return true;
            ancestor = ancestor.ParentElement;
        }

        // (c) non-empty aria-label
        var ariaLabel = field.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return true;

        // (d) aria-labelledby: dereference each IDREF, concatenate TextContent,
        //     treat as labelled iff concatenated trimmed result is non-empty.
        //     Q3 (architect-locked 2026-04-10): unresolved IDREFs contribute
        //     empty text — they do not throw and do not disqualify the others.
        var labelledBy = field.GetAttribute("aria-labelledby");
        if (!string.IsNullOrEmpty(labelledBy))
        {
            var idrefs = labelledBy.Split(WhitespaceChars, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var idref in idrefs)
            {
                var target = document.GetElementById(idref);
                if (target != null)
                    sb.Append(target.TextContent);
            }
            if (!string.IsNullOrWhiteSpace(sb.ToString()))
                return true;
        }

        return false;
    }
}

public sealed record StructuredHtmlContent(
    string? Title,
    string? MetaDescription,
    StructuredHeadings Headings,
    int WordCount,
    StructuredImages Images,
    StructuredLinks Links,
    StructuredForms Forms,
    StructuredSemanticElements SemanticElements,
    string? Lang);

public sealed record StructuredHeadings(
    [property: JsonPropertyName("h1")] IReadOnlyList<string> H1,
    [property: JsonPropertyName("h2")] IReadOnlyList<string> H2,
    [property: JsonPropertyName("h3_h6_count")] int H3H6Count,
    [property: JsonPropertyName("sequence")] IReadOnlyList<string> Sequence);

public sealed record StructuredImages(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("with_alt")] int WithAlt,
    [property: JsonPropertyName("missing_alt")] int MissingAlt);

public sealed record StructuredLinks(
    [property: JsonPropertyName("internal")] int Internal,
    [property: JsonPropertyName("external")] int External,
    [property: JsonPropertyName("anchor_texts")] IReadOnlyList<string> AnchorTexts,
    [property: JsonPropertyName("anchor_texts_truncated")] bool AnchorTextsTruncated);

public sealed record StructuredForms(
    [property: JsonPropertyName("field_count")] int FieldCount,
    [property: JsonPropertyName("fields_with_label")] int FieldsWithLabel,
    [property: JsonPropertyName("fields_missing_label")] int FieldsMissingLabel);

public sealed record StructuredSemanticElements(
    [property: JsonPropertyName("main")] bool Main,
    [property: JsonPropertyName("nav")] bool Nav,
    [property: JsonPropertyName("header")] bool Header,
    [property: JsonPropertyName("footer")] bool Footer);
