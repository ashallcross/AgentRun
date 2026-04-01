using System.Net;
using System.Text;
using NSubstitute;
using Shallai.UmbracoAgentRunner.Security;
using Shallai.UmbracoAgentRunner.Tools;

namespace Shallai.UmbracoAgentRunner.Tests.Tools;

[TestFixture]
public class FetchUrlToolTests
{
    private SsrfProtection _ssrfProtection = null!;
    private IHttpClientFactory _httpClientFactory = null!;
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
        _tool = new FetchUrlTool(_ssrfProtection, _httpClientFactory);
        _context = new ToolExecutionContext("/tmp/test", "inst-001", "step-1", "test-workflow");
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
    public async Task ResponseExceedingJsonLimit_IsTruncated()
    {
        var largeContent = new string('A', 204_801); // 200KB + 1 byte
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeContent, Encoding.UTF8, "application/json")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/data.json" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 200KB]"));
        // 200KB of content + truncation message — original body is cut to exactly 204,800 chars
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
        var tool = new FetchUrlTool(blockingSsrf, _httpClientFactory);

        var args = new Dictionary<string, object?> { ["url"] = "https://internal.corp" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("resolves to a blocked address"));
    }

    [Test]
    public void Timeout_ThrowsWithTimeoutMessage()
    {
        SetupHttpClient((_, _) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var args = new Dictionary<string, object?> { ["url"] = "https://slow.example.com" };

        var ex = Assert.ThrowsAsync<ToolExecutionException>(
            () => _tool.ExecuteAsync(args, _context, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("timed out"));
    }

    [Test]
    public async Task HtmlResponse_ExceedingHtmlLimit_IsTruncated()
    {
        var largeHtml = new string('H', 102_401); // 100KB + 1 byte
        SetupHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(largeHtml, Encoding.UTF8, "text/html")
        }));

        var args = new Dictionary<string, object?> { ["url"] = "https://example.com/page.html" };
        var result = (string)await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        Assert.That(result, Does.EndWith("[Response truncated at 100KB]"));
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
    public async Task MissingContentType_DefaultsTo200KBLimit()
    {
        var content = new string('X', 204_801); // 200KB + 1 byte
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

        Assert.That(result, Does.EndWith("[Response truncated at 200KB]"));
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
