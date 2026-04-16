using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Web.Common.Authorization;
using AgentRun.Umbraco.Configuration;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Engine.Events;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Models.ApiModels;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Endpoints;

[ApiController]
[Route("umbraco/api/agentrun")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public class ExecutionEndpoints : ControllerBase
{
    // SSE keepalive interval clamp bounds. Exposed so tests can assert the
    // contract directly against ClampInterval.
    internal static readonly TimeSpan KeepaliveMin = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan KeepaliveMax = TimeSpan.FromMinutes(5);

    // Process-wide gate so a misconfigured KeepaliveInterval only logs the
    // clamp warning once per process lifetime instead of per SSE request.
    private static int _keepaliveClampWarned;

    private readonly IInstanceManager _instanceManager;
    private readonly IProfileResolver _profileResolver;
    private readonly IWorkflowOrchestrator _workflowOrchestrator;
    private readonly IWorkflowRegistry _workflowRegistry;
    private readonly IConversationStore _conversationStore;
    private readonly IActiveInstanceRegistry _activeInstanceRegistry;
    private readonly AgentRunOptions _options;
    private readonly ILogger<ExecutionEndpoints> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ExecutionEndpoints(
        IInstanceManager instanceManager,
        IProfileResolver profileResolver,
        IWorkflowOrchestrator workflowOrchestrator,
        IWorkflowRegistry workflowRegistry,
        IConversationStore conversationStore,
        IActiveInstanceRegistry activeInstanceRegistry,
        IOptions<AgentRunOptions> options,
        ILogger<ExecutionEndpoints> logger,
        ILoggerFactory loggerFactory)
    {
        _instanceManager = instanceManager;
        _profileResolver = profileResolver;
        _workflowOrchestrator = workflowOrchestrator;
        _workflowRegistry = workflowRegistry;
        _conversationStore = conversationStore;
        _activeInstanceRegistry = activeInstanceRegistry;
        _options = options.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Clamps the configured SSE keepalive interval into the supported range
    /// [<see cref="KeepaliveMin"/>, <see cref="KeepaliveMax"/>]. Values outside
    /// this range are clamped with a warn log; startup is never failed for
    /// bad config (locked decision 12).
    /// </summary>
    internal static TimeSpan ClampInterval(TimeSpan raw)
    {
        if (raw < KeepaliveMin) return KeepaliveMin;
        if (raw > KeepaliveMax) return KeepaliveMax;
        return raw;
    }

    [HttpPost("instances/{id}/start")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> StartInstance(string id, CancellationToken cancellationToken)
    {
        var instance = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (instance is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        // Reject completed/failed/cancelled/interrupted (Interrupted uses Retry, not Start — Story 10.9)
        if (instance.Status is InstanceStatus.Completed or InstanceStatus.Failed or InstanceStatus.Cancelled or InstanceStatus.Interrupted)
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_status",
                Message = $"Cannot start instance with status '{instance.Status}'."
            });
        }

        // Reject already running (concurrent execution guard) — but allow restart
        // for interactive mode between steps when not actively executing
        if (instance.Status == InstanceStatus.Running)
        {
            if (_activeInstanceRegistry.GetMessageWriter(id) is not null)
            {
                return Conflict(new ErrorResponse
                {
                    Error = "already_running",
                    Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
                });
            }
            // Not actively executing — allow restart (interactive mode between steps)
        }

        // Provider prerequisite check — resolve workflow definition for profile fallback
        var registered = _workflowRegistry.GetWorkflow(instance.WorkflowAlias);
        if (!await _profileResolver.HasConfiguredProviderAsync(registered?.Definition, cancellationToken))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "no_provider",
                Message = "Configure an AI provider in Umbraco.AI before workflows can run."
            });
        }

        // Atomic orchestrator-slot claim. The non-atomic status + writer-presence
        // check above is a fast-path UX hint; TryClaim is the source-of-truth
        // race gate. If a second /start arrives after our status check passed
        // but before this TryClaim runs, exactly one caller wins — the loser
        // returns 409.
        if (!_activeInstanceRegistry.TryClaim(id))
        {
            return Conflict(new ErrorResponse
            {
                Error = "already_running",
                Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
            });
        }

        try
        {
            // Set instance to Running (skip if already Running — interactive mode between steps)
            if (instance.Status != InstanceStatus.Running)
            {
                try
                {
                    await _instanceManager.SetInstanceStatusAsync(
                        instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Running, cancellationToken);
                }
                catch (InstanceAlreadyRunningException)
                {
                    // The in-manager guard fired despite our TryClaim winning,
                    // which is only possible if a concurrent retry mutated the
                    // persisted status to Running between our pre-claim status
                    // read and now. Release the claim and 409.
                    _activeInstanceRegistry.UnregisterInstance(id);
                    return Conflict(new ErrorResponse
                    {
                        Error = "already_running",
                        Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
                    });
                }
            }

            return await ExecuteSseAsync(instance, cancellationToken);
        }
        catch
        {
            // Anything thrown before the orchestrator's lifecycle takes over must
            // release the claim. Once ExecuteSseAsync enters the orchestrator's
            // try/finally, the orchestrator's UnregisterInstance handles release
            // on all exit paths (happy, OCE, error). A second UnregisterInstance
            // call from here is a safe no-op (TryRemove returns false).
            _activeInstanceRegistry.UnregisterInstance(id);
            throw;
        }
    }

    [HttpPost("instances/{id}/retry")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(404)]
    [ProducesResponseType<ErrorResponse>(409)]
    public async Task<IActionResult> RetryInstance(string id, CancellationToken cancellationToken)
    {
        var instance = await _instanceManager.FindInstanceAsync(id, cancellationToken);
        if (instance is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "instance_not_found",
                Message = $"Instance '{id}' was not found."
            });
        }

        if (instance.Status is not (InstanceStatus.Failed or InstanceStatus.Interrupted))
        {
            return Conflict(new ErrorResponse
            {
                Error = "invalid_state",
                Message = "Instance is not in a failed or interrupted state"
            });
        }

        // Atomic orchestrator-slot claim. A GetMessageWriter-presence check
        // alone is non-atomic against a concurrent in-flight orchestrator
        // lifecycle, so a rapid double-click Retry could race the first stream's
        // teardown. TryClaim is the single source-of-truth gate and returns
        // 409 on the loser cleanly.
        if (!_activeInstanceRegistry.TryClaim(id))
        {
            return Conflict(new ErrorResponse
            {
                Error = "already_running",
                Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
            });
        }

        try
        {
            // Failed paths resume from the StepStatus.Error step; Interrupted
            // paths resume from the StepStatus.Active step (the orphan left
            // when the SSE stream was torn down mid-execution).
            var stepStatusToFind = instance.Status == InstanceStatus.Failed
                ? StepStatus.Error
                : StepStatus.Active;

            var stepIndex = instance.Steps.FindIndex(s => s.Status == stepStatusToFind);
            if (stepIndex == -1)
            {
                _activeInstanceRegistry.UnregisterInstance(id);
                return Conflict(new ErrorResponse
                {
                    Error = "invalid_state",
                    Message = instance.Status == InstanceStatus.Failed
                        ? "No errored step found to resume"
                        : "No active step found to resume"
                });
            }

            // Reconcile CurrentStepIndex with the step FindIndex actually
            // resumes from. The Failed and Interrupted branches both use
            // FindIndex as authority but historically never wrote the discovered
            // index back when CurrentStepIndex disagreed. Fixed here because we
            // are already inside the retry endpoint's serialised mutation path.
            // No-op if already in sync.
            if (instance.CurrentStepIndex != stepIndex)
            {
                instance = await _instanceManager.SetCurrentStepIndexAsync(
                    instance.WorkflowAlias, instance.InstanceId, stepIndex, cancellationToken);
            }

            // Capture targetStep from the post-reconciliation instance so
            // downstream uses (WipeHistoryAsync, UpdateStepStatusAsync) see
            // the step from the state we just wrote back, not the pre-read.
            var targetStep = instance.Steps[stepIndex];

            // Failed retries wipe the conversation log and restart the step
            // from scratch. The original JSONL is archived to
            //   conversation-{stepId}.failed-{ISO8601-UTC}.jsonl
            // so the retry sees a fresh empty conversation. The model never
            // witnesses the degenerate "all my tools have already run" state
            // that would trip the stall-recovery nudge on retry. The on-disk
            // .fetch-cache/ + cache-on-hit path makes the re-issued fetch_url
            // calls cheap (ms, no HTTP).
            //
            // For Interrupted, the stream was torn down mid-response — no
            // failed assistant message was committed to JSONL (the recorder
            // writes on completion boundaries, not deltas). No wipe, no
            // truncate — the conversation is already at a clean boundary.
            if (instance.Status == InstanceStatus.Failed)
            {
                string? archivedTo;
                try
                {
                    archivedTo = await _conversationStore.WipeHistoryAsync(
                        instance.WorkflowAlias, instance.InstanceId, targetStep.Id, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Story 10.13 AC5: do not echo ex.Message — it can leak file
                    // paths or implementation detail to admins/marketplace users.
                    // Full diagnostic context (including stack) is on the log.
                    _logger.LogError(ex,
                        "Retry failed: could not wipe conversation history for {WorkflowAlias}/{InstanceId}/{StepId}",
                        instance.WorkflowAlias, instance.InstanceId, targetStep.Id);
                    _activeInstanceRegistry.UnregisterInstance(id);
                    return Conflict(new ErrorResponse
                    {
                        Error = "retry_recovery_failed",
                        Message = "Failed to prepare conversation for retry. Check server logs for details."
                    });
                }

                _logger.LogInformation(
                    "Retry recovery for {WorkflowAlias}/{InstanceId}/{StepId}: RecoveryStrategy={RecoveryStrategy} ArchivedTo={ArchivedTo}",
                    instance.WorkflowAlias, instance.InstanceId, targetStep.Id, "wipe-and-restart", archivedTo ?? "(none)");
            }

            // Reset step to Pending so the executor handles the Active transition
            await _instanceManager.UpdateStepStatusAsync(
                instance.WorkflowAlias, instance.InstanceId, stepIndex, StepStatus.Pending, cancellationToken);

            // Set instance back to Running. Map the manager's "already running"
            // InvalidOperationException into a 409 — defence-in-depth once
            // TryClaim is the primary gate, but retained because a stale
            // persisted status from a prior write could in principle still
            // trip the in-manager guard.
            try
            {
                instance = await _instanceManager.SetInstanceStatusAsync(
                    instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Running, cancellationToken);
            }
            catch (InstanceAlreadyRunningException)
            {
                _activeInstanceRegistry.UnregisterInstance(id);
                return Conflict(new ErrorResponse
                {
                    Error = "already_running",
                    Message = $"Instance '{id}' is already running. Concurrent execution is not permitted."
                });
            }

            return await ExecuteSseAsync(instance, cancellationToken);
        }
        catch
        {
            // Anything thrown before the orchestrator's lifecycle takes over must
            // release the claim. Once ExecuteSseAsync enters the orchestrator's
            // try/finally, the orchestrator's UnregisterInstance handles release
            // on all exit paths. A second UnregisterInstance call from here is a
            // safe no-op (TryRemove returns false).
            _activeInstanceRegistry.UnregisterInstance(id);
            throw;
        }
    }

    private async Task<IActionResult> ExecuteSseAsync(InstanceState instance, CancellationToken cancellationToken)
    {
        SseHelper.ConfigureSseResponse(Response);

        var emitter = new SseEventEmitter(
            Response.Body, _loggerFactory.CreateLogger<SseEventEmitter>());

        // SSE keepalive. Reverse proxies with idle-read timeouts (nginx/AWS
        // ALB default 60s, Cloudflare ~30s) close SSE connections during long
        // LLM thinking windows, causing the disconnect path to fire spuriously.
        // A fire-and-forget heartbeat writes `: keepalive\n\n` every
        // KeepaliveInterval (default 15s, clamped to [5s, 300s]).
        var rawInterval = _options.KeepaliveInterval;
        var keepaliveInterval = ClampInterval(rawInterval);
        if (keepaliveInterval != rawInterval
            && Interlocked.CompareExchange(ref _keepaliveClampWarned, 1, 0) == 0)
        {
            _logger.LogWarning(
                "AgentRun:KeepaliveInterval {RawInterval} is outside [{Min}, {Max}]; clamped to {Clamped}",
                rawInterval, KeepaliveMin, KeepaliveMax, keepaliveInterval);
        }

        // CancellationTokenSource.Dispose() does NOT cancel the token (MS docs:
        // "Dispose... releases all resources"). A plain `using var` would leave
        // the heartbeat running past orchestrator return, relying on the next
        // stream write to fail. Explicit Cancel-then-Dispose in finally gives
        // deterministic teardown on every exit path (happy, OCE, exception).
        var keepaliveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            _ = emitter.StartKeepaliveAsync(keepaliveInterval, keepaliveCts.Token);

            try
            {
                await _workflowOrchestrator.ExecuteNextStepAsync(
                    instance.WorkflowAlias, instance.InstanceId, emitter, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // If the cancel endpoint already persisted Cancelled, preserve
                // that status. Writing Failed here would overwrite the user's
                // explicit cancel with a disconnect-style failure. A fresh
                // FindInstanceAsync is required because the in-memory `instance`
                // variable was loaded before the cancel endpoint mutated the
                // YAML. The disconnect path (status=Running) is handled below.
                var current = await _instanceManager.FindInstanceAsync(
                    instance.InstanceId, CancellationToken.None);

                if (current is not null && current.Status == InstanceStatus.Cancelled)
                {
                    // Deliberate server-initiated cancellation (cancel endpoint
                    // already persisted Cancelled). The SSE stream ends cleanly —
                    // do NOT rethrow. Rethrowing produces a 100-line "unhandled
                    // exception" log every time Cancel is clicked because the OCE
                    // surfaces as an aborted controller action, even though the
                    // run was stopped correctly. Emit a terminal run.finished event
                    // so non-initiating observers can distinguish cancel from a
                    // dropped connection.
                    _logger.LogInformation(
                        "Cancellation observed for instance {InstanceId}; preserving Cancelled status",
                        instance.InstanceId);

                    try
                    {
                        await emitter.EmitRunFinishedAsync(
                            instance.InstanceId, "Cancelled", CancellationToken.None);
                    }
                    catch (Exception emitEx)
                    {
                        _logger.LogDebug(emitEx,
                            "Failed to emit run.finished(Cancelled) for instance {InstanceId}; client stream likely already closed",
                            instance.InstanceId);
                    }

                    return new EmptyResult();
                }

                // Distinguish client disconnect (tab close, network drop, F5,
                // proxy idle timeout) from a provider-internal OCE. The
                // controller `cancellationToken` parameter is bound from
                // `HttpContext.RequestAborted` by the ASP.NET model binder — it
                // fires iff the HTTP request is aborted. Provider-internal OCEs
                // (e.g., a nested HttpClient timeout) do NOT cancel this token.
                // When the client has gone away, persisting Failed is
                // semantically wrong — the run was progressing normally.
                // Persist Interrupted instead and return cleanly (same
                // "don't rethrow a handled OCE" pattern as the Cancelled branch
                // above). Gate on current.Status == Running: if the orchestrator
                // has already transitioned the run to a terminal state
                // (Completed/Failed/Cancelled) before the OCE propagated here,
                // we must NOT attempt an Interrupted write. The terminal-
                // transition guard in InstanceManager would refuse silently,
                // but the "marking Interrupted" log would lie about what
                // happened. For Pending/Interrupted, the same write would
                // refresh UpdatedAt for no user-visible gain.
                if (current is not null
                    && current.Status == InstanceStatus.Running
                    && cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "SSE client disconnected for instance {InstanceId}; marking Interrupted",
                        instance.InstanceId);

                    try
                    {
                        await _instanceManager.SetInstanceStatusAsync(
                            instance.WorkflowAlias, instance.InstanceId,
                            InstanceStatus.Interrupted, CancellationToken.None);
                    }
                    catch (Exception statusEx)
                    {
                        _logger.LogError(statusEx,
                            "Failed to set instance {InstanceId} status to Interrupted after disconnect",
                            instance.InstanceId);
                    }

                    // Do NOT emit run.finished(Interrupted) — the client is gone;
                    // the emit would fail on a half-closed stream (locked decision 8).
                    return new EmptyResult();
                }

                try
                {
                    await _instanceManager.SetInstanceStatusAsync(
                        instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Failed, CancellationToken.None);
                }
                catch (Exception statusEx)
                {
                    _logger.LogCritical(statusEx,
                        "Failed to set instance {InstanceId} status to Failed after cancellation",
                        instance.InstanceId);
                }

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Execution failed for instance {InstanceId} of workflow {WorkflowAlias}",
                    instance.InstanceId, instance.WorkflowAlias);

                try
                {
                    await _instanceManager.SetInstanceStatusAsync(
                        instance.WorkflowAlias, instance.InstanceId, InstanceStatus.Failed, CancellationToken.None);
                }
                catch (Exception statusEx)
                {
                    _logger.LogCritical(statusEx,
                        "Failed to set instance {InstanceId} status to Failed",
                        instance.InstanceId);
                }

                try
                {
                    // Story 10.13 AC5: classified-LLM messages flow through their
                    // own emit path with safe text (LlmErrorClassifier). Anything
                    // reaching the catch-all here is unclassified and must not
                    // surface raw exception text to the client.
                    await emitter.EmitRunErrorAsync(
                        "execution_error",
                        "Workflow execution failed. Check server logs for details.",
                        CancellationToken.None);
                }
                catch (Exception sseEx)
                {
                    _logger.LogWarning(sseEx,
                        "Failed to emit run.error SSE event for instance {InstanceId}",
                        instance.InstanceId);
                }
            }

            return new EmptyResult();
        }
        finally
        {
            keepaliveCts.Cancel();
            keepaliveCts.Dispose();
        }
    }

    [HttpPost("instances/{id}/message")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ErrorResponse>(400)]
    [ProducesResponseType<ErrorResponse>(409)]
    public IActionResult SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "empty_message",
                Message = "Message cannot be empty."
            });
        }

        var writer = _activeInstanceRegistry.GetMessageWriter(id);
        if (writer is null)
        {
            return Conflict(new ErrorResponse
            {
                Error = "not_running",
                Message = $"Instance '{id}' is not currently executing a step."
            });
        }

        writer.TryWrite(request.Message);
        return Ok();
    }
}
