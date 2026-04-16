using System;

namespace AgentRun.Umbraco.Engine;

public interface IStepExecutionFailureHandler
{
    // Classifies a step-execution exception into the (code, message) tuple
    // assigned to StepExecutionContext.LlmError. Engine-domain exceptions
    // always return a non-null tuple; other exceptions delegate to
    // LlmErrorClassifier whose own contract allows a nullable return.
    //
    // Load-bearing invariant (F5 / memory): engine-domain exceptions
    // (AgentRunException + subclasses such as StallDetectedException,
    // ProviderEmptyResponseException) carry their own user-facing message
    // and MUST bypass LlmErrorClassifier entirely, otherwise the provider
    // classifier masks them as a generic "provider_error" and the user
    // sees meaningless copy. StepExecutionFailureHandlerTests codifies this.
    (string ErrorCode, string UserMessage)? Classify(Exception exception);
}
