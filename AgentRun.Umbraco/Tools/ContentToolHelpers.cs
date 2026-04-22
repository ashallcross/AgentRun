using System.Text;
using System.Text.Json;

namespace AgentRun.Umbraco.Tools;

public static class ContentToolHelpers
{
    public static void RejectUnknownParameters(
        IDictionary<string, object?> arguments,
        IReadOnlySet<string> knownParameters)
    {
        var unknown = arguments.Keys.Where(k => !knownParameters.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new ToolExecutionException(
                $"Unrecognised parameter(s): {string.Join(", ", unknown)}");
        }
    }

    public static string? ExtractOptionalStringArgument(
        IDictionary<string, object?> arguments,
        string name)
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

    public static string ExtractRequiredStringArgument(
        IDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            throw new ToolExecutionException($"Missing required argument: '{name}'");
        }

        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString() ?? string.Empty,
            _ => throw new ToolExecutionException($"Argument '{name}' must be a string")
        };
    }

    public static int? ExtractOptionalIntArgument(
        IDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        return CoerceToPositiveInt(value, name);
    }

    public static int ExtractRequiredIntArgument(
        IDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            throw new ToolExecutionException($"Missing required argument: '{name}'");
        }

        return CoerceToPositiveInt(value, name);
    }

    /// <summary>
    /// Extract a node reference argument that accepts EITHER a positive integer (legacy
    /// tree ID — display-only, not durable across environments) OR a GUID string (Umbraco
    /// Key — durable, cross-environment-stable). Returns exactly one of <paramref name="id"/>
    /// or <paramref name="key"/> populated; the other is <see langword="null"/>. Throws
    /// <see cref="ToolExecutionException"/> on missing / empty / malformed input.
    /// </summary>
    /// <remarks>
    /// Mirror of <c>GetAiContextTool.TryExtractContentNodeRef</c> but as a throwing helper
    /// that matches the <c>ExtractRequiredIntArgument</c> contract used by the other
    /// content tools. Captured under memory <c>feedback_guid_over_integer_id.md</c> — the
    /// going-forward rule is that node references in tool contracts must accept GUIDs.
    /// </remarks>
    public static (int? Id, Guid? Key) ExtractRequiredNodeRefArgument(
        IDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            throw new ToolExecutionException($"Missing required argument: '{name}'");
        }

        return CoerceToNodeRef(value, name, required: true)
               ?? throw new ToolExecutionException($"Missing required argument: '{name}'");
    }

    /// <summary>
    /// Optional-variant of <see cref="ExtractRequiredNodeRefArgument"/>. Returns
    /// <c>(null, null)</c> when the argument is missing, null, or a whitespace-only
    /// string (LLMs occasionally emit `" "` as a filler for "no value"). Otherwise
    /// exactly one of <c>Id</c> / <c>Key</c> is populated. Throws on non-whitespace
    /// malformed input (e.g. `"0"`, `"abc"`) so bad references surface loudly.
    /// </summary>
    public static (int? Id, Guid? Key) ExtractOptionalNodeRefArgument(
        IDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return (null, null);
        }

        // Whitespace-only strings and whitespace-only JsonElement strings are
        // treated as "absent" to match ExtractOptionalStringArgument's semantics.
        if (value is string s && string.IsNullOrWhiteSpace(s))
        {
            return (null, null);
        }
        if (value is JsonElement je
            && je.ValueKind == JsonValueKind.String
            && string.IsNullOrWhiteSpace(je.GetString()))
        {
            return (null, null);
        }

        return CoerceToNodeRef(value, name, required: false)
               ?? (null, null);
    }

    private static (int? Id, Guid? Key)? CoerceToNodeRef(object value, string name, bool required)
    {
        // Unwrap JsonElement to native value so one parsing path covers CLR and JSON inputs.
        object? unwrapped = value;
        if (value is JsonElement je)
        {
            unwrapped = je.ValueKind switch
            {
                JsonValueKind.Number => je.TryGetInt64(out var n) ? (object)n : null,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                _ => null
            };
            if (unwrapped is null)
            {
                if (!required) return (null, null);
                throw new ToolExecutionException(
                    $"Argument '{name}' must be a positive integer or a GUID string");
            }
        }

        // Integer path — accepts int, long (range-checked), double-whole, or numeric string.
        // String-numeric inputs (e.g. "1124") parse as int BEFORE attempting GUID parse so
        // callers that want to pass integers as strings aren't forced to the GUID error path.
        int? parsedInt = unwrapped switch
        {
            int i => i,
            long l when l is <= int.MaxValue and >= int.MinValue => (int)l,
            double d when !double.IsNaN(d) && !double.IsInfinity(d)
                           && d <= int.MaxValue && d >= int.MinValue
                           && Math.Abs(d - Math.Truncate(d)) < double.Epsilon => (int)d,
            string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                                       System.Globalization.CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };

        if (parsedInt is not null)
        {
            if (parsedInt <= 0)
            {
                throw new ToolExecutionException(
                    $"Argument '{name}' must be a positive integer or a GUID string");
            }
            return (parsedInt, null);
        }

        // GUID path — string that parses as Guid (D format and variants accepted).
        if (unwrapped is string str && !string.IsNullOrWhiteSpace(str))
        {
            if (Guid.TryParse(str, out var key) && key != Guid.Empty)
            {
                return (null, key);
            }
        }

        throw new ToolExecutionException(
            $"Argument '{name}' must be a positive integer or a GUID string");
    }

    // Binary-search truncation: find the largest prefix of `items` whose serialised JSON
    // plus marker fits within `limitBytes`. Replaces O(n²) "remove-one-re-serialise" loops.
    // `serialise` owns the outer shape so GetContentTool can wrap the subset in a
    // result object while the List* tools serialise the list directly.
    // Returns (Json, IncludedCount); IncludedCount < items.Count signals truncation.
    //
    // Monotonicity invariant: the fit predicate
    //     utf8Bytes(serialise(items.Take(n)) + markerFor(n)) <= limitBytes
    // MUST be monotone-non-increasing in n (i.e. if prefix-of-N+1 fits, so does prefix-of-N).
    // All three current callers satisfy this because `serialise(prefix)` grows with n and
    // `markerFor(n)` is `"… {n} of {total} …"` whose length is non-decreasing in n.
    // A caller whose marker length shrinks as n grows (e.g. "… {total - n} omitted …") can
    // break the search and silently return a prefix that overruns limitBytes.
    //
    // When even `serialise(empty) + markerFor(0)` exceeds limitBytes the method returns that
    // minimal payload rather than throwing — callers accept it as a best-effort "over budget,
    // nothing truncatable fit" result.
    public static (string Json, int IncludedCount) TruncateToByteLimit<T>(
        IList<T> items,
        int limitBytes,
        Func<IList<T>, string> serialise,
        Func<int, string> markerFor)
    {
        var fullJson = serialise(items);
        if (Encoding.UTF8.GetByteCount(fullJson) <= limitBytes)
        {
            return (fullJson, items.Count);
        }

        int lo = 0;
        int hi = items.Count;
        var bestJson = serialise(Array.Empty<T>()) + markerFor(0);
        var bestCount = 0;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var prefix = mid == items.Count ? items : items.Take(mid).ToList();
            var candidate = serialise(prefix) + markerFor(mid);

            if (Encoding.UTF8.GetByteCount(candidate) <= limitBytes)
            {
                bestJson = candidate;
                bestCount = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (bestJson, bestCount);
    }

    private static int CoerceToPositiveInt(object value, string name)
    {
        var intValue = value switch
        {
            int i => i,
            long l when l is > int.MaxValue or < int.MinValue
                => throw new ToolExecutionException($"Argument '{name}' is out of range"),
            long l => (int)l,
            double d when double.IsNaN(d) || double.IsInfinity(d) || d > int.MaxValue || d < int.MinValue
                => throw new ToolExecutionException($"Argument '{name}' is out of range"),
            double d => (int)d,
            JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt32(out var n) => n,
            JsonElement { ValueKind: JsonValueKind.Number }
                => throw new ToolExecutionException($"Argument '{name}' must be an integer"),
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
