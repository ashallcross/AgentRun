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
