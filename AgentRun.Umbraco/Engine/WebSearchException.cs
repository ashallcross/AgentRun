namespace AgentRun.Umbraco.Engine;

/// <summary>
/// Base exception for web-search failures. Inherits <see cref="AgentRunException"/>
/// so <c>LlmErrorClassifier</c> does not mask the message with generic
/// "provider error" wording (see project-context.md — AgentRunException
/// bypass invariant). API keys must NEVER appear in <see cref="Exception.Message"/>
/// or any inner exception surfaced through this type.
/// </summary>
public class WebSearchException : AgentRunException
{
    public WebSearchException(string message) : base(message)
    {
    }

    public WebSearchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a provider's API key is missing, empty, whitespace-only, OR
/// rejected by the provider at request time (HTTP 401/403). Both cases are
/// semantically equivalent — the configured key cannot be used to reach the
/// provider. <see cref="WebSearchTool"/> catches this and translates it to a
/// structured <c>{"error":"not_configured"}</c> tool result so the LLM does
/// not retry futilely as it would for a transient transport failure.
/// </summary>
public sealed class WebSearchNotConfiguredException : WebSearchException
{
    public string ProviderName { get; }

    public WebSearchNotConfiguredException(string providerName)
        : base($"web_search is not configured; add an API key under AgentRun:WebSearch:Providers:{providerName}:ApiKey in appsettings.json")
    {
        ProviderName = providerName;
    }

    public WebSearchNotConfiguredException(string providerName, int httpStatusCode)
        : base($"web_search authentication failed: provider {providerName} rejected the API key (HTTP {httpStatusCode}). Check AgentRun:WebSearch:Providers:{providerName}:ApiKey in appsettings.json.")
    {
        ProviderName = providerName;
    }
}

/// <summary>
/// Thrown on HTTP 429 responses from the provider. <see cref="RetryAfterSeconds"/>
/// carries the parsed <c>Retry-After</c> delta-seconds header value, or
/// <c>null</c> when the header is absent or unparseable (only the
/// delta-seconds form is parsed — HTTP-date form is deferred per Story 11.8
/// D8).
/// </summary>
public sealed class WebSearchRateLimitedException : WebSearchException
{
    public string ProviderName { get; }
    public int? RetryAfterSeconds { get; }

    public WebSearchRateLimitedException(string providerName, int? retryAfterSeconds)
        : base(FormatMessage(providerName, retryAfterSeconds))
    {
        ProviderName = providerName;
        RetryAfterSeconds = retryAfterSeconds;
    }

    private static string FormatMessage(string providerName, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is int seconds)
            return $"Search provider {providerName} rate limit exceeded. Retry after {seconds} seconds, or try a different provider.";
        return $"Search provider {providerName} rate limit exceeded. Try a different provider or wait before retrying.";
    }
}
