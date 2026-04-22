using System.Text.Json;
using AgentRun.Umbraco.Engine;
using Examine;
using Examine.Search;
using Lucene.Net.QueryParsers.Classic;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace AgentRun.Umbraco.Tools;

/// <summary>
/// Workflow tool that performs full-text keyword search against Umbraco's published
/// content via the Examine External index.
///
/// Scope limits (v1 — Story 11.12 D-locks):
/// - NO snippet/highlight field — Examine 3.x ships no first-party highlighter API,
///   and term-vector indexing is not enabled on Umbraco's default indexer. Returning
///   arbitrary truncations would mislead LLMs; agents must call <c>get_content</c>
///   for match context.
/// - External index only — Internal index (backoffice / unpublished / members) is not
///   exposed, no configurable <c>indexName</c> parameter.
/// - No pagination (no <c>skip</c>) — agents narrow via <c>contentType</c> / <c>parentId</c>
///   / refined <c>query</c>, not by paging through thousands of hits.
/// - Max 50 results (clamped at the boundary; requests above are silently clamped to 50).
/// - Block List / Block Grid content is NOT indexed by Umbraco's default External indexer
///   — adopters who need block content to be searchable must wire <c>TransformingIndexValues</c>
///   themselves. The tool reliably finds hits on <c>nodeName</c>, plain-text properties,
///   and RTE (via Umbraco's <c>__Raw_*</c> transformation).
/// </summary>
public class SearchContentTool : IWorkflowTool
{
    private const int DefaultCount = 10;
    private const int MaxCount = 50;

    // Umbraco content category on the External index. CreateQuery requires a category —
    // "content" scopes to published content nodes (excludes media, members).
    private const string ContentCategory = "content";

    // Examine field key: Umbraco's content-type alias. Plain `nodeTypeAlias` was dropped
    // in Umbraco v8 (umbraco-cms#9539); only `__NodeTypeAlias` is reliable.
    private const string NodeTypeAliasField = "__NodeTypeAlias";

    // Examine field key: comma-separated ancestor IDs ("-1,1000,1100"). Used for the
    // post-query subtree filter (D6 — we filter in C# rather than via Lucene wildcard).
    private const string PathField = "__Path";

    private const string NodeNameField = "nodeName";
    private const string LevelField = "level";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Keyword query to search against the Umbraco External index. Accepts free text; Lucene reserved characters are escaped automatically."
                },
                "contentType": {
                    "type": "string",
                    "description": "Filter results to this document-type alias (e.g. 'articlePage'). Omit to search across all types."
                },
                "parentId": {
                    "type": ["integer", "string"],
                    "description": "Filter results to descendants of this node — either the Umbraco GUID Key (preferred; durable across environments) or the integer tree ID (legacy; env-variable). Omit to search the whole tree."
                },
                "count": {
                    "type": "integer",
                    "description": "Maximum number of results to return. Default 10, capped at 50."
                }
            },
            "required": ["query"],
            "additionalProperties": false
        }
        """).RootElement;

    private static readonly HashSet<string> KnownParameters = new(StringComparer.Ordinal)
    {
        "query", "contentType", "parentId", "count"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly IExamineManager _examineManager;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly ILogger<SearchContentTool> _logger;

    public string Name => "search_content";

    public string Description =>
        "Keyword search against published Umbraco content (Examine External index). " +
        "Optional filters: contentType (doc-type alias), parentId (subtree). " +
        "Returns up to 50 hits with id / name / contentType / url / level / relevanceScore.";

    public JsonElement? ParameterSchema => Schema;

    public SearchContentTool(
        IExamineManager examineManager,
        IUmbracoContextFactory umbracoContextFactory,
        IPublishedUrlProvider urlProvider,
        ILogger<SearchContentTool> logger)
    {
        _examineManager = examineManager;
        _umbracoContextFactory = umbracoContextFactory;
        _urlProvider = urlProvider;
        _logger = logger;
    }

    public Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "SearchContentTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        ContentToolHelpers.RejectUnknownParameters(arguments, KnownParameters);

        var rawQuery = ContentToolHelpers.ExtractRequiredStringArgument(arguments, "query");
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return Task.FromResult<object>(JsonSerializer.Serialize(new
            {
                error = "invalid_argument",
                message = "'query' must be a non-empty string."
            }));
        }

        var contentType = ContentToolHelpers.ExtractOptionalStringArgument(arguments, "contentType");

        int? parentId;
        Guid? parentKey;
        try
        {
            (parentId, parentKey) = ContentToolHelpers.ExtractOptionalNodeRefArgument(arguments, "parentId");
        }
        catch (ToolExecutionException)
        {
            // Surface malformed-ref validation as a structured error rather than throwing.
            return Task.FromResult<object>(JsonSerializer.Serialize(new
            {
                error = "invalid_argument",
                message = "'parentId' must be a positive integer or a GUID string."
            }));
        }

        int? requestedCount;
        try
        {
            requestedCount = ContentToolHelpers.ExtractOptionalIntArgument(arguments, "count");
        }
        catch (ToolExecutionException)
        {
            return Task.FromResult<object>(JsonSerializer.Serialize(new
            {
                error = "invalid_argument",
                message = "'count' must be a positive integer."
            }));
        }

        int count = requestedCount ?? DefaultCount;
        if (count > MaxCount)
        {
            _logger.LogDebug(
                "search_content: count {Requested} clamped to {Max} (v1 maximum)",
                count, MaxCount);
            count = MaxCount;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_examineManager.TryGetIndex(Constants.UmbracoIndexes.ExternalIndexName, out var index))
        {
            _logger.LogWarning(
                "search_content: IExamineManager.TryGetIndex('{IndexName}') returned false",
                Constants.UmbracoIndexes.ExternalIndexName);
            return Task.FromResult<object>(JsonSerializer.Serialize(new
            {
                error = "index_unavailable",
                message = "Umbraco's External Examine index is not available. The site may still be starting up, or the index has not been registered."
            }));
        }

        ISearchResults searchResults;
        try
        {
            // Escape Lucene reserved characters in user-supplied query (injection defence).
            // Examine 3.7.1 does NOT expose an Escape helper despite older docs suggesting
            // otherwise — we use Lucene's own classic parser helper directly.
            var escapedQuery = QueryParser.Escape(rawQuery);

            IBooleanOperation queryOp = index.Searcher
                .CreateQuery(ContentCategory, BooleanOperation.And)
                .NativeQuery(escapedQuery);

            if (!string.IsNullOrWhiteSpace(contentType))
            {
                // contentType is not Escape()d — Umbraco doc-type aliases are validated
                // identifiers (letter/digit only) with no Lucene-reserved chars by construction.
                queryOp = queryOp.And().Field(NodeTypeAliasField, contentType);
            }

            searchResults = queryOp.Execute(QueryOptions.SkipTake(0, count));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "search_content: Examine query threw {ExceptionType}: {Message}",
                ex.GetType().Name, ex.Message);
            return Task.FromResult<object>(JsonSerializer.Serialize(new
            {
                error = "search_failure",
                message = "Umbraco Examine search failed. The External index may be rebuilding, corrupted, or experiencing an I/O issue."
            }));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var hits = MapHits(searchResults, parentId, parentKey);

        if (hits.Count == 0)
        {
            _logger.LogDebug("search_content: query '{Query}' returned 0 hits", rawQuery);
            return Task.FromResult<object>("[]");
        }

        return Task.FromResult<object>(JsonSerializer.Serialize(hits, JsonOptions));
    }

    // Per-execution dedup gate for the "key resolution failed" Warning so a
    // stale-index cluster of hits doesn't produce per-hit log spam. Reset on
    // every MapHits invocation; never a long-lived static.
    private bool _loggedKeyResolutionFailure;

    private List<object> MapHits(ISearchResults results, int? parentId, Guid? parentKey)
    {
        _loggedKeyResolutionFailure = false;

        // Narrow UmbracoContext scope to the URL-provider + key-resolution call paths (D10).
        // IPublishedUrlProvider.GetUrl(int) and IPublishedContentCache.GetById(int) require
        // an ambient Umbraco context; the Examine query itself does not. We materialise the
        // list inside the scope so the context stays open for all lookup calls.
        using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();
        var contentCache = contextReference.UmbracoContext.Content;

        // Resolve GUID parentKey → int at the filter boundary (deferred-work option a) so
        // the existing __Path CSV-containment logic can continue to use integer matching
        // — Umbraco's path field is integer-based by construction. Returns null if the
        // GUID doesn't resolve to a published node; the filter then rejects every hit.
        int? effectiveParentId = parentId;
        if (!effectiveParentId.HasValue && parentKey.HasValue && contentCache is not null)
        {
            var parent = contentCache.GetById(parentKey.Value);
            effectiveParentId = parent?.Id;
        }

        var list = new List<object>();
        var preFilterCount = 0;

        foreach (var r in results)
        {
            preFilterCount++;

            if (!int.TryParse(r.Id, out var id))
            {
                _logger.LogWarning(
                    "search_content: skipping result with non-integer Id '{Id}'",
                    r.Id);
                continue;
            }

            if (effectiveParentId.HasValue && !IsDescendantOf(r, effectiveParentId.Value))
            {
                continue;
            }

            // Resolve GUID Key for each hit — prefer Examine's __Key field if indexed,
            // fall back to content-cache lookup. Umbraco's default External index writes
            // __Key for all content items, so the Examine path is the common case.
            //
            // When neither source resolves the key (rare — stale index entry pointing at
            // a deleted node, or a custom indexer that omits __Key), emit `key: null`
            // rather than `Guid.Empty`. Guid.Empty would silently collide across failed
            // hits AND get rejected by ExtractRequiredNodeRefArgument — confusing for
            // downstream agents. A null key signals "key unavailable; fall back to id
            // for this hit" and is logged once per execution so operators can diagnose
            // index drift without log-spam per hit.
            Guid? key = null;
            if (r.Values.TryGetValue("__Key", out var keyRaw)
                && !string.IsNullOrWhiteSpace(keyRaw)
                && Guid.TryParse(keyRaw, out var parsedKey)
                && parsedKey != Guid.Empty)
            {
                key = parsedKey;
            }
            else if (contentCache is not null)
            {
                var cacheKey = contentCache.GetById(id)?.Key;
                if (cacheKey.HasValue && cacheKey.Value != Guid.Empty)
                {
                    key = cacheKey.Value;
                }
            }

            if (!key.HasValue && !_loggedKeyResolutionFailure)
            {
                _loggedKeyResolutionFailure = true;
                _logger.LogWarning(
                    "search_content: one or more hits returned without a resolvable GUID key (id {Id}) — agents will see `key: null` and should fall back to `id`. This typically indicates index drift or a custom indexer that omits __Key.",
                    id);
            }

            list.Add(new
            {
                key,
                id,
                name = r.Values.TryGetValue(NodeNameField, out var n) ? n ?? string.Empty : string.Empty,
                contentType = r.Values.TryGetValue(NodeTypeAliasField, out var t) ? t ?? string.Empty : string.Empty,
                url = _urlProvider.GetUrl(id) ?? string.Empty,
                level = r.Values.TryGetValue(LevelField, out var l) && int.TryParse(l, out var lv) ? lv : 0,
                relevanceScore = (float)Math.Round(r.Score, 2)
            });
        }

        if (effectiveParentId.HasValue && list.Count < preFilterCount)
        {
            _logger.LogDebug(
                "search_content: parentId {ParentId} filter reduced {PreFilter} hits to {PostFilter} (descendants of {ParentId})",
                effectiveParentId.Value, preFilterCount, list.Count, effectiveParentId.Value);
        }

        return list;
    }

    private static bool IsDescendantOf(ISearchResult result, int parentId)
    {
        // The __Path field is a CSV of ancestor IDs ("-1,1000,1100"). Containment check
        // uses comma-delimited segments to avoid the prefix-collision bug (node 10000 would
        // false-match "1000" under a naive Contains). Both surrounded (",1000,") and
        // end-anchored (",1000") match — the latter catches the case where the matched
        // node IS the parent itself (included, matching AncestorsOrSelf semantics).
        if (!result.Values.TryGetValue(PathField, out var path) || path is null)
        {
            return false;
        }

        var needle = "," + parentId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return path.Contains(needle + ",", StringComparison.Ordinal)
            || path.EndsWith(needle, StringComparison.Ordinal);
    }

}
