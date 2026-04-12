using System.Text.Json;
using System.Text.RegularExpressions;
using AgentRun.Umbraco.Engine;
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

    public Task<object> ExecuteAsync(
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

        RejectUnknownParameters(arguments);

        var id = ExtractRequiredIntArgument(arguments, "id");

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
        var json = JsonSerializer.Serialize(result);

        if (System.Text.Encoding.UTF8.GetByteCount(json) <= limit)
        {
            return Task.FromResult<object>(json);
        }

        // Truncate by removing properties from the end until under limit (preserves valid JSON)
        var propertyKeys = properties.Keys.ToList();
        var totalPropertyCount = propertyKeys.Count;
        while (propertyKeys.Count > 0)
        {
            properties.Remove(propertyKeys[^1]);
            propertyKeys.RemoveAt(propertyKeys.Count - 1);

            json = JsonSerializer.Serialize(result);
            var marker = $"[Response truncated — returned {propertyKeys.Count} of {totalPropertyCount} properties. " +
                         "Override get_content.max_response_bytes in your workflow configuration to increase the limit.]";
            var candidate = json + marker;

            if (System.Text.Encoding.UTF8.GetByteCount(candidate) <= limit)
            {
                return Task.FromResult<object>(candidate);
            }
        }

        // Even zero properties exceeds the limit
        json = JsonSerializer.Serialize(result);
        var emptyMarker = $"[Response truncated — returned 0 of {totalPropertyCount} properties. " +
                          "Override get_content.max_response_bytes in your workflow configuration to increase the limit.]";
        return Task.FromResult<object>(json + emptyMarker);
    }

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

    private static string ExtractBlockList(IPublishedProperty property)
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
                    // Extract string-valued properties for a simplified text representation
                    foreach (var prop in element.Properties)
                    {
                        if (prop.HasValue(culture: null, segment: null))
                        {
                            var propVal = prop.GetValue(culture: null, segment: null);
                            if (propVal is string s && !string.IsNullOrWhiteSpace(s))
                            {
                                // Truncate long values to keep the representation concise
                                var display = s.Length > 100 ? s[..100] + "..." : s;
                                parts.Add($"{prop.Alias}=\"{display}\"");
                            }
                        }
                    }
                    labels.Add(string.Join(" ", parts));
                }
            }

            return string.Join("; ", labels);
        }

        return string.Empty;
    }

    private static string ExtractBlockGrid(IPublishedProperty property)
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

    private static void RejectUnknownParameters(IDictionary<string, object?> arguments)
    {
        var unknown = arguments.Keys.Where(k => !KnownParameters.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolExecutionException(
                $"Unrecognised parameter(s): {string.Join(", ", unknown)}");
        }
    }

    private static int ExtractRequiredIntArgument(IDictionary<string, object?> arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            throw new ToolExecutionException($"Missing required argument: '{name}'");
        }

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
