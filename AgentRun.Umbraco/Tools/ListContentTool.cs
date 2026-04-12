using System.Text.Json;
using AgentRun.Umbraco.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Core.Routing;

namespace AgentRun.Umbraco.Tools;

public class ListContentTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "contentType": {
                    "type": "string",
                    "description": "Filter by document type alias (e.g. 'blogPost'). Returns all types if omitted."
                },
                "parentId": {
                    "type": "integer",
                    "description": "Filter to direct children of this content node ID. Returns all content if omitted."
                }
            },
            "additionalProperties": false
        }
        """).RootElement;

    private static readonly HashSet<string> KnownParameters = new(StringComparer.Ordinal)
    {
        "contentType", "parentId"
    };

    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IDocumentNavigationQueryService _navigationQueryService;
    private readonly IPublishedContentStatusFilteringService _statusFilteringService;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IToolLimitResolver _limitResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListContentTool> _logger;

    public string Name => "list_content";

    public string Description =>
        "Lists published content nodes from the Umbraco instance. " +
        "Optionally filter by document type alias or parent node ID.";

    public JsonElement? ParameterSchema => Schema;

    public ListContentTool(
        IUmbracoContextFactory umbracoContextFactory,
        IDocumentNavigationQueryService navigationQueryService,
        IPublishedContentStatusFilteringService statusFilteringService,
        IPublishedUrlProvider urlProvider,
        IToolLimitResolver limitResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<ListContentTool> logger)
    {
        _umbracoContextFactory = umbracoContextFactory;
        _navigationQueryService = navigationQueryService;
        _statusFilteringService = statusFilteringService;
        _urlProvider = urlProvider;
        _limitResolver = limitResolver;
        _scopeFactory = scopeFactory;
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
                "ListContentTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        RejectUnknownParameters(arguments);

        var contentTypeFilter = ExtractOptionalStringArgument(arguments, "contentType");
        var parentId = ExtractOptionalIntArgument(arguments, "parentId");

        using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();
        var contentCache = contextReference.UmbracoContext.Content;

        if (contentCache is null)
        {
            throw new ToolExecutionException(
                "Umbraco content cache is not available — the site may still be starting up");
        }

        var nodes = CollectNodes(contentCache, contentTypeFilter, parentId);

        // Resolve creator names
        var creatorNames = ResolveCreatorNames(nodes);

        var items = nodes.Select(n => new
        {
            id = n.Id,
            name = n.Name ?? string.Empty,
            contentType = n.ContentType.Alias,
            url = _urlProvider.GetUrl(n) ?? string.Empty,
            level = n.Level,
            createDate = n.CreateDate.ToString("O"),
            updateDate = n.UpdateDate.ToString("O"),
            creatorName = creatorNames.GetValueOrDefault(n.CreatorId, string.Empty),
            childCount = GetChildCount(n)
        }).ToList();

        var limit = _limitResolver.ResolveListContentMaxResponseBytes(context.Step, context.Workflow);
        var json = JsonSerializer.Serialize(items);

        if (System.Text.Encoding.UTF8.GetByteCount(json) <= limit)
        {
            return Task.FromResult<object>(json);
        }

        // Truncate: remove items from the end until under limit
        var totalCount = items.Count;
        while (items.Count > 0)
        {
            items.RemoveAt(items.Count - 1);
            var truncatedJson = JsonSerializer.Serialize(items);
            var marker = $"[Response truncated — returned {items.Count} of {totalCount} content nodes. " +
                         "Use contentType or parentId filters to narrow results.]";
            var candidate = truncatedJson + marker;

            if (System.Text.Encoding.UTF8.GetByteCount(candidate) <= limit)
            {
                return Task.FromResult<object>(candidate);
            }
        }

        // Even zero items exceeds the limit (very small limit configured)
        var emptyMarker = $"[Response truncated — returned 0 of {totalCount} content nodes. " +
                          "Use contentType or parentId filters to narrow results.]";
        return Task.FromResult<object>("[]" + emptyMarker);
    }

    private List<IPublishedContent> CollectNodes(
        global::Umbraco.Cms.Core.PublishedCache.IPublishedContentCache contentCache,
        string? contentTypeFilter,
        int? parentId)
    {
        IEnumerable<IPublishedContent> source;

        if (parentId.HasValue)
        {
            // Get direct children of the specified parent
            var parent = contentCache.GetById(parentId.Value);
            if (parent is null)
            {
                return [];
            }

            if (_navigationQueryService.TryGetChildrenKeys(parent.Key, out var childKeys))
            {
                source = _statusFilteringService.FilterAvailable(childKeys, culture: null);
            }
            else
            {
                return [];
            }
        }
        else
        {
            // Get all published content via tree traversal
            source = GetAllPublishedContent(contentCache);
        }

        if (!string.IsNullOrEmpty(contentTypeFilter))
        {
            source = source.Where(n =>
                string.Equals(n.ContentType.Alias, contentTypeFilter, StringComparison.Ordinal));
        }

        return source.ToList();
    }

    private IEnumerable<IPublishedContent> GetAllPublishedContent(
        global::Umbraco.Cms.Core.PublishedCache.IPublishedContentCache contentCache)
    {
        if (!_navigationQueryService.TryGetRootKeys(out var rootKeys))
        {
            yield break;
        }

        var roots = _statusFilteringService.FilterAvailable(rootKeys, culture: null);

        var queue = new Queue<IPublishedContent>(roots);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;

            if (_navigationQueryService.TryGetChildrenKeys(node.Key, out var childKeys))
            {
                foreach (var child in _statusFilteringService.FilterAvailable(childKeys, culture: null))
                {
                    queue.Enqueue(child);
                }
            }
        }
    }

    private int GetChildCount(IPublishedContent node)
    {
        if (_navigationQueryService.TryGetChildrenKeys(node.Key, out var childKeys))
        {
            return _statusFilteringService.FilterAvailable(childKeys, culture: null).Count();
        }

        return 0;
    }

    private Dictionary<int, string> ResolveCreatorNames(List<IPublishedContent> nodes)
    {
        var result = new Dictionary<int, string>();
        var uniqueIds = nodes.Select(n => n.CreatorId).Distinct().ToList();

        if (uniqueIds.Count == 0)
        {
            return result;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            foreach (var id in uniqueIds)
            {
                var profile = userService.GetProfileById(id);
                result[id] = profile?.Name ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve creator names — returning empty strings");
        }

        return result;
    }

    private static void RejectUnknownParameters(IDictionary<string, object?> arguments)
    {
        var unknown = arguments.Keys.Where(k => !KnownParameters.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolExecutionException(
                $"Unrecognised parameter(s): {string.Join(", ", unknown)}");
        }
    }

    private static string? ExtractOptionalStringArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        return value switch
        {
            string s => string.IsNullOrWhiteSpace(s) ? null : s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };
    }

    private static int? ExtractOptionalIntArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        var intValue = value switch
        {
            int i => i,
            long l when l is > int.MaxValue or < int.MinValue
                => throw new ToolExecutionException($"Argument '{name}' is out of range"),
            long l => (int)l,
            double d when double.IsNaN(d) || double.IsInfinity(d) || d > int.MaxValue || d < int.MinValue
                => throw new ToolExecutionException($"Argument '{name}' is out of range"),
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
            JsonElement { ValueKind: JsonValueKind.String } je when int.TryParse(je.GetString(), out var parsed)
                => parsed,
            _ => throw new ToolExecutionException($"Argument '{name}' must be an integer")
        };

        if (intValue <= 0)
        {
            throw new ToolExecutionException($"Argument '{name}' must be a positive integer");
        }

        return intValue;
    }
}
