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
        // Characterisation test: binary-search result matches the linear "remove-from-end-until-fits" loop
        // for a representative truncation scenario. Difference of ±1 is tolerated per F1.
        var items = Enumerable.Range(0, 200)
            .Select(i => new { id = i, name = $"item-{i:D3}", description = new string('x', 50) })
            .ToList();

        Func<IList<object>, string> serialiseObj = sub => JsonSerializer.Serialize(sub);
        Func<int, string> markerFor = count => $"[Response truncated — returned {count} of {items.Count} items.]";

        var objectItems = items.Cast<object>().ToList();

        // Pick a limit that forces truncation (well under the full size)
        var fullSize = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(objectItems));
        var limit = fullSize / 3;

        var linearCount = ComputeLinearReferenceCount(objectItems, limit, serialiseObj, markerFor);

        var (bsJson, bsCount) = ContentToolHelpers.TruncateToByteLimit(
            objectItems,
            limit,
            serialiseObj,
            markerFor);

        Assert.That(Math.Abs(bsCount - linearCount), Is.LessThanOrEqualTo(1),
            $"Binary-search count {bsCount} diverged from linear reference {linearCount} by more than 1");
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
