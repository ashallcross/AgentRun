using System.Net;
using System.Text;
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

        var step = new StepDefinition { Id = "step-1", Name = "Test", Agent = "agents/test.md" };
        var workflow = new WorkflowDefinition { Name = "Test Workflow", Alias = "test-workflow", Steps = { step } };
        _context = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow")
        {
            Step = step,
            Workflow = workflow
        };
    }

    private sealed class FakeToolLimitResolver : IToolLimitResolver
    {
        public int MaxBytes { get; set; } = EngineDefaults.FetchUrlMaxResponseBytes;
        public int TimeoutSeconds { get; set; } = EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => MaxBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => TimeoutSeconds;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 300;
    }

    private void SetupHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        var mockHandler = new MockHttpHandler(handler);
        var client = new HttpClient(mockHandler);
        _httpClientFactory.CreateClient("FetchUrl").Returns(client);
    }

    [Test]
    public async Task SuccessfulFetch_ReturnsResponseBody()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Hello, world!", Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("Hello, world!"));
    }

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
    public async Task Http404_ReturnsErrorString()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            ReasonPhrase = "Not Found"
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/missing" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo("HTTP 404: Not Found"));
    }

    [Test]
    public async Task Http500_ReturnsErrorString()
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
    public async Task ResponseExceedingResolvedLimit_IsTruncated()
    {
        _resolver.MaxBytes = 50_000;
        var largeContent = new string('A', 60_000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeContent, Encoding.UTF8, "application/json")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/data.json" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 50000 bytes]"));
        Assert.That(result, Does.StartWith(new string('A', 100)));
    }

    [Test]
    public async Task ResponseWithinLimit_ReturnsFull()
    {
        var content = new string('B', 1000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/small.json" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo(content));
    }

    [Test]
    public void SsrfBlockedUrl_Throws()
    {
        // Create a protection that blocks everything
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
        // Story 9.6: timeout is applied per-request via a linked CTS, not HttpClient.Timeout.
        SetupHttpClient((_, _) =>
            throw new TaskCanceledException("Cancelled"));

        var args = new Dictionary<string, object?> { ["url"] = "https://slow.example.com" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("timed out"));
        Assert.That(ex.Message, Does.Contain($"{EngineDefaults.FetchUrlTimeoutSeconds} seconds"));
    }

    [Test]
    public async Task HtmlResponse_UsesSameLimit_AsAllOtherContentTypes()
    {
        // Story 9.6: the HTML/JSON content-type-aware split is collapsed.
        // A single max_response_bytes applies regardless of media type.
        _resolver.MaxBytes = 80_000;
        var largeHtml = new string('H', 90_000);
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeHtml, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/page.html" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 80000 bytes]"));
    }

    [Test]
    public async Task EmptyResponseBody_ReturnsEmptyString()
    {
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/empty" };
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Is.EqualTo(string.Empty));
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

        // HttpClient wraps OCE in TaskCanceledException — verify it propagates (not wrapped in ToolExecutionException)
        Assert.That(
            () => _tool.ExecuteAsync(args, _context, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task MissingContentType_StillUsesResolvedLimit()
    {
        _resolver.MaxBytes = 30_000;
        var content = new string('X', 40_000);
        SetupHttpClient((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(content))
            };
            // No Content-Type header set
            return Task.FromResult(response);
        });

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/noct" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 30000 bytes]"));
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
        // P7 / AC #6: prove the per-request CancellationTokenSource.CancelAfter
        // really fires and the OCE is caught into a friendly ToolExecutionException.
        // The handler awaits Task.Delay on the cancellation token — it does NOT
        // throw TaskCanceledException synchronously like the older test stub.
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
    public void NullStepOrWorkflow_Throws_InvalidOperationException()
    {
        // D2: missing step/workflow context is a wiring bug — fail loud.
        var contextWithoutStep = new ToolExecutionContext("/tmp", "i", "s", "w");

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com" };
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _tool.ExecuteAsync(args, contextWithoutStep, CancellationToken.None));
    }

    [Test]
    public async Task RealResolver_WorkflowDeclaredMaxResponseBytes_FlowsThroughToTruncation()
    {
        // P8 / AC #6 integration: a workflow-declared tool_defaults value must
        // flow end-to-end through the real ToolLimitResolver into FetchUrlTool's
        // truncation logic — not via the test FakeToolLimitResolver shortcut.
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
        var context = new ToolExecutionContext("/tmp", "inst", "scan", "e2e")
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
        var result = (string)await tool.ExecuteAsync(args, context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 25000 bytes]"));
    }

    [Test]
    public async Task RealResolver_StepOverride_BeatsWorkflowDefault()
    {
        // P8 (b): step-level override flows end-to-end via the real resolver.
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
        var context = new ToolExecutionContext("/tmp", "inst", "tight", "e2e")
        {
            Step = step,
            Workflow = workflow
        };

        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('Z', 30_000), Encoding.UTF8, "text/plain")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/x" };
        var result = (string)await tool.ExecuteAsync(args, context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 10000 bytes]"));
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
