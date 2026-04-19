using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umbraco.AI.Core.Contexts;

namespace AgentRun.Umbraco.Tools;

internal static class GetAiContextToolHelpers
{
    private static readonly IReadOnlySet<string> KnownParameters =
        new HashSet<string>(StringComparer.Ordinal) { "alias", "content_node_id" };

    public static void RejectUnknownParameters(IDictionary<string, object?> arguments)
    {
        var unknown = arguments.Keys.Where(k => !KnownParameters.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolExecutionException(
                $"Unrecognised parameter(s): {string.Join(", ", unknown)}");
        }
    }

    /// <summary>
    /// Tolerant extraction of Context GUIDs from a <c>Uai.ContextPicker</c>
    /// property value. Handles the shapes plausible at the published-content
    /// boundary depending on whether Umbraco.AI's property-value-converter
    /// is active: <see cref="AIContext"/>, <c>IEnumerable&lt;AIContext&gt;</c>,
    /// <see cref="Guid"/>, <c>Guid[]</c>, <see cref="string"/> (JSON), and
    /// <c>IEnumerable&lt;string&gt;</c>. Malformed shapes log a Warning and
    /// yield zero GUIDs — walk continues to the next ancestor.
    /// </summary>
    public static IEnumerable<Guid> ExtractContextGuids(object? value, ILogger logger, int nodeId)
    {
        if (value is null) yield break;

        switch (value)
        {
            case AIContext ctx:
                yield return ctx.Id;
                yield break;

            case Guid g when g != Guid.Empty:
                yield return g;
                yield break;

            case string s:
                foreach (var guid in ParseGuidsFromString(s, logger, nodeId))
                    yield return guid;
                yield break;

            case IEnumerable enumerable and not string:
                foreach (var item in enumerable)
                {
                    if (item is null) continue;
                    switch (item)
                    {
                        case AIContext ctx:
                            yield return ctx.Id;
                            break;
                        case Guid g when g != Guid.Empty:
                            yield return g;
                            break;
                        case string sItem:
                            foreach (var guid in ParseGuidsFromString(sItem, logger, nodeId))
                                yield return guid;
                            break;
                        default:
                            logger.LogWarning(
                                "get_ai_context: picker on node {ContentNodeId} contained unsupported item type {Type}",
                                nodeId, item.GetType().Name);
                            break;
                    }
                }
                yield break;

            default:
                logger.LogWarning(
                    "get_ai_context: picker on node {ContentNodeId} returned unsupported value type {Type}",
                    nodeId, value.GetType().Name);
                yield break;
        }
    }

    private static IEnumerable<Guid> ParseGuidsFromString(string raw, ILogger logger, int nodeId)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;

        var trimmed = raw.Trim();

        // Single GUID?
        if (Guid.TryParse(trimmed, out var single) && single != Guid.Empty)
        {
            yield return single;
            yield break;
        }

        // JSON array of GUIDs?
        if (trimmed.StartsWith('['))
        {
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "get_ai_context: malformed Uai.ContextPicker JSON value on node {ContentNodeId}",
                    nodeId);
            }

            if (doc is null) yield break;

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    yield break;

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String) continue;
                    var str = element.GetString();
                    if (string.IsNullOrWhiteSpace(str)) continue;
                    if (Guid.TryParse(str, out var g) && g != Guid.Empty)
                        yield return g;
                }
            }

            yield break;
        }

        // JSON object with an id or guid field? (defensive — rare)
        if (trimmed.StartsWith('{'))
        {
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                logger.LogWarning(
                    "get_ai_context: malformed Uai.ContextPicker JSON value on node {ContentNodeId}",
                    nodeId);
            }

            if (doc is null) yield break;

            using (doc)
            {
                foreach (var prop in new[] { "id", "guid", "key" })
                {
                    if (doc.RootElement.TryGetProperty(prop, out var idEl)
                        && idEl.ValueKind == JsonValueKind.String
                        && Guid.TryParse(idEl.GetString(), out var g)
                        && g != Guid.Empty)
                    {
                        yield return g;
                        yield break;
                    }
                }
            }

            yield break;
        }

        logger.LogWarning(
            "get_ai_context: unparseable Uai.ContextPicker string value on node {ContentNodeId}",
            nodeId);
    }
}
