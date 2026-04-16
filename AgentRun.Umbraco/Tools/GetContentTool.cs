using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;

namespace AgentRun.Umbraco.Tools;

public class GetContentTool : IWorkflowTool
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "id": {
                    "type": "integer",
                    "description": "The ID of the published content node to retrieve."
                }
            },
            "required": ["id"],
            "additionalProperties": false
        }
        """).RootElement;

    private static readonly HashSet<string> KnownParameters = new(StringComparer.Ordinal) { "id" };

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IToolLimitResolver _limitResolver;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GetContentTool> _logger;

    public string Name => "get_content";

    public string Description =>
        "Gets the full details and property values of a single published content node by ID.";

    public JsonElement? ParameterSchema => Schema;

    public GetContentTool(
        IUmbracoContextFactory umbracoContextFactory,
        IPublishedUrlProvider urlProvider,
        IToolLimitResolver limitResolver,
        IServiceScopeFactory scopeFactory,
        ILogger<GetContentTool> logger)
    {
        _umbracoContextFactory = umbracoContextFactory;
        _urlProvider = urlProvider;
        _limitResolver = limitResolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private static readonly JsonSerializerOptions HandleJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public async Task<object> ExecuteAsync(
        IDictionary<string, object?> arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Step is null || context.Workflow is null)
        {
            throw new ToolContextMissingException(
                "GetContentTool requires ToolExecutionContext.Step and .Workflow to be set by the executor. " +
                "This is an engine wiring bug, not a workflow configuration issue.");
        }

        if (string.IsNullOrEmpty(context.InstanceFolderPath))
        {
            throw new InvalidOperationException(
                "GetContentTool requires ToolExecutionContext.InstanceFolderPath to be set " +
                "so the tool can offload content to the instance scratch path.");
        }

        ContentToolHelpers.RejectUnknownParameters(arguments, KnownParameters);

        var id = ContentToolHelpers.ExtractRequiredIntArgument(arguments, "id");

        using var contextReference = _umbracoContextFactory.EnsureUmbracoContext();
        var contentCache = contextReference.UmbracoContext.Content;

        if (contentCache is null)
        {
            throw new ToolExecutionException(
                "Umbraco content cache is not available — the site may still be starting up");
        }

        var node = contentCache.GetById(id);
        if (node is null)
        {
            throw new ToolExecutionException(
                $"Content node with ID {id} not found or is not published");
        }

        // Resolve creator name
        var creatorName = ResolveCreatorName(node.CreatorId);

        var properties = ExtractProperties(node);

        var result = new
        {
            id = node.Id,
            name = node.Name ?? string.Empty,
            contentType = node.ContentType.Alias,
            url = _urlProvider.GetUrl(node) ?? string.Empty,
            level = node.Level,
            createDate = node.CreateDate.ToString("O"),
            updateDate = node.UpdateDate.ToString("O"),
            creatorName,
            templateAlias = ResolveTemplateAlias(node.TemplateId),
            properties
        };

        var limit = _limitResolver.ResolveGetContentMaxResponseBytes(context.Step, context.Workflow);
        var propertyList = properties.ToList();
        var totalPropertyCount = propertyList.Count;

        var (json, includedCount) = ContentToolHelpers.TruncateToByteLimit(
            propertyList,
            limit,
            sub =>
            {
                var subset = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kvp in sub)
                {
                    subset[kvp.Key] = kvp.Value;
                }
                var subsetResult = new
                {
                    id = result.id,
                    name = result.name,
                    contentType = result.contentType,
                    url = result.url,
                    level = result.level,
                    createDate = result.createDate,
                    updateDate = result.updateDate,
                    creatorName = result.creatorName,
                    templateAlias = result.templateAlias,
                    properties = subset
                };
                return JsonSerializer.Serialize(subsetResult);
            },
            count => $"[Response truncated — returned {count} of {totalPropertyCount} properties. " +
                     "Override get_content.max_response_bytes in your workflow configuration to increase the limit.]");

        var truncated = includedCount < totalPropertyCount;

        // Offload: write full result to .content-cache/{contentId}.json, return handle
        var relPath = $".content-cache/{id}.json";

        string validatedPath;
        try
        {
            validatedPath = PathSandbox.ValidatePath(relPath, context.InstanceFolderPath);
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            throw new ToolExecutionException(
                $"Failed to cache get_content response to {relPath}: {ex.Message}");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(validatedPath)!);
            await File.WriteAllTextAsync(validatedPath, json, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex,
                "Failed to write content cache for node {NodeId} in instance {InstanceId}, step {StepId}",
                id, context.InstanceId, context.StepId);
            throw new ToolExecutionException(
                $"Failed to cache get_content response to {relPath}: {ex.Message}");
        }

        var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);
        var nodeName = node.Name ?? string.Empty;
        // Truncate name in handle to keep handle under 1 KB
        if (nodeName.Length > 100)
            nodeName = nodeName[..100] + "...";

        var handle = new GetContentHandle(
            id,
            nodeName,
            node.ContentType.Alias,
            _urlProvider.GetUrl(node) ?? string.Empty,
            includedCount,
            sizeBytes,
            relPath,
            truncated);

        return JsonSerializer.Serialize(handle, HandleJsonOptions);
    }

    private sealed record GetContentHandle(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("propertyCount")] int PropertyCount,
        [property: JsonPropertyName("size_bytes")] int SizeBytes,
        [property: JsonPropertyName("saved_to")] string SavedTo,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private Dictionary<string, object?> ExtractProperties(IPublishedContent node)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in node.Properties)
        {
            try
            {
                var value = ExtractPropertyValue(node, property);
                properties[property.Alias] = value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to extract property '{PropertyAlias}' (editor: {EditorAlias}) on node {NodeId}",
                    property.Alias, property.PropertyType.EditorAlias, node.Id);

                try
                {
                    var sourceValue = property.GetSourceValue(culture: null, segment: null);
                    properties[property.Alias] = sourceValue is not null
                        ? JsonSerializer.Serialize(sourceValue)
                        : "[extraction failed]";
                }
                catch
                {
                    properties[property.Alias] = "[extraction failed]";
                }
            }
        }

        return properties;
    }

    private object? ExtractPropertyValue(IPublishedContent node, IPublishedProperty property)
    {
        var editorAlias = property.PropertyType.EditorAlias;

        if (!property.HasValue(culture: null, segment: null))
        {
            return null;
        }

        return editorAlias switch
        {
            "Umbraco.RichText" => ExtractRichText(property),
            "Umbraco.TextBox" or "Umbraco.TextArea" => ExtractText(property),
            "Umbraco.ContentPicker" => ExtractContentPicker(property),
            "Umbraco.MediaPicker3" => ExtractMediaPicker(property),
            "Umbraco.BlockList" => ExtractBlockList(property),
            "Umbraco.BlockGrid" => ExtractBlockGrid(property),
            "Umbraco.TrueFalse" => ExtractTrueFalse(property),
            "Umbraco.Integer" => ExtractInteger(property),
            "Umbraco.Decimal" => ExtractDecimal(property),
            "Umbraco.DateTime" => ExtractDateTime(property),
            "Umbraco.Tags" => ExtractTags(property),
            _ => ExtractFallback(property)
        };
    }

    private static string ExtractRichText(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        // Umbraco RichText value converter may return IHtmlEncodedString — use ToHtmlString()
        // to get raw HTML rather than ToString() which may return encoded output.
        var html = value is IHtmlEncodedString hes
            ? hes.ToHtmlString()
            : value?.ToString() ?? string.Empty;
        // Strip HTML tags using simple regex (no AngleSharp dependency per story spec)
        var text = HtmlTagRegex.Replace(html, string.Empty);
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    private static string? ExtractText(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        return value?.ToString();
    }

    private string ExtractContentPicker(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        if (value is IPublishedContent picked)
        {
            var url = _urlProvider.GetUrl(picked);
            return $"{picked.Name} ({url})";
        }

        return "not published";
    }

    private string ExtractMediaPicker(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);

        if (value is IPublishedContent singleMedia)
        {
            return FormatMediaItem(singleMedia);
        }

        if (value is IEnumerable<IPublishedContent> multipleMedia)
        {
            var items = multipleMedia.Select(FormatMediaItem).ToList();
            return string.Join("; ", items);
        }

        return string.Empty;
    }

    private string FormatMediaItem(IPublishedContent media)
    {
        var url = _urlProvider.GetUrl(media);
        // Get altText via direct property access to avoid StaticServiceProvider dependency
        var altTextProp = media.GetProperty("altText");
        var altText = altTextProp is not null && altTextProp.HasValue(culture: null, segment: null)
            ? altTextProp.GetValue(culture: null, segment: null)?.ToString() ?? string.Empty
            : string.Empty;
        return string.IsNullOrEmpty(altText)
            ? $"{media.Name} ({url})"
            : $"{media.Name} ({url}) alt=\"{altText}\"";
    }

    private string ExtractBlockList(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        if (value is IEnumerable<object> blocks)
        {
            var labels = new List<string>();
            foreach (var block in blocks)
            {
                // BlockListItem<T>/BlockGridItem<T> declares a typed Content property that
                // hides the base IPublishedElement Content — GetProperty("Content") throws
                // AmbiguousMatchException. Use GetProperties() to sidestep the ambiguity.
                var contentProp = block.GetType().GetProperties()
                    .FirstOrDefault(p => p.Name == "Content"
                        && typeof(IPublishedElement).IsAssignableFrom(p.PropertyType));
                if (contentProp?.GetValue(block) is IPublishedElement element)
                {
                    var parts = new List<string> { $"[{element.ContentType.Alias}]" };
                    foreach (var prop in element.Properties)
                    {
                        if (!prop.HasValue(culture: null, segment: null))
                            continue;

                        // Route through the same editor-aware extraction used for top-level
                        // properties so that RTEs, media pickers, etc. inside blocks are
                        // handled correctly instead of being silently skipped.
                        var extracted = ExtractElementPropertyValue(prop);
                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            parts.Add($"{prop.Alias}=\"{extracted}\"");
                        }
                    }
                    labels.Add(string.Join(" ", parts));
                }
            }

            return string.Join("; ", labels);
        }

        return string.Empty;
    }

    private static string? ExtractElementPropertyValue(IPublishedProperty property)
    {
        var editorAlias = property.PropertyType.EditorAlias;
        var result = editorAlias switch
        {
            "Umbraco.RichText" => ExtractRichText(property),
            "Umbraco.TextBox" or "Umbraco.TextArea" => ExtractText(property),
            "Umbraco.TrueFalse" => ExtractTrueFalse(property),
            "Umbraco.Integer" => ExtractInteger(property),
            "Umbraco.Decimal" => ExtractDecimal(property),
            "Umbraco.DateTime" => ExtractDateTime(property),
            "Umbraco.Tags" => ExtractTags(property),
            _ => property.GetValue(culture: null, segment: null)?.ToString()
        };

        if (string.IsNullOrWhiteSpace(result))
            return null;

        // Truncate long values to keep block representation concise
        return result.Length > 500 ? result[..500] + "..." : result;
    }

    private string ExtractBlockGrid(IPublishedProperty property)
    {
        // Same approach as BlockList — extract top-level block content type aliases
        return ExtractBlockList(property);
    }

    private static string ExtractTrueFalse(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        return value is true ? "true" : "false";
    }

    private static string ExtractInteger(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        return value?.ToString() ?? string.Empty;
    }

    private static string ExtractDecimal(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        return value?.ToString() ?? string.Empty;
    }

    private static string ExtractDateTime(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        if (value is DateTime dt)
        {
            return dt.ToString("O");
        }

        return value?.ToString() ?? string.Empty;
    }

    private static string ExtractTags(IPublishedProperty property)
    {
        var value = property.GetValue(culture: null, segment: null);
        if (value is IEnumerable<string> tags)
        {
            return string.Join(", ", tags);
        }

        return value?.ToString() ?? string.Empty;
    }

    private static object? ExtractFallback(IPublishedProperty property)
    {
        var value = property.GetSourceValue(culture: null, segment: null);
        if (value is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(value);
    }

    private string ResolveCreatorName(int creatorId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
            var profile = userService.GetProfileById(creatorId);
            return profile?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve creator name for ID {CreatorId}", creatorId);
            return string.Empty;
        }
    }

    private string ResolveTemplateAlias(int? templateId)
    {
        if (!templateId.HasValue)
        {
            return string.Empty;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();
            var template = fileService.GetTemplate(templateId.Value);
            return template?.Alias ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve template alias for ID {TemplateId}", templateId.Value);
            return string.Empty;
        }
    }

}
