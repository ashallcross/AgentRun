using System.Text;
using System.Text.Json;
using AgentRun.Umbraco.Tools;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class ContentToolHelpersTests
{
    private static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "alias", "parentId"
    };

    [Test]
    public void RejectUnknownParameters_KnownOnly_DoesNotThrow()
    {
        var args = new Dictionary<string, object?> { ["alias"] = "blogPost", ["parentId"] = 1 };

        Assert.DoesNotThrow(() => ContentToolHelpers.RejectUnknownParameters(args, Known));
    }

    [Test]
    public void RejectUnknownParameters_UnknownKey_ThrowsWithKeyName()
    {
        var args = new Dictionary<string, object?> { ["alias"] = "x", ["nope"] = 42 };

        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.RejectUnknownParameters(args, Known));

        Assert.That(ex!.Message, Does.Contain("nope"));
    }

    [Test]
    public void ExtractOptionalStringArgument_CoversMissingWhitespaceAndJsonElement()
    {
        var args = new Dictionary<string, object?>
        {
            ["blank"] = "   ",
            ["plain"] = "hello",
            ["je"] = JsonDocument.Parse("\"world\"").RootElement
        };

        Assert.That(ContentToolHelpers.ExtractOptionalStringArgument(args, "missing"), Is.Null);
        Assert.That(ContentToolHelpers.ExtractOptionalStringArgument(args, "blank"), Is.Null);
        Assert.That(ContentToolHelpers.ExtractOptionalStringArgument(args, "plain"), Is.EqualTo("hello"));
        Assert.That(ContentToolHelpers.ExtractOptionalStringArgument(args, "je"), Is.EqualTo("world"));

        var nonString = new Dictionary<string, object?> { ["x"] = 42 };
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalStringArgument(nonString, "x"));
    }

    [Test]
    public void ExtractRequiredStringArgument_MissingKey_ThrowsWithName()
    {
        var args = new Dictionary<string, object?>();
        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredStringArgument(args, "query"));
        Assert.That(ex!.Message, Does.Contain("query"));
    }

    [Test]
    public void ExtractRequiredStringArgument_PlainStringAndJsonElementAreReturned()
    {
        var args = new Dictionary<string, object?>
        {
            ["plain"] = "hello",
            ["je"] = JsonDocument.Parse("\"world\"").RootElement
        };
        Assert.That(ContentToolHelpers.ExtractRequiredStringArgument(args, "plain"), Is.EqualTo("hello"));
        Assert.That(ContentToolHelpers.ExtractRequiredStringArgument(args, "je"), Is.EqualTo("world"));
    }

    [Test]
    public void ExtractRequiredStringArgument_WrongTypeThrows()
    {
        var args = new Dictionary<string, object?> { ["query"] = 42 };
        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredStringArgument(args, "query"));
        Assert.That(ex!.Message, Does.Contain("query").And.Contain("string"));
    }

    [Test]
    public void ExtractOptionalIntArgument_CoercesAndEnforcesPositiveRange()
    {
        var args = new Dictionary<string, object?>
        {
            ["plainInt"] = 5,
            ["longVal"] = 42L,
            ["jsonNum"] = JsonDocument.Parse("7").RootElement,
            ["jsonStr"] = JsonDocument.Parse("\"13\"").RootElement
        };

        Assert.That(ContentToolHelpers.ExtractOptionalIntArgument(args, "missing"), Is.Null);
        Assert.That(ContentToolHelpers.ExtractOptionalIntArgument(args, "plainInt"), Is.EqualTo(5));
        Assert.That(ContentToolHelpers.ExtractOptionalIntArgument(args, "longVal"), Is.EqualTo(42));
        Assert.That(ContentToolHelpers.ExtractOptionalIntArgument(args, "jsonNum"), Is.EqualTo(7));
        Assert.That(ContentToolHelpers.ExtractOptionalIntArgument(args, "jsonStr"), Is.EqualTo(13));

        var outOfRange = new Dictionary<string, object?> { ["big"] = (long)int.MaxValue + 1 };
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalIntArgument(outOfRange, "big"));

        var negative = new Dictionary<string, object?> { ["neg"] = -1 };
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalIntArgument(negative, "neg"));

        var zero = new Dictionary<string, object?> { ["z"] = 0 };
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalIntArgument(zero, "z"));

        var fractional = new Dictionary<string, object?> { ["frac"] = JsonDocument.Parse("3.5").RootElement };
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalIntArgument(fractional, "frac"));
    }

    [Test]
    public void ExtractRequiredIntArgument_MissingThrows_HappyPathReturns()
    {
        var empty = new Dictionary<string, object?>();
        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredIntArgument(empty, "id"));

        var args = new Dictionary<string, object?> { ["id"] = 42 };
        Assert.That(ContentToolHelpers.ExtractRequiredIntArgument(args, "id"), Is.EqualTo(42));
    }

    [Test]
    public void TruncateToByteLimit_FullFits_ReturnsFullJsonAndFullCount()
    {
        var items = Enumerable.Range(0, 5).Select(i => new { id = i }).ToList();
        var limit = 10_000;

        var (json, count) = ContentToolHelpers.TruncateToByteLimit(
            items,
            limit,
            sub => JsonSerializer.Serialize(sub),
            _ => "[truncated]");

        Assert.That(count, Is.EqualTo(5));
        Assert.That(json, Does.Not.Contain("[truncated]"));
    }

    [Test]
    public void TruncateToByteLimit_BinarySearchMatchesLinearReference()
    {
        // Characterisation test: under the helper's monotonicity invariant (markerFor length
        // non-decreasing in count) the binary-search prefix MUST equal the linear reference
        // exactly — any drift would indicate an off-by-one in the search bounds.
        var items = Enumerable.Range(0, 200)
            .Select(i => new { id = i, name = $"item-{i:D3}", description = new string('x', 50) })
            .ToList();

        Func<IList<object>, string> serialiseObj = sub => JsonSerializer.Serialize(sub);
        Func<int, string> markerFor = count => $"[Response truncated — returned {count} of {items.Count} items.]";

        var objectItems = items.Cast<object>().ToList();

        var fullSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(objectItems));
        var limit = fullSize / 3;

        var linearCount = ComputeLinearReferenceCount(objectItems, limit, serialiseObj, markerFor);

        // Guard against a vacuous pass: the chosen limit must actually exercise truncation.
        Assert.That(linearCount, Is.GreaterThan(0).And.LessThan(objectItems.Count),
            $"Test input must force a non-trivial truncation (linearCount={linearCount})");

        var (bsJson, bsCount) = ContentToolHelpers.TruncateToByteLimit(
            objectItems,
            limit,
            serialiseObj,
            markerFor);

        Assert.That(bsCount, Is.EqualTo(linearCount),
            $"Binary-search count {bsCount} diverged from linear reference {linearCount} under monotone marker — off-by-one in search bounds");
        Assert.That(Encoding.UTF8.GetByteCount(bsJson), Is.LessThanOrEqualTo(limit),
            "Binary-search result must fit within the byte limit");
        Assert.That(bsJson, Does.Contain($"returned {bsCount} of {items.Count}"),
            "Truncated payload must include the truncation marker with the included count");
    }

    [Test]
    public void TruncateToByteLimit_LimitSmallerThanEmptyMarker_ReturnsZeroCountWithMarker()
    {
        var items = Enumerable.Range(0, 3).Select(i => new { id = i }).ToList();

        var (json, count) = ContentToolHelpers.TruncateToByteLimit(
            items,
            limitBytes: 1,
            sub => JsonSerializer.Serialize(sub),
            markerFor: c => $"[truncated — {c} of {items.Count}]");

        Assert.That(count, Is.EqualTo(0));
        Assert.That(json, Does.Contain("[truncated — 0 of 3]"));
    }

    [Test]
    public void TruncateToByteLimit_EmptyList_ReturnsEmptyJsonAndZero()
    {
        var items = new List<object>();

        var (json, count) = ContentToolHelpers.TruncateToByteLimit(
            items,
            limitBytes: 10_000,
            sub => JsonSerializer.Serialize(sub),
            _ => "[truncated]");

        Assert.That(count, Is.EqualTo(0));
        Assert.That(json, Is.EqualTo("[]"));
        Assert.That(json, Does.Not.Contain("[truncated]"));
    }

    [Test]
    public void TruncateToByteLimit_SingleItemOverLimit_ReturnsMarkerOnlyWithZeroCount()
    {
        var items = new List<object> { new { id = 0, payload = new string('x', 500) } };

        var (json, count) = ContentToolHelpers.TruncateToByteLimit(
            items,
            limitBytes: 50,
            sub => JsonSerializer.Serialize(sub),
            markerFor: c => $"[truncated — {c} of 1]");

        Assert.That(count, Is.EqualTo(0));
        Assert.That(json, Does.Contain("[truncated — 0 of 1]"));
    }

    // ---------- Node-ref extractor: GUID-first retrofit ----------

    [Test]
    public void ExtractRequiredNodeRefArgument_IntegerInput_ReturnsIdNullKey()
    {
        var args = new Dictionary<string, object?> { ["id"] = 1124 };

        var (id, key) = ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id");

        Assert.That(id, Is.EqualTo(1124));
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractRequiredNodeRefArgument_GuidStringInput_ReturnsKeyNullId()
    {
        var guid = Guid.NewGuid();
        var args = new Dictionary<string, object?> { ["id"] = guid.ToString("D") };

        var (id, key) = ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.EqualTo(guid));
    }

    [Test]
    public void ExtractRequiredNodeRefArgument_StringIntegerInput_ResolvesAsInt()
    {
        // Callers that pass integer-as-string should take the int path rather
        // than falling through to the GUID parser error.
        var args = new Dictionary<string, object?> { ["id"] = "1124" };

        var (id, key) = ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id");

        Assert.That(id, Is.EqualTo(1124));
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractRequiredNodeRefArgument_JsonElementInteger_Works()
    {
        var doc = JsonDocument.Parse("""{"id": 1124}""");
        var args = new Dictionary<string, object?> { ["id"] = doc.RootElement.GetProperty("id") };

        var (id, key) = ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id");

        Assert.That(id, Is.EqualTo(1124));
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractRequiredNodeRefArgument_JsonElementGuidString_Works()
    {
        var guid = Guid.NewGuid();
        var doc = JsonDocument.Parse($$"""{"id": "{{guid:D}}"}""");
        var args = new Dictionary<string, object?> { ["id"] = doc.RootElement.GetProperty("id") };

        var (id, key) = ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.EqualTo(guid));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ExtractRequiredNodeRefArgument_NonPositiveInt_Throws(int bad)
    {
        var args = new Dictionary<string, object?> { ["id"] = bad };

        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id"));
        Assert.That(ex!.Message, Does.Contain("positive integer or a GUID string"));
    }

    [TestCase("not-a-guid")]
    [TestCase("12.34")]
    [TestCase("00000000-0000-0000-0000-000000000000")] // Guid.Empty rejected
    public void ExtractRequiredNodeRefArgument_MalformedString_Throws(string bad)
    {
        var args = new Dictionary<string, object?> { ["id"] = bad };

        Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id"));
    }

    [Test]
    public void ExtractRequiredNodeRefArgument_Missing_Throws()
    {
        var args = new Dictionary<string, object?>();

        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractRequiredNodeRefArgument(args, "id"));
        Assert.That(ex!.Message, Does.Contain("Missing required argument"));
    }

    [Test]
    public void ExtractOptionalNodeRefArgument_Missing_ReturnsNullPair()
    {
        var args = new Dictionary<string, object?>();

        var (id, key) = ContentToolHelpers.ExtractOptionalNodeRefArgument(args, "parentId");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractOptionalNodeRefArgument_GuidString_ReturnsKey()
    {
        var guid = Guid.NewGuid();
        var args = new Dictionary<string, object?> { ["parentId"] = guid.ToString("D") };

        var (id, key) = ContentToolHelpers.ExtractOptionalNodeRefArgument(args, "parentId");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.EqualTo(guid));
    }

    // ---------- Post-2026-04-22 code review patches ----------

    [Test]
    public void ExtractOptionalNodeRefArgument_WhitespaceOnlyString_ReturnsNullPair()
    {
        // Regression for Edge Case Hunter #15: LLMs occasionally emit " " as a
        // filler for "no value". Other optional helpers (ExtractOptionalStringArgument)
        // treat whitespace as null; the node-ref optional helper now does the same.
        var args = new Dictionary<string, object?> { ["parentId"] = "   " };

        var (id, key) = ContentToolHelpers.ExtractOptionalNodeRefArgument(args, "parentId");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractOptionalNodeRefArgument_WhitespaceOnlyJsonString_ReturnsNullPair()
    {
        // Same rule for JsonElement-wrapped whitespace-only strings.
        using var doc = System.Text.Json.JsonDocument.Parse("\"   \"");
        var args = new Dictionary<string, object?> { ["parentId"] = doc.RootElement.Clone() };

        var (id, key) = ContentToolHelpers.ExtractOptionalNodeRefArgument(args, "parentId");

        Assert.That(id, Is.Null);
        Assert.That(key, Is.Null);
    }

    [Test]
    public void ExtractOptionalNodeRefArgument_MalformedNonWhitespaceString_StillThrows()
    {
        // Non-whitespace malformed strings must still surface loudly so bad
        // references aren't silently dropped. Distinct from the whitespace case.
        var args = new Dictionary<string, object?> { ["parentId"] = "not-a-ref" };

        var ex = Assert.Throws<ToolExecutionException>(
            () => ContentToolHelpers.ExtractOptionalNodeRefArgument(args, "parentId"));
        Assert.That(ex!.Message, Does.Contain("positive integer"));
    }

    private static int ComputeLinearReferenceCount<T>(
        IList<T> items,
        int limitBytes,
        Func<IList<T>, string> serialise,
        Func<int, string> markerFor)
    {
        // Reference implementation mirroring the pre-10.7c remove-from-end loop:
        // start with full list; if it fits, keep all. Otherwise peel one-by-one from the tail
        // until the serialised subset + marker fits under the limit.
        if (Encoding.UTF8.GetByteCount(serialise(items)) <= limitBytes)
        {
            return items.Count;
        }

        var remaining = new List<T>(items);
        while (remaining.Count > 0)
        {
            remaining.RemoveAt(remaining.Count - 1);
            var candidate = serialise(remaining) + markerFor(remaining.Count);
            if (Encoding.UTF8.GetByteCount(candidate) <= limitBytes)
            {
                return remaining.Count;
            }
        }

        return 0;
    }
}
