using System.Text.Json;
using AgentRun.Umbraco.Engine;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace AgentRun.Umbraco.Tools;

public class ListContentTypesTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "alias": {
                    "type": "string",
                    "description": "Filter by document type alias. Returns all document types if omitted."
                }
            },
            "additionalProperties": false
        }
        """).RootElement;

    private static readonly HashSet<string> KnownParameters = new(StringComparer.Ordinal) { "alias" };

    private readonly IContentTypeService _contentTypeService;
    private readonly IToolLimitResolver _limitResolver;

    public string Name => "list_content_types";

    public string Description =>
        "Lists document type definitions from the Umbraco instance, including their properties, " +
        "compositions, and allowed child types.";

    public JsonElement? ParameterSchema => Schema;

    public ListContentTypesTool(
        IContentTypeService contentTypeService,
        IToolLimitResolver limitResolver)
    {
        _contentTypeService = contentTypeService;
        _limitResolver = limitResolver;
    }

    public Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "ListContentTypesTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        RejectUnknownParameters(arguments);

        var aliasFilter = ExtractOptionalStringArgument(arguments, "alias");

        IEnumerable<IContentType> contentTypes;

        if (!string.IsNullOrEmpty(aliasFilter))
        {
            var single = _contentTypeService.Get(aliasFilter);
            contentTypes = single is not null ? [single] : [];
        }
        else
        {
            contentTypes = _contentTypeService.GetAll();
        }

        var items = contentTypes.Select(ct => new
        {
            alias = ct.Alias,
            name = ct.Name ?? string.Empty,
            description = ct.Description ?? string.Empty,
            icon = ct.Icon ?? string.Empty,
            properties = ct.CompositionPropertyTypes.Select(pt => new
            {
                alias = pt.Alias,
                name = pt.Name ?? string.Empty,
                editorAlias = pt.PropertyEditorAlias,
                mandatory = pt.Mandatory
            }).ToList(),
            compositions = ct.ContentTypeComposition
                .Select(c => c.Alias)
                .ToList(),
            allowedChildTypes = ct.AllowedContentTypes?
                .Select(a => a.Alias)
                .Where(a => a is not null)
                .ToList() ?? []
        }).ToList();

        var limit = _limitResolver.ResolveListContentTypesMaxResponseBytes(context.Step, context.Workflow);
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
            var marker = $"[Response truncated — returned {items.Count} of {totalCount} document types. " +
                         "Use alias filter to narrow results.]";
            var candidate = truncatedJson + marker;

            if (System.Text.Encoding.UTF8.GetByteCount(candidate) <= limit)
            {
                return Task.FromResult<object>(candidate);
            }
        }

        var emptyMarker = $"[Response truncated — returned 0 of {totalCount} document types. " +
                          "Use alias filter to narrow results.]";
        return Task.FromResult<object>("[]" + emptyMarker);
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
}
