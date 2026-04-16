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

        ContentToolHelpers.RejectUnknownParameters(arguments, KnownParameters);

        var aliasFilter = ContentToolHelpers.ExtractOptionalStringArgument(arguments, "alias");

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
        var totalCount = items.Count;

        var (json, _) = ContentToolHelpers.TruncateToByteLimit(
            items,
            limit,
            sub => JsonSerializer.Serialize(sub),
            count => $"[Response truncated — returned {count} of {totalCount} document types. " +
                     "Use alias filter to narrow results.]");

        return Task.FromResult<object>(json);
    }
}
