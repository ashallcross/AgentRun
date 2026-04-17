using Microsoft.Extensions.AI;

namespace AgentRun.Umbraco.Engine;

public class AgentRunException : Exception
{
    // Story 11.5 — partial UsageDetails observed before the engine-domain
    // throw, so StepExecutor's catch can log non-zero cache.usage for
    // stall/max-iteration/empty-response failures instead of silent zeros.
    // Settable (not init-only) so ToolLoop can attach usage via catch-rethrow
    // when the exception originates in a collaborator (e.g. StallRecoveryPolicy).
    public UsageDetails? PartialUsage { get; set; }

    public AgentRunException(string message) : base(message)
    {
    }

    public AgentRunException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
