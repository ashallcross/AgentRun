using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRun.Umbraco.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Contexts;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace AgentRun.Umbraco.Tools;

/// <summary>
/// <c>get_ai_context</c> tool. Read-only lookup of Umbraco.AI Contexts by
/// alias or by content-node tree-inheritance. Story 11.9.
/// </summary>
/// <remarks>
/// <para>
/// Returns structured JSON envelopes for Umbraco.AI service failures and
/// argument-validation errors. Throws <see cref="OperationCanceledException"/>
/// on cancellation, <see cref="ToolContextMissingException"/> when the
/// executor has not populated <c>Step</c>/<c>Workflow</c> on the context
/// (engine-wiring bug), and <see cref="ToolExecutionException"/> when the
/// Umbraco content cache is unavailable (site still starting up).
/// </para>
/// <para>
/// Alias mode looks up directly via <c>IAIContextService.GetContextByAliasAsync</c>.
/// <c>content_node_id</c> mode walks the published content tree (self +
/// ancestors) for the first <c>Uai.ContextPicker</c> property with a
/// resolvable Context — same semantics as
/// <c>Umbraco.AI.Core.Contexts.Resolvers.ContentContextResolver</c> but
/// invoked from a background <c>IHostedService</c> context without
/// manufacturing an <c>IAIRuntimeContextScopeProvider</c> scope.
/// </para>
/// <para>
/// <see cref="IAIContextService"/> is resolved per-call via
/// <see cref="IServiceScopeFactory"/> because its likely-scoped lifetime
/// (depends on <c>IBackOfficeSecurityAccessor</c>) makes direct injection
/// into a singleton tool unsafe.
/// </para>
/// </remarks>
public sealed class GetAiContextTool : IWorkflowTool
{
    private const string ContextPickerEditorAlias = "Uai.ContextPicker";

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "alias": {
              "type": "string",
              "description": "The alias of the Umbraco.AI Context to retrieve (e.g. \"corporate-brand-voice\"). Takes precedence over content_node_id if both are supplied."
            },
            "content_node_id": {
              "type": ["integer", "string"],
              "description": "The integer ID or GUID Key of a published content node whose AI Context Picker property (or its nearest ancestor's) selects the Context to retrieve. Either representation is accepted — the backoffice URL typically shows the GUID."
            }
          },
          "additionalProperties": false
        }
        """).RootElement;

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GetAiContextTool> _logger;

    public GetAiContextTool(
        IUmbracoContextFactory umbracoContextFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<GetAiContextTool> logger)
    {
        _umbracoContextFactory = umbracoContextFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public string Name => "get_ai_context";

    public string Description =>
        "Reads an Umbraco.AI Context by alias (direct) or by content_node_id " +
        "(walks self + ancestors for the nearest Uai.ContextPicker property). " +
        "Returns JSON with alias, name, version, and resources (each with " +
        "injectionMode=always|on_demand, resourceTypeId, sortOrder, settings). " +
        "Use for brand voice, tone-of-voice guidance, and user-curated reference text.";

    public JsonElement? ParameterSchema => Schema;

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "GetAiContextTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        try
        {
            GetAiContextToolHelpers.RejectUnknownParameters(arguments);
        }
        catch (ToolExecutionException ex)
        {
            return InvalidArgument(ex.Message);
        }

        if (!TryExtractAlias(arguments, out var alias, out var aliasError))
            return InvalidArgument(aliasError!);

        // When alias is supplied it wins (AC6 precedence) — skip validation of
        // content_node_id entirely so a malformed node ref alongside a valid
        // alias does not shadow the alias path with invalid_argument.
        int? nodeIntId = null;
        Guid? nodeGuidKey = null;
        string? nodeRefDisplay = null;
        var hasNodeRef = false;

        if (alias is null)
        {
            if (!TryExtractContentNodeRef(arguments, out nodeIntId, out nodeGuidKey, out nodeRefDisplay, out var nodeIdError))
                return InvalidArgument(nodeIdError!);
            hasNodeRef = nodeIntId is not null || nodeGuidKey is not null;

            if (!hasNodeRef)
            {
                return InvalidArgument("get_ai_context requires either 'alias' or 'content_node_id'.");
            }
        }
        else if (arguments.TryGetValue("content_node_id", out var raw) && raw is not null)
        {
            _logger.LogDebug(
                "get_ai_context: both 'alias' and 'content_node_id' supplied; using alias '{Alias}' (step {StepId})",
                alias, context.StepId);
        }

        if (alias is not null)
        {
            return await ExecuteAliasMode(alias, context, cancellationToken);
        }

        return await ExecuteContentNodeIdModeAsync(nodeIntId, nodeGuidKey, nodeRefDisplay!, context, cancellationToken);
    }

    private async Task<string> ExecuteAliasMode(
        string alias,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var contextService = scope.ServiceProvider.GetRequiredService<IAIContextService>();
            var ctx = await contextService.GetContextByAliasAsync(alias, cancellationToken);

            if (ctx is null)
            {
                _logger.LogWarning(
                    "get_ai_context: alias '{Alias}' not found (step {StepId})",
                    alias, context.StepId);
                return NotFound(alias);
            }

            return SerialiseContext(ctx, resolvedFrom: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "get_ai_context: IAIContextService threw {ExceptionType} in alias mode for '{Alias}' (step {StepId})",
                ex.GetType().Name, alias, context.StepId);
            return ContextServiceFailure();
        }
    }

    private async Task<string> ExecuteContentNodeIdModeAsync(
        int? contentNodeIntId,
        Guid? contentNodeGuidKey,
        string contentNodeRefDisplay,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            using var contextRef = _umbracoContextFactory.EnsureUmbracoContext();
            var contentCache = contextRef.UmbracoContext.Content;

            if (contentCache is null)
            {
                throw new ToolExecutionException(
                    "Umbraco content cache is not available — the site may still be starting up");
            }

            var node = contentNodeIntId is not null
                ? contentCache.GetById(contentNodeIntId.Value)
                : contentCache.GetById(contentNodeGuidKey!.Value);

            if (node is null)
            {
                return ContentNotFound(contentNodeRefDisplay);
            }

            using var scope = _scopeFactory.CreateScope();
            var contextService = scope.ServiceProvider.GetRequiredService<IAIContextService>();

            var resolved = await ResolveFromTreeAsync(node, contextService, context, cancellationToken);

            if (resolved is null)
            {
                _logger.LogDebug(
                    "get_ai_context: no context picker property found on node {ContentNodeRef} or ancestors (step {StepId})",
                    contentNodeRefDisplay, context.StepId);
                return NoContextForNode(contentNodeRefDisplay);
            }

            var (aiContext, resolvedNodeId, resolvedNodeName) = resolved.Value;
            return SerialiseContext(aiContext, new ResolvedFromDto(resolvedNodeId, resolvedNodeName));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ToolExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "get_ai_context: IAIContextService or content cache threw {ExceptionType} in content_node_id mode for {ContentNodeRef} (step {StepId})",
                ex.GetType().Name, contentNodeRefDisplay, context.StepId);
            return ContextServiceFailure();
        }
    }

    private async Task<(AIContext Context, int NodeId, string NodeName)?> ResolveFromTreeAsync(
        IPublishedContent startNode,
        IAIContextService contextService,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Walk self + ancestors via .Parent chain. Matches Umbraco's
        // AncestorsOrSelf() semantics but does not depend on the extension
        // method (which would need an IPublishedSnapshotAccessor).
        IPublishedContent? current = startNode;
        while (current is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pickerProperty = current.Properties?.FirstOrDefault(p =>
                string.Equals(
                    p?.PropertyType?.EditorAlias,
                    ContextPickerEditorAlias,
                    StringComparison.OrdinalIgnoreCase));

            if (pickerProperty is not null)
            {
                var rawValue = pickerProperty.GetValue();
                // Materialise once — helper uses `yield return` with
                // side-effectful warning logs, so double-iteration duplicates
                // log entries on malformed values.
                var guids = GetAiContextToolHelpers
                    .ExtractContextGuids(rawValue, _logger, current.Id)
                    .ToList();

                if (guids.Count > 1)
                {
                    _logger.LogInformation(
                        "get_ai_context: multi-picker on node {ContentNodeId} has {Count} entries; using first resolvable (step {StepId})",
                        current.Id, guids.Count, context.StepId);
                }

                foreach (var guid in guids)
                {
                    var ctx = await contextService.GetContextAsync(guid, cancellationToken);
                    if (ctx is null)
                    {
                        _logger.LogWarning(
                            "get_ai_context: picker on node {ContentNodeId} references missing Context {Guid} (step {StepId})",
                            current.Id, guid, context.StepId);
                        continue;
                    }
                    return (ctx, current.Id, current.Name ?? string.Empty);
                }
            }

            // IPublishedContent.Parent is the canonical self/parent walk in
            // Umbraco 17. The replacement (INavigationQueryService +
            // IPublishedStatusFilteringService) requires two extra services
            // for no behavioural gain; migrate when Umbraco 18 drops the
            // property. Tracking alongside GetContentTool.cs:486 CS0618.
#pragma warning disable CS0618
            current = current.Parent;
#pragma warning restore CS0618
        }

        return null;
    }

    private static string SerialiseContext(AIContext ctx, ResolvedFromDto? resolvedFrom)
    {
        var resources = (ctx.Resources ?? Enumerable.Empty<AIContextResource>())
            .OrderBy(r => r.SortOrder)
            .Select(r => new ResourceDto(
                r.Name,
                r.Description,
                r.ResourceTypeId,
                r.SortOrder,
                r.InjectionMode,
                r.Settings))
            .ToArray();

        var dto = new ContextResultDto(
            ctx.Alias,
            ctx.Name,
            ctx.Version,
            resolvedFrom,
            resources);

        return JsonSerializer.Serialize(dto, ResultJsonOptions);
    }

    private static bool TryExtractAlias(IDictionary<string, object?> args, out string? alias, out string? error)
    {
        alias = null;
        error = null;
        if (!args.TryGetValue("alias", out var raw) || raw is null)
            return true;

        var value = raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => raw.ToString()
        };

        if (value is null) return true;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "'alias' must be a non-empty string.";
            return false;
        }
        alias = value;
        return true;
    }

    private static bool TryExtractContentNodeRef(
        IDictionary<string, object?> args,
        out int? contentNodeId,
        out Guid? contentNodeKey,
        out string? displayRef,
        out string? error)
    {
        contentNodeId = null;
        contentNodeKey = null;
        displayRef = null;
        error = null;
        if (!args.TryGetValue("content_node_id", out var raw) || raw is null)
            return true;

        // Unwrap JsonElement to its native value so shared parsing below applies
        // to both raw CLR objects and System.Text.Json boxed values.
        object? unwrapped = raw;
        if (raw is JsonElement je)
        {
            unwrapped = je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt64(out var n) ? n : (object?)null,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                _ => null
            };
        }

        // Integer path (handles int, long, double-whole, or numeric string).
        int? parsedInt = unwrapped switch
        {
            int i => i,
            long l when l is <= int.MaxValue and >= int.MinValue => (int)l,
            double d when !double.IsNaN(d) && !double.IsInfinity(d) && d <= int.MaxValue && d >= int.MinValue => (int)d,
            string s when int.TryParse(s, out var n) => n,
            _ => null
        };

        if (parsedInt is not null)
        {
            if (parsedInt <= 0)
            {
                error = "'content_node_id' must be a positive integer.";
                return false;
            }
            contentNodeId = parsedInt;
            displayRef = parsedInt.Value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        // GUID path — string that parses as Guid.
        if (unwrapped is string str && !string.IsNullOrWhiteSpace(str))
        {
            if (Guid.TryParse(str, out var key) && key != Guid.Empty)
            {
                contentNodeKey = key;
                displayRef = key.ToString("D", CultureInfo.InvariantCulture);
                return true;
            }
        }

        error = "'content_node_id' must be a positive integer or a GUID string.";
        return false;
    }

    private static string InvalidArgument(string message)
        => JsonSerializer.Serialize(new ErrorEnvelope("invalid_argument", null, null, message), ResultJsonOptions);

    private static string NotFound(string alias)
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "not_found",
            alias,
            null,
            $"No Umbraco.AI Context found with alias '{alias}'."), ResultJsonOptions);

    private static string ContentNotFound(string contentNodeRef)
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "content_not_found",
            null,
            contentNodeRef,
            $"Content node {contentNodeRef} not found or is not published."), ResultJsonOptions);

    private static string NoContextForNode(string contentNodeRef)
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "no_context_for_node",
            null,
            contentNodeRef,
            $"No Umbraco.AI Context is configured for content node {contentNodeRef} or any of its ancestors."), ResultJsonOptions);

    private static string ContextServiceFailure()
        => JsonSerializer.Serialize(new ErrorEnvelope(
            "context_service_failure",
            null,
            null,
            "Umbraco.AI Context service is temporarily unavailable. Try again in a moment or proceed without context."), ResultJsonOptions);

    private sealed record ContextResultDto(
        [property: JsonPropertyName("alias")] string Alias,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("resolvedFrom")] ResolvedFromDto? ResolvedFrom,
        [property: JsonPropertyName("resources")] IReadOnlyList<ResourceDto> Resources);

    private sealed record ResolvedFromDto(
        [property: JsonPropertyName("contentNodeId")] int ContentNodeId,
        [property: JsonPropertyName("contentNodeName")] string ContentNodeName);

    private sealed record ResourceDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("resourceTypeId")] string ResourceTypeId,
        [property: JsonPropertyName("sortOrder")] int SortOrder,
        [property: JsonPropertyName("injectionMode")] AIContextResourceInjectionMode InjectionMode,
        [property: JsonPropertyName("settings")] object? Settings);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("alias")] string? Alias,
        [property: JsonPropertyName("contentNodeId")] string? ContentNodeId,
        [property: JsonPropertyName("message")] string Message);
}
