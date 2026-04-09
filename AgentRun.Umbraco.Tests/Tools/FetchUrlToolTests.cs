using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using NSubstitute;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Security;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Tools;

[TestFixture]
public class FetchUrlToolTests
{
    private SsrfProtection _ssrfProtection = null!;
    private IHttpClientFactory _httpClientFactory = null!;
    private FakeToolLimitResolver _resolver = null!;
    private FetchUrlTool _tool = null!;
    private ToolExecutionContext _context = null!;
    private string _instanceRoot = null!;

    [SetUp]
    public void SetUp()
    {
        var policy = Substitute.For<INetworkAccessPolicy>();
        policy.IsAddressAllowed(Arg.Any<IPAddress>()).Returns(true);

        var dnsResolver = Substitute.For<IDnsResolver>();
        dnsResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("93.184.216.34") });

        _ssrfProtection = new SsrfProtection(policy, dnsResolver);
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
        _resolver = new FakeToolLimitResolver();
        _tool = new FetchUrlTool(_ssrfProtection, _httpClientFactory, _resolver);

        _instanceRoot = Path.Combine(Path.GetTempPath(), "agentrun-fetchurl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_instanceRoot);

        var step = new StepDefinition { Id = "step-1", Name = "Test", Agent = "agents/test.md" };
        var workflow = new WorkflowDefinition { Name = "Test Workflow", Alias = "test-workflow", Steps = { step } };
        _context = new ToolExecutionContext(_instanceRoot, "inst-001", "step-1", "test-workflow")
        {
            Step = step,
            Workflow = workflow
        };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_instanceRoot))
            Directory.Delete(_instanceRoot, recursive: true);
    }

    private sealed class FakeToolLimitResolver : IToolLimitResolver
    {
        public int MaxBytes { get; set; } = EngineDefaults.FetchUrlMaxResponseBytes;
        public int TimeoutSeconds { get; set; } = EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => MaxBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => TimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
    }

    private void SetupHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var mockHandler = new MockHttpHandler(handler);
        var client = new HttpClient(mockHandler);
        _httpClientFactory.CreateClient("FetchUrl").Returns(client);
    }

    private sealed record Handle(
        string url,
        int status,
        string content_type,
        long size_bytes,
        string? saved_to,
        bool truncated);

    private static Handle Parse(object result)
    {
        Assert.That(result, Is.InstanceOf<string>());
        var json = (string)result;
        Assert.That(json.Length, Is.LessThan(1024), "handle JSON must be < 1 KB");
        return JsonSerializer.Deserialize<Handle>(json)!;
    }

    // ---------- Task 4 / handle shape, offloading, truncation, no-body, IO failure ----------

    [Test]
    public async Task HandleShape_HasExactlyExpectedFields_AndIsUnder1KB()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello, world!", Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        var json = (string)result;
        Assert.That(json.Length, Is.LessThan(1024));

        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.That(props, Is.EquivalentTo(new[] { "url", "status", "content_type", "size_bytes", "saved_to", "truncated" }));

        var handle = Parse(result);
        Assert.That(handle.url, Is.EqualTo("https://example.com"));
        Assert.That(handle.status, Is.EqualTo(200));
        Assert.That(handle.content_type, Is.EqualTo("text/plain"));
        Assert.That(handle.size_bytes, Is.EqualTo(13));
        Assert.That(handle.truncated, Is.False);
        Assert.That(handle.saved_to, Is.Not.Null);
        Assert.That(Regex.IsMatch(handle.saved_to!, @"^\.fetch-cache/[0-9a-f]{64}\.html$"));
    }

    [Test]
    public async Task SuccessfulFetch_WritesBytesToCacheFile()
    {
        var body = new string('A', 10_000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/page" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(fullPath), Is.True);
        var bytes = await File.ReadAllBytesAsync(fullPath);
        Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(body));
        Assert.That(handle.size_bytes, Is.EqualTo(new FileInfo(fullPath).Length));
    }

    [Test]
    public async Task TruncationCooperation_WritesTruncatedBytes_AndFlagIsTrue()
    {
        _resolver.MaxBytes = 5_000;
        var body = new string('A', 12_000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/big" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.truncated, Is.True);

        var marker = $"\n\n[Response truncated at 5000 bytes]";
        var expectedSize = 5000 + Encoding.UTF8.GetByteCount(marker);
        Assert.That(handle.size_bytes, Is.EqualTo(expectedSize));

        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        var bytes = await File.ReadAllBytesAsync(fullPath);
        Assert.That(bytes.Length, Is.EqualTo(expectedSize));
        Assert.That(Encoding.UTF8.GetString(bytes, 0, 5000), Is.EqualTo(new string('A', 5000)));
        Assert.That(Encoding.UTF8.GetString(bytes), Does.EndWith(marker));
    }

    [Test]
    public async Task NoBody_204_ReturnsHandleWithNullSavedTo_AndWritesNoFile()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/nope" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.status, Is.EqualTo(204));
        Assert.That(handle.size_bytes, Is.EqualTo(0));
        Assert.That(handle.saved_to, Is.Null);
        Assert.That(handle.truncated, Is.False);
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);
    }

    [Test]
    public async Task NoBody_200WithEmptyBody_ReturnsHandleWithNullSavedTo_AndWritesNoFile()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/empty" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.status, Is.EqualTo(200));
        Assert.That(handle.size_bytes, Is.EqualTo(0));
        Assert.That(handle.saved_to, Is.Null);
        Assert.That(handle.truncated, Is.False);
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);
    }

    [Test]
    public async Task Http404_PreservesExistingErrorStringContract_AndWritesNoFile()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = "Not Found"
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/missing" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("HTTP 404: Not Found"));
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);
    }

    [Test]
    public async Task Http500_PreservesExistingErrorStringContract()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Internal Server Error"
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/error" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("HTTP 500: Internal Server Error"));
    }

    [Test]
    public async Task ReachabilityViaReadFile_AfterFetch_ReturnsCachedBytes()
    {
        var body = "<html><body>Hello cache!</body></html>";
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/reach" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        var readFile = new ReadFileTool(_resolver);
        var read = await readFile.ExecuteAsync(
            new Dictionary<string, object?> { ["path"] = handle.saved_to! },
            _context,
            CancellationToken.None);

        Assert.That(read, Is.EqualTo(body));
    }

    [Test]
    public async Task PathSandbox_IsCalledForWriteTarget_FilenameIsHashOfUrl()
    {
        var url = "https://example.com/abc";
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))
            .ToLowerInvariant();

        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("body", Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = url };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.saved_to, Is.EqualTo($".fetch-cache/{expectedHash}.html"));
        var fullPath = Path.Combine(_instanceRoot, ".fetch-cache", $"{expectedHash}.html");
        Assert.That(File.Exists(fullPath), Is.True);
    }

    [Test]
    public void IoFailure_ThrowsToolExecutionException_WithoutInliningBody()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("THIS_BODY_MUST_NOT_LEAK", Encoding.UTF8, "text/html")
        }));

        // Force the write to fail by pre-creating .fetch-cache as a *file* instead of a directory.
        var collisionPath = Path.Combine(_instanceRoot, ".fetch-cache");
        File.WriteAllText(collisionPath, "blocking");

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/io-fail" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("Failed to cache fetch_url response"));
        Assert.That(ex.Message, Does.Contain(".fetch-cache/"));
        Assert.That(ex.Message, Does.Not.Contain("THIS_BODY_MUST_NOT_LEAK"));
    }

    [Test]
    public async Task ConcurrentDirectoryCreation_DoesNotThrow()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("payload", Encoding.UTF8, "text/plain")
        }));

        var args1 = new Dictionary<string, object?> { ["url"] = "https://example.com/a" };
        var args2 = new Dictionary<string, object?> { ["url"] = "https://example.com/b" };

        var t1 = _tool.ExecuteAsync(args1, _context, CancellationToken.None);
        var t2 = _tool.ExecuteAsync(args2, _context, CancellationToken.None);

        await Task.WhenAll(t1, t2);

        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.True);
        var files = Directory.GetFiles(Path.Combine(_instanceRoot, ".fetch-cache"));
        Assert.That(files.Length, Is.EqualTo(2));
    }

    [Test]
    public async Task MissingContentType_HandleHasEmptyContentTypeField()
    {
        var content = "no-content-type-here";
        SetupHttpClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content))
            };
            return Task.FromResult(response);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/noct" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.content_type, Is.EqualTo(""));
        Assert.That(handle.saved_to, Is.Not.Null);
    }

    // ---------- Task 5: regression fixtures (small / medium / large) ----------

    private static byte[] LoadFixture(string name)
    {
        var dir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Tools", "Fixtures");
        var path = Path.Combine(dir, name);
        Assert.That(File.Exists(path), $"Fixture missing: {path}");
        return File.ReadAllBytes(path);
    }

    [TestCase("fetch-url-100kb.html", "https://wearecogworks.com/")]
    [TestCase("fetch-url-500kb.html", "https://umbraco.com/products/cms/")]
    [TestCase("fetch-url-1500kb.html", "https://www.bbc.co.uk/news")]
    public async Task RegressionFixture_FullSize_HandleUnder1KB_AndCachedBytesMatch(string fixture, string url)
    {
        var bytes = LoadFixture(fixture);
        // Allow the full body through (no truncation).
        _resolver.MaxBytes = bytes.Length + 10_000;

        SetupHttpClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        });

        var args = new Dictionary<string, object?> { ["url"] = url };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var json = (string)result;
        Assert.That(json.Length, Is.LessThan(1024));
        var handle = Parse(result);
        Assert.That(handle.truncated, Is.False);
        Assert.That(handle.size_bytes, Is.EqualTo(bytes.Length));

        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        var written = await File.ReadAllBytesAsync(fullPath);
        Assert.That(written, Is.EqualTo(bytes));
    }

    [Test]
    public async Task RegressionFixture_BbcLargest_TruncatedBranchAlsoCovered()
    {
        var bytes = LoadFixture("fetch-url-1500kb.html");
        _resolver.MaxBytes = 100_000; // force truncation

        SetupHttpClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://www.bbc.co.uk/news" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.truncated, Is.True);
        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        var written = await File.ReadAllBytesAsync(fullPath);
        Assert.That(handle.size_bytes, Is.EqualTo(written.LongLength));
        Assert.That(written.Length, Is.LessThan(bytes.Length));
        Assert.That(Encoding.UTF8.GetString(written), Does.EndWith("[Response truncated at 100000 bytes]"));
    }

    // ---------- Existing behavioural guarantees (preserved) ----------

    [Test]
    public void MissingUrlArgument_Throws()
    {
        var args = new Dictionary<string, object?>();

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Missing required argument: 'url'"));
    }

    [Test]
    public void InvalidUrl_Throws()
    {
        var args = new Dictionary<string, object?> { ["url"] = "not-a-url" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Invalid URL"));
    }

    [Test]
    public void SsrfBlockedUrl_Throws()
    {
        var blockingPolicy = Substitute.For<INetworkAccessPolicy>();
        blockingPolicy.IsAddressAllowed(Arg.Any<IPAddress>()).Returns(false);

        var dnsResolver = Substitute.For<IDnsResolver>();
        dnsResolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("10.0.0.1") });

        var blockingSsrf = new SsrfProtection(blockingPolicy, dnsResolver);
        var tool = new FetchUrlTool(blockingSsrf, _httpClientFactory, _resolver);

        var args = new Dictionary<string, object?> { ["url"] = "https://internal.corp" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("resolves to a blocked address"));
    }

    [Test]
    public void Timeout_ThrowsWithTimeoutMessage()
    {
        SetupHttpClient((_, _) =>
            throw new TaskCanceledException("Cancelled"));

        var args = new Dictionary<string, object?> { ["url"] = "https://slow.example.com" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("timed out"));
        Assert.That(ex.Message, Does.Contain($"{EngineDefaults.FetchUrlTimeoutSeconds} seconds"));
    }

    [Test]
    public void Cancellation_RethrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        SetupHttpClient((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com" };

        Assert.That(
            () => _tool.ExecuteAsync(args, _context, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ConnectionFailure_ThrowsToolExecutionException()
    {
        SetupHttpClient((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var args = new Dictionary<string, object?> { ["url"] = "https://down.example.com" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Connection failed"));
    }

    [Test]
    public void Timeout_LinkedCtsActuallyFires_ProducesFriendlyTimeoutMessage()
    {
        _resolver.TimeoutSeconds = 1;
        SetupHttpClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://slow.example.com" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("timed out"));
        Assert.That(ex.Message, Does.Contain("1 seconds"));
    }

    [Test]
    public void NullStepOrWorkflow_Throws_ToolContextMissingException()
    {
        var contextWithoutStep = new ToolExecutionContext(_instanceRoot, "i", "s", "w");

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com" };
        var ex = Assert.ThrowsAsync<ToolContextMissingException>(
            () => _tool.ExecuteAsync(args, contextWithoutStep, CancellationToken.None));
        Assert.That(ex, Is.InstanceOf<AgentRunException>());
    }

    [Test]
    public async Task RealResolver_WorkflowDeclaredMaxResponseBytes_FlowsThroughToTruncation()
    {
        var realResolver = new ToolLimitResolver(Options.Create(new AgentRunOptions()));
        var tool = new FetchUrlTool(_ssrfProtection, _httpClientFactory, realResolver);

        var step = new StepDefinition { Id = "scan", Name = "Scan", Agent = "a.md" };
        var workflow = new WorkflowDefinition
        {
            Name = "End to End",
            Alias = "e2e",
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 25_000 } },
            Steps = { step }
        };
        var context = new ToolExecutionContext(_instanceRoot, "inst", "scan", "e2e")
        {
            Step = step,
            Workflow = workflow
        };

        var largePayload = new string('Y', 40_000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largePayload, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/big" };
        var handle = Parse(await tool.ExecuteAsync(args, context, CancellationToken.None));

        Assert.That(handle.truncated, Is.True);
        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        var written = await File.ReadAllBytesAsync(fullPath);
        Assert.That(Encoding.UTF8.GetString(written), Does.EndWith("[Response truncated at 25000 bytes]"));
    }

    [Test]
    public async Task RealResolver_StepOverride_BeatsWorkflowDefault()
    {
        var realResolver = new ToolLimitResolver(Options.Create(new AgentRunOptions()));
        var tool = new FetchUrlTool(_ssrfProtection, _httpClientFactory, realResolver);

        var step = new StepDefinition
        {
            Id = "tight", Name = "Tight", Agent = "a.md",
            ToolOverrides = new() { FetchUrl = new() { MaxResponseBytes = 10_000 } }
        };
        var workflow = new WorkflowDefinition
        {
            Name = "End to End", Alias = "e2e",
            ToolDefaults = new() { FetchUrl = new() { MaxResponseBytes = 50_000 } },
            Steps = { step }
        };
        var context = new ToolExecutionContext(_instanceRoot, "inst", "tight", "e2e")
        {
            Step = step,
            Workflow = workflow
        };

        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('Z', 30_000), Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/x" };
        var handle = Parse(await tool.ExecuteAsync(args, context, CancellationToken.None));

        Assert.That(handle.truncated, Is.True);
        var fullPath = Path.Combine(_instanceRoot, handle.saved_to!.Replace('/', Path.DirectorySeparatorChar));
        var written = await File.ReadAllBytesAsync(fullPath);
        Assert.That(Encoding.UTF8.GetString(written), Does.EndWith("[Response truncated at 10000 bytes]"));
    }

    // ============================================================================
    // Story 9.1b — extract: "structured" tests
    // ============================================================================

    private sealed record StructuredHandle(
        string url,
        int status,
        string? title,
        string? meta_description,
        StructuredHeadings headings,
        int word_count,
        StructuredImages images,
        StructuredLinks links,
        bool truncated);

    private sealed record StructuredHeadings(List<string> h1, List<string> h2, int h3_h6_count);
    private sealed record StructuredImages(int total, int with_alt, int missing_alt);
    private sealed record StructuredLinks(int @internal, int external);

    private static StructuredHandle ParseStructured(object result)
    {
        Assert.That(result, Is.InstanceOf<string>());
        var json = (string)result;
        return JsonSerializer.Deserialize<StructuredHandle>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private void SetupHttpClientHtml(byte[] body, string contentType = "text/html")
    {
        SetupHttpClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return Task.FromResult(response);
        });
    }

    [Test]
    public async Task Extract_Raw_DefaultBehaviour_PreservesStory97Handle()
    {
        // AC #3: omitted parameter == raw behaviour, byte-for-byte.
        var body = "<html><body>hi</body></html>";
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/raw-default" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.saved_to, Is.Not.Null);
        Assert.That(handle.size_bytes, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
    }

    [Test]
    public async Task Extract_Raw_ExplicitParameter_PreservesStory97Handle()
    {
        // AC #3: extract: "raw" explicit == raw behaviour.
        var body = "<html><body>hi</body></html>";
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/raw-explicit",
            ["extract"] = "raw"
        };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.saved_to, Is.Not.Null);
        // P8 (Story 9.1b code-review fix pass): mirror the byte-count assertion
        // from the default-behaviour test so the two raw-mode regression guards
        // are symmetric.
        Assert.That(handle.size_bytes, Is.EqualTo(Encoding.UTF8.GetByteCount(body)));
    }

    [Test]
    public async Task Extract_Structured_100kb_ProducesLockedShape()
    {
        // AC #4
        var bytes = LoadFixture("fetch-url-100kb.html");
        _resolver.MaxBytes = bytes.Length + 10_000;
        SetupHttpClientHtml(bytes);

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://wearecogworks.com/",
            ["extract"] = "structured"
        };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var json = (string)result;

        // Top-level field check
        using (var doc = JsonDocument.Parse(json))
        {
            var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.That(props, Is.EquivalentTo(new[]
            {
                "url", "status", "title", "meta_description",
                "headings", "word_count", "images", "links",
                "truncated"
            }));
        }

        var h = ParseStructured(result);
        Assert.That(h.url, Is.EqualTo("https://wearecogworks.com/"));
        Assert.That(h.status, Is.EqualTo(200));
        Assert.That(h.headings, Is.Not.Null);
        Assert.That(h.images, Is.Not.Null);
        Assert.That(h.links, Is.Not.Null);
        Assert.That(h.truncated, Is.False);
        Assert.That(h.images.total, Is.EqualTo(h.images.with_alt + h.images.missing_alt));
        // No cache write in structured mode (Q1 = a)
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);
    }

    [Test]
    public async Task Extract_Structured_207kb_PopulatesMetaAndWordCount()
    {
        // AC #5
        var bytes = LoadFixture("fetch-url-500kb.html");
        _resolver.MaxBytes = bytes.Length + 10_000;
        SetupHttpClientHtml(bytes);

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://umbraco.com/products/cms/",
            ["extract"] = "structured"
        };
        var h = ParseStructured(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(h.word_count, Is.GreaterThan(0));
        Assert.That(h.truncated, Is.False);
        // P7 (Story 9.1b code-review fix pass): the test is named
        // PopulatesMetaAndWordCount — actually assert the meta_description
        // field is populated rather than leaving a comment.
        Assert.That(h.meta_description, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task Extract_Structured_1mb_ParsesAtProductionScale()
    {
        // AC #6
        var bytes = LoadFixture("fetch-url-1500kb.html");
        _resolver.MaxBytes = 2_097_152; // CQA workflow default
        SetupHttpClientHtml(bytes);

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://www.bbc.co.uk/news",
            ["extract"] = "structured"
        };
        var h = ParseStructured(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(h.truncated, Is.False);
        Assert.That(h.word_count, Is.GreaterThan(0));
    }

    [Test]
    public async Task Extract_Structured_DeterministicAcrossTwoParses()
    {
        // AC #4: byte-identical determinism
        var bytes = LoadFixture("fetch-url-100kb.html");
        _resolver.MaxBytes = bytes.Length + 10_000;
        SetupHttpClientHtml(bytes);

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://wearecogworks.com/",
            ["extract"] = "structured"
        };
        var first = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var second = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public async Task Extract_Structured_TruncatedDuringParse_FlagSetCorrectly()
    {
        // AC #7
        var bytes = LoadFixture("fetch-url-1500kb.html");
        _resolver.MaxBytes = 65_536;
        SetupHttpClientHtml(bytes);

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://www.bbc.co.uk/news",
            ["extract"] = "structured"
        };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);
        var h = ParseStructured(result);

        Assert.That(h.truncated, Is.True);
        // Marker text MUST NOT have been parsed as HTML content
        Assert.That(result, Does.Not.Contain("Response truncated at"));
        // No cache write in structured mode
        Assert.That(Directory.Exists(Path.Combine(_instanceRoot, ".fetch-cache")), Is.False);
    }

    [Test]
    public async Task Extract_Structured_NotInvokedOn_HttpError()
    {
        // AC #8
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = "Not Found"
        }));

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/missing",
            ["extract"] = "structured"
        };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("HTTP 404: Not Found"));
    }

    [Test]
    public async Task Extract_Structured_NotInvokedOn_EmptyBody()
    {
        // AC #8 + P5 (Story 9.1b code-review fix pass): structured mode on
        // empty body returns the structured shape with empty fields, NOT the
        // raw FetchUrlHandle shape. Schema drift would otherwise break the
        // analyser when it sees raw-shape JSON keys for an empty page.
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        }));

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/empty",
            ["extract"] = "structured"
        };
        var h = ParseStructured(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(h.status, Is.EqualTo(204));
        Assert.That(h.url, Is.EqualTo("https://example.com/empty"));
        Assert.That(h.title, Is.Null);
        Assert.That(h.meta_description, Is.Null);
        Assert.That(h.headings.h1, Is.Empty);
        Assert.That(h.headings.h2, Is.Empty);
        Assert.That(h.headings.h3_h6_count, Is.Zero);
        Assert.That(h.word_count, Is.Zero);
        Assert.That(h.images.total, Is.Zero);
        Assert.That(h.links.@internal, Is.Zero);
        Assert.That(h.links.external, Is.Zero);
        Assert.That(h.truncated, Is.False);
    }

    [Test]
    public void Extract_Structured_NonHtmlContentType_ThrowsToolExecutionException()
    {
        // AC #9
        SetupHttpClientHtml(Encoding.UTF8.GetBytes("%PDF-1.4 fake"), contentType: "application/pdf");

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/file.pdf",
            ["extract"] = "structured"
        };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo(
            "Cannot extract structured fields from content type 'application/pdf'. Use extract: 'raw' instead."));
    }

    [Test]
    public void Extract_InvalidValue_ThrowsToolExecutionException()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("x", Encoding.UTF8, "text/html")
        }));
        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com",
            ["extract"] = "json"
        };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("Invalid extract value: 'json'. Must be 'raw' or 'structured'."));
    }

    [Test]
    public void Extract_NonStringType_ThrowsToolExecutionException()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("x", Encoding.UTF8, "text/html")
        }));
        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com",
            ["extract"] = 42
        };
        Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
    }

    [Test]
    public async Task Extract_Structured_HandcraftedFixture_ImagesAccountingAndLinkClassification()
    {
        // AC #4 invariants — uses a hand-crafted fixture so the assertions are deterministic
        // regardless of which captured fixture changes.
        var html = """
            <!DOCTYPE html>
            <html><head>
              <title>  Hello World  </title>
              <meta name="description" content="A test page">
            </head><body>
              <h1>Top Heading</h1>
              <h1>Second H1</h1>
              <h2>Sub</h2>
              <h3>Sub-sub</h3>
              <h4>4</h4>
              <p>One two three four five.</p>
              <img src="a.png" alt="present">
              <img src="b.png" alt="">
              <img src="c.png">
              <a href="/internal-relative">x</a>
              <a href="https://example.com/internal-abs">x</a>
              <a href="https://other.example.com/external">x</a>
              <a href="mailto:foo@bar.com">x</a>
            </body></html>
            """;
        SetupHttpClientHtml(Encoding.UTF8.GetBytes(html));

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/page",
            ["extract"] = "structured"
        };
        var h = ParseStructured(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(h.title, Is.EqualTo("Hello World"));
        Assert.That(h.meta_description, Is.EqualTo("A test page"));
        Assert.That(h.headings.h1, Is.EqualTo(new[] { "Top Heading", "Second H1" }));
        Assert.That(h.headings.h2, Is.EqualTo(new[] { "Sub" }));
        Assert.That(h.headings.h3_h6_count, Is.EqualTo(2)); // h3 + h4
        Assert.That(h.word_count, Is.GreaterThan(0));

        Assert.That(h.images.total, Is.EqualTo(3));
        Assert.That(h.images.with_alt, Is.EqualTo(2)); // present + empty alt
        Assert.That(h.images.missing_alt, Is.EqualTo(1));

        // /internal-relative + https://example.com/internal-abs == 2 internal
        // https://other.example.com/external + mailto: == 2 external (mailto classified as external)
        Assert.That(h.links.@internal, Is.EqualTo(2));
        Assert.That(h.links.external, Is.EqualTo(2));
    }

    [Test]
    public async Task Extract_Structured_EmptyTitle_NormalisedToNull()
    {
        // Architect-locked: AngleSharp's empty-string title becomes null in the handle.
        var html = "<html><head></head><body><p>x</p></body></html>";
        SetupHttpClientHtml(Encoding.UTF8.GetBytes(html));

        var args = new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/notitle",
            ["extract"] = "structured"
        };
        var h = ParseStructured(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(h.title, Is.Null);
    }

    // ---------- D2 / Locked Decision #11: manual redirect loop with per-hop SSRF ----------

    [Test]
    public void Redirect_ToPrivateIp_RejectedBySsrf()
    {
        // Locked Decision #11: each Location target is re-validated through
        // SsrfProtection.ValidateUrlAsync. A 302 to a link-local / metadata
        // service URL must be rejected, not silently followed.
        var policy = Substitute.For<INetworkAccessPolicy>();
        // The initial example.com host is allowed; the redirect target's IP is not.
        policy.IsAddressAllowed(IPAddress.Parse("93.184.216.34")).Returns(true);
        policy.IsAddressAllowed(IPAddress.Parse("169.254.169.254")).Returns(false);

        var dnsResolver = Substitute.For<IDnsResolver>();
        dnsResolver.ResolveAsync("example.com", Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("93.184.216.34") });
        dnsResolver.ResolveAsync("169.254.169.254", Arg.Any<CancellationToken>())
            .Returns(new[] { IPAddress.Parse("169.254.169.254") });

        var ssrf = new SsrfProtection(policy, dnsResolver);
        var tool = new FetchUrlTool(ssrf, _httpClientFactory, _resolver);

        SetupHttpClient((req, _) =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            resp.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
            return Task.FromResult(resp);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/start" };
        Assert.ThrowsAsync<ToolExecutionException>(
            () => tool.ExecuteAsync(args, _context, CancellationToken.None));
    }

    [Test]
    public void Redirect_ChainExceedsCap_ThrowsTooManyRedirects()
    {
        // Locked Decision #11: 5-redirect cap matches HttpClientHandler.MaxAutomaticRedirections.
        // 6 chained 302s must trip the cap on the 6th hop.
        var hits = 0;
        SetupHttpClient((_, _) =>
        {
            hits++;
            var resp = new HttpResponseMessage(HttpStatusCode.Found);
            // Each hop redirects to a fresh path on the same host so the SSRF
            // re-validation passes (same already-allowed IP).
            resp.Headers.Location = new Uri($"https://example.com/hop-{hits}");
            return Task.FromResult(resp);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/start" };
        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Is.EqualTo("Too many redirects (max 5)"));
        // 6 total HTTP requests = initial + 5 redirects, then the 6th redirect attempt trips the cap.
        // The loop runs hop=0..5 (6 SendAsync calls), and the 6th hop (hop == maxRedirects) throws
        // before resolving the next Location. So the handler is hit 6 times.
        Assert.That(hits, Is.EqualTo(6));
    }

    [Test]
    public async Task Redirect_ToPublicUrl_FollowsAndReturnsBody()
    {
        // Locked Decision #11 negative test: a legitimate cross-host redirect
        // to a different allowed host still works after manual following.
        var hits = 0;
        SetupHttpClient((req, _) =>
        {
            hits++;
            if (hits == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                resp.Headers.Location = new Uri("https://example.com/final");
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello", Encoding.UTF8, "text/html")
            });
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/start" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.size_bytes, Is.EqualTo(5));
        Assert.That(handle.saved_to, Is.Not.Null);
        Assert.That(hits, Is.EqualTo(2));
    }

    [Test]
    public async Task Redirect_RelativeLocation_ResolvesAgainstCurrentRequestUri()
    {
        // Locked Decision #11: relative Location values are resolved against
        // the current request URI per RFC 7231 §7.1.2 — `Location: /new` from
        // https://example.com/old must produce https://example.com/new.
        var seenUris = new List<Uri>();
        var hits = 0;
        SetupHttpClient((req, _) =>
        {
            seenUris.Add(req.RequestUri!);
            hits++;
            if (hits == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Found);
                // Relative Location header — must resolve against current request URI.
                resp.Headers.Location = new Uri("/new", UriKind.Relative);
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok", Encoding.UTF8, "text/html")
            });
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/old" };
        var handle = Parse(await _tool.ExecuteAsync(args, _context, CancellationToken.None));

        Assert.That(handle.saved_to, Is.Not.Null);
        Assert.That(seenUris, Has.Count.EqualTo(2));
        Assert.That(seenUris[0], Is.EqualTo(new Uri("https://example.com/old")));
        Assert.That(seenUris[1], Is.EqualTo(new Uri("https://example.com/new")));
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
