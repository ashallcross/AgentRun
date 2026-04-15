using System;

namespace AgentRun.Umbraco.Engine;

public interface IStepExecutionFailureHandler
{
    // Classifies a step-execution exception into the (code, message) tuple
    // assigned to StepExecutionContext.LlmError. Nullable return follows
    // LlmErrorClassifier's convention — null means "user cancellation, do
    // not record an error" (StepExecutor's OperationCanceledException filter
    // guarantees this path is unreachable in practice, but the shape matches).
    //
    // Load-bearing invariant (F5 / memory): engine-domain exceptions
    // (AgentRunException + subclasses such as StallDetectedException,
    // ProviderEmptyResponseException) carry their own user-facing message
    // and MUST bypass LlmErrorClassifier entirely, otherwise the provider
    // classifier masks them as a generic "provider_error" and the user
    // sees meaningless copy. The call sites in StepExecutionFailureHandlerTests
    // codify this invariant.
    (string ErrorCode, string UserMessage)? Classify(Exception exception);
}
