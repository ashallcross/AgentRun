using System;

namespace AgentRun.Umbraco.Engine;

// Stateless exception-to-(errorCode, userMessage) classifier — registered as
// a singleton.
public class StepExecutionFailureHandler : IStepExecutionFailureHandler
{
    public (string ErrorCode, string UserMessage)? Classify(Exception exception) => exception switch
    {
        // Engine-domain exceptions (AgentRunException and its subclasses)
        // carry their own user-facing message and must NOT be reformatted by
        // the LLM provider classifier, which would otherwise mask them as a
        // generic "provider_error".
        ProviderEmptyResponseException empty => ("provider_empty_response", empty.Message),
        StallDetectedException stall => ("stall_detected", stall.Message),
        AgentRunException agentEx => ("step_failed", agentEx.Message),
        _ => LlmErrorClassifier.Classify(exception),
    };
}
