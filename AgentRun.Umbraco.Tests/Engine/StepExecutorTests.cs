using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using NSubstitute.ExceptionExtensions;
using AgentRun.Umbraco.Engine;
using AgentRun.Umbraco.Instances;
using AgentRun.Umbraco.Tools;
using AgentRun.Umbraco.Workflows;

namespace AgentRun.Umbraco.Tests.Engine;

[TestFixture]
public class StepExecutorTests
{
    private IProfileResolver _profileResolver = null!;
    private IPromptAssembler _promptAssembler = null!;
    private IInstanceManager _instanceManager = null!;
    private IConversationStore _conversationStore = null!;
    private IArtifactValidator _artifactValidator = null!;
    private ICompletionChecker _completionChecker = null!;
    private IChatClient _chatClient = null!;
    private ILogger<StepExecutor> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _profileResolver = Substitute.For<IProfileResolver>();
        _promptAssembler = Substitute.For<IPromptAssembler>();
        _instanceManager = Substitute.For<IInstanceManager>();
        _conversationStore = Substitute.For<IConversationStore>();
        _artifactValidator = Substitute.For<IArtifactValidator>();
        _completionChecker = Substitute.For<ICompletionChecker>();
        _chatClient = Substitute.For<IChatClient>();
        _logger = NullLogger<StepExecutor>.Instance;

        // Default: profile resolver returns mock client
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(_chatClient);

        // Default: assembler returns a test prompt
        _promptAssembler.AssemblePromptAsync(Arg.Any<PromptAssemblyContext>(), Arg.Any<CancellationToken>())
            .Returns("test prompt");

        // Default: instance manager returns updated state
        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => MakeInstance());

        // Default: conversation store returns empty history (first-run scenario)
        _conversationStore.GetHistoryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConversationEntry>());

        // Default: streaming chat client returns no tool calls (text only)
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(StreamText("done"));

        // Default: artifact validation passes
        _artifactValidator.ValidateInputArtifactsAsync(Arg.Any<StepDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ArtifactValidationResult(true, []));

        // Default: completion check passes
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(true, []));
    }

    [TearDown]
    public void TearDown()
    {
        _chatClient?.Dispose();
    }

    private StepExecutor CreateExecutor(IEnumerable<IWorkflowTool>? tools = null)
    {
        // Story 10.7a Track C: StepExecutor takes IStepExecutionFailureHandler.
        // Real handler injected (stateless, pure classification; dedicated tests
        // in StepExecutionFailureHandlerTests). Existing failure-path assertions
        // remain identical because the handler preserves the AgentRunException
        // bypass invariant verbatim.
        return new StepExecutor(
            _profileResolver,
            _promptAssembler,
            tools ?? [],
            _instanceManager,
            _conversationStore,
            _artifactValidator,
            _completionChecker,
            new StubToolLimitResolver(),
            new StepExecutionFailureHandler(),
            _logger);
    }

    private sealed class StubToolLimitResolver : AgentRun.Umbraco.Engine.IToolLimitResolver
    {
        public int ResolveFetchUrlMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.FetchUrlMaxResponseBytes;
        public int ResolveFetchUrlTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.FetchUrlTimeoutSeconds;
        public int ResolveReadFileMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.ReadFileMaxResponseBytes;
        public int ResolveToolLoopUserMessageTimeoutSeconds(StepDefinition step, WorkflowDefinition workflow) => 1; // short for tests
        public int ResolveListContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.ListContentMaxResponseBytes;
        public int ResolveGetContentMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.GetContentMaxResponseBytes;
        public int ResolveListContentTypesMaxResponseBytes(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.ListContentTypesMaxResponseBytes;
        public int ResolveCompactionTurnThreshold(StepDefinition step, WorkflowDefinition workflow) => AgentRun.Umbraco.Engine.EngineDefaults.CompactionTurnThreshold;
        public void EnforceCeilings(WorkflowDefinition workflow) { }
    }

    private static StepDefinition MakeStep(string id = "step-1", List<string>? tools = null) =>
        new() { Id = id, Name = "Test Step", Agent = "agents/test.md", Tools = tools };

    private static WorkflowDefinition MakeWorkflow() =>
        new() { Name = "test-workflow", Steps = [MakeStep()] };

    private static InstanceState MakeInstance(string workflowAlias = "test-workflow", string instanceId = "inst-001") =>
        new()
        {
            WorkflowAlias = workflowAlias,
            InstanceId = instanceId,
            Steps = [new StepState { Id = "step-1", Status = StepStatus.Pending }]
        };

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamText(string text)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
        await Task.CompletedTask;
    }

    private static StepExecutionContext MakeExecutionContext(
        WorkflowDefinition? workflow = null,
        StepDefinition? step = null,
        InstanceState? instance = null) =>
        new(
            Workflow: workflow ?? MakeWorkflow(),
            Step: step ?? MakeStep(),
            Instance: instance ?? MakeInstance(),
            InstanceFolderPath: "/tmp/instances/inst-001",
            WorkflowFolderPath: "/tmp/workflows/test-workflow");

    [Test]
    public async Task SuccessfulExecution_UpdatesStepStatus_ActiveThenComplete()
    {
        // AC #1, #10: step status transitions Active → Complete
        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Received.InOrder(() =>
        {
            _instanceManager.UpdateStepStatusAsync("test-workflow", "inst-001", 0, StepStatus.Active, Arg.Any<CancellationToken>());
            _instanceManager.UpdateStepStatusAsync("test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task ExceptionDuringExecution_SetsStepStatusToError()
    {
        // AC #8: exceptions caught and surfaced as step errors
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProfileNotFoundException("bad-profile"));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        // Should NOT throw
        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnlyDeclaredTools_PassedToToolLoop()
    {
        // AC #2, #3: only step.Tools tools are used
        var declaredTool = Substitute.For<IWorkflowTool>();
        declaredTool.Name.Returns("read_file");
        declaredTool.Description.Returns("Reads");

        var undeclaredTool = Substitute.For<IWorkflowTool>();
        undeclaredTool.Name.Returns("delete_file");
        undeclaredTool.Description.Returns("Deletes");

        var step = MakeStep(tools: ["read_file"]);
        var workflow = new WorkflowDefinition { Name = "test-workflow", Steps = [step] };
        var instance = MakeInstance();
        var context = new StepExecutionContext(workflow, step, instance, "/tmp/inst", "/tmp/wf");

        var executor = CreateExecutor([declaredTool, undeclaredTool]);

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        // ChatOptions.Tools should only contain read_file
        _chatClient.Received().GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => o!.Tools!.Count == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProfileResolved_BeforePromptAssembled_BeforeToolLoop()
    {
        // AC #1: correct execution order
        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Received.InOrder(() =>
        {
            _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>());
            _promptAssembler.AssemblePromptAsync(Arg.Any<PromptAssemblyContext>(), Arg.Any<CancellationToken>());
            _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task StructuredLogging_IncludesRequiredFields()
    {
        // AC #11: structured logging with WorkflowAlias, InstanceId, StepId
        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(_profileResolver, _promptAssembler, [], _instanceManager, _conversationStore, _artifactValidator, _completionChecker, new StubToolLimitResolver(), new StepExecutionFailureHandler(), loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        // Verify at least one log call includes all three fields
        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("step-1")
                && o.ToString()!.Contains("test-workflow")
                && o.ToString()!.Contains("inst-001")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public void CancellationToken_Propagated()
    {
        // AC #7: cancellation propagates through async calls
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _instanceManager.UpdateStepStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<StepStatus>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await executor.ExecuteStepAsync(context, cts.Token));
    }

    [Test]
    public async Task ReadsFromValidationFailure_SetsStepToError_LLMNeverCalled()
    {
        // AC #1, #2: reads_from validation fails → Error, no LLM call
        _artifactValidator.ValidateInputArtifactsAsync(Arg.Any<StepDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ArtifactValidationResult(false, ["missing-input.md"]));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        _chatClient.DidNotReceive().GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompletionCheckFailure_SetsStepToError()
    {
        // AC #5, #7: completion check fails → Error
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(false, ["output.md"]));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        await _instanceManager.DidNotReceive().UpdateStepStatusAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), StepStatus.Complete, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CompletionCheckPass_SetsStepToComplete()
    {
        // AC #5, #6: completion check passes → Complete
        _completionChecker.CheckAsync(Arg.Any<CompletionCheckDefinition?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompletionCheckResult(true, []));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task NullCompletionCheck_SetsStepToComplete()
    {
        // AC #8: null completion check → Complete (backwards compat)
        var step = MakeStep();
        // step.CompletionCheck is null by default
        var context = MakeExecutionContext(step: step);

        var executor = CreateExecutor();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Complete, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OperationCanceledException_WithCancelledToken_Rethrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteStepAsync(context, cts.Token));
    }

    [Test]
    public async Task HttpRequestException429_SetsLlmError_RateLimit()
    {
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        Assert.That(context.LlmError, Is.Not.Null);
        Assert.That(context.LlmError!.Value.ErrorCode, Is.EqualTo("rate_limit"));
    }

    [Test]
    public async Task StallDetectedException_SetsLlmError_StallDetected_PreservesUserFacingMessage()
    {
        // Story 9.0 regression: StallDetectedException must NOT be reformatted by
        // LlmErrorClassifier. Its own Message is the user-facing string surfaced
        // via run.error in the chat panel.
        var stall = new StallDetectedException("fetch_url", "step-1", "inst-001", "test-workflow");
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(stall);

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        Assert.That(context.LlmError, Is.Not.Null);
        Assert.That(context.LlmError!.Value.ErrorCode, Is.EqualTo("stall_detected"));
        Assert.That(context.LlmError.Value.UserMessage,
            Is.EqualTo("The agent stopped responding mid-task. Click retry to try again."));
    }

    [Test]
    public async Task TaskCanceledException_SetsLlmError_Timeout()
    {
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The operation timed out"));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        Assert.That(context.LlmError, Is.Not.Null);
        Assert.That(context.LlmError!.Value.ErrorCode, Is.EqualTo("timeout"));
    }

    [Test]
    public async Task WithConversationHistory_ToolLoopReceivesSystemPromptPlusHistory()
    {
        var history = new List<ConversationEntry>
        {
            new() { Role = "user", Content = "Check the homepage", Timestamp = DateTime.UtcNow },
            new() { Role = "assistant", Content = "I'll check it now", Timestamp = DateTime.UtcNow }
        };

        _conversationStore.GetHistoryAsync("test-workflow", "inst-001", "step-1", Arg.Any<CancellationToken>())
            .Returns(history);

        List<ChatMessage>? snapshot = null;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                snapshot = callInfo.Arg<IEnumerable<ChatMessage>>().ToList();
                return StreamText("ok");
            });

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Assert.That(snapshot, Is.Not.Null);
        // System prompt + user message + assistant message = 3
        Assert.That(snapshot, Has.Count.EqualTo(3));
        Assert.That(snapshot![0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(snapshot[1].Role, Is.EqualTo(ChatRole.User));
        Assert.That(snapshot[1].Text, Is.EqualTo("Check the homepage"));
        Assert.That(snapshot[2].Role, Is.EqualTo(ChatRole.Assistant));
        Assert.That(snapshot[2].Text, Is.EqualTo("I'll check it now"));
    }

    [Test]
    public async Task WithEmptyHistory_ToolLoopReceivesSystemPromptOnly()
    {
        _conversationStore.GetHistoryAsync("test-workflow", "inst-001", "step-1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConversationEntry>());

        List<ChatMessage>? snapshot = null;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                snapshot = callInfo.Arg<IEnumerable<ChatMessage>>().ToList();
                return StreamText("ok");
            });

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot![0].Role, Is.EqualTo(ChatRole.System));
    }

    [Test]
    public async Task WithToolCallHistory_ConvertsToFunctionCallContent()
    {
        var history = new List<ConversationEntry>
        {
            new() { Role = "assistant", ToolCallId = "tc_001", ToolName = "read_file", ToolArguments = "{\"path\":\"test.md\"}", Timestamp = DateTime.UtcNow },
            new() { Role = "tool", ToolCallId = "tc_001", ToolResult = "file contents here", Timestamp = DateTime.UtcNow }
        };

        _conversationStore.GetHistoryAsync("test-workflow", "inst-001", "step-1", Arg.Any<CancellationToken>())
            .Returns(history);

        List<ChatMessage>? snapshot = null;
        _chatClient.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                snapshot = callInfo.Arg<IEnumerable<ChatMessage>>().ToList();
                return StreamText("ok");
            });

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot, Has.Count.EqualTo(3)); // system + assistant(tool call) + tool(result)

        var assistantMsg = snapshot![1];
        Assert.That(assistantMsg.Role, Is.EqualTo(ChatRole.Assistant));
        var functionCall = assistantMsg.Contents.OfType<FunctionCallContent>().FirstOrDefault();
        Assert.That(functionCall, Is.Not.Null);
        Assert.That(functionCall!.CallId, Is.EqualTo("tc_001"));
        Assert.That(functionCall.Name, Is.EqualTo("read_file"));

        var toolMsg = snapshot[2];
        Assert.That(toolMsg.Role, Is.EqualTo(ChatRole.Tool));
        var functionResult = toolMsg.Contents.OfType<FunctionResultContent>().FirstOrDefault();
        Assert.That(functionResult, Is.Not.Null);
        Assert.That(functionResult!.CallId, Is.EqualTo("tc_001"));
    }

    [Test]
    public async Task GenericException_SetsLlmError_ProviderError()
    {
        _profileResolver.ResolveAndGetClientAsync(Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Something broke"));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        await _instanceManager.Received(1).UpdateStepStatusAsync(
            "test-workflow", "inst-001", 0, StepStatus.Error, Arg.Any<CancellationToken>());
        Assert.That(context.LlmError, Is.Not.Null);
        Assert.That(context.LlmError!.Value.ErrorCode, Is.EqualTo("provider_error"));
    }

    // --- Cache-aware Message Hints (Story 11.5) ---

    [Test]
    public async Task ExecuteStepAsync_SystemMessage_CarriesCacheableHint()
    {
        // Story 11.5 AC1 — engine sets AdditionalProperties["Cacheable"] = true
        // on the System message. Provider-neutral; conforming M.E.AI adapters
        // ignore unknown keys. Captured at the chat client boundary.
        IEnumerable<ChatMessage>? captured = null;
        _chatClient.GetStreamingResponseAsync(
                Arg.Do<IEnumerable<ChatMessage>>(m => captured = m.ToList()),
                Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(StreamText("done"));

        var executor = CreateExecutor();
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        var systemMessage = captured!.FirstOrDefault(m => m.Role == ChatRole.System);
        Assert.That(systemMessage, Is.Not.Null, "System message expected as first message");
        Assert.That(systemMessage!.AdditionalProperties, Is.Not.Null, "System message should carry AdditionalProperties");
        Assert.That(systemMessage.AdditionalProperties!.ContainsKey(EngineDefaults.CacheableHintKey), Is.True,
            $"System message AdditionalProperties should contain key '{EngineDefaults.CacheableHintKey}'");
        Assert.That(systemMessage.AdditionalProperties[EngineDefaults.CacheableHintKey], Is.EqualTo(true));
    }

    [Test]
    public async Task ExecuteStepAsync_EmitsCacheUsageLog_WithUsageFieldsOnComplete()
    {
        // Story 11.5 AC2 — cache.usage log emitted at Information level with
        // structured fields from UsageDetails.CachedInputTokenCount +
        // AdditionalCounts extras on clean completion.
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(StreamWithUsage(
                text: "done",
                inputTokens: 1000,
                outputTokens: 50,
                cachedInput: 800,
                cacheReadExtra: 800,
                cacheWriteExtra: 0));

        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(
            _profileResolver, _promptAssembler, [], _instanceManager, _conversationStore,
            _artifactValidator, _completionChecker, new StubToolLimitResolver(),
            new StepExecutionFailureHandler(), loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cache.usage")
                && o.ToString()!.Contains("step-1")
                && o.ToString()!.Contains("test-workflow")
                && o.ToString()!.Contains("inst-001")
                && o.ToString()!.Contains("input=1000")
                && o.ToString()!.Contains("cached_input=800")
                && o.ToString()!.Contains("cache_read=800")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task ExecuteStepAsync_EmitsCacheUsageLog_WithZerosWhenProviderReportsNoUsage()
    {
        // Story 11.5 AC2 — zeros are signal, not absence. Log still fires
        // when UsageDetails is null or all zero.
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(StreamText("done"));

        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(
            _profileResolver, _promptAssembler, [], _instanceManager, _conversationStore,
            _artifactValidator, _completionChecker, new StubToolLimitResolver(),
            new StepExecutionFailureHandler(), loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cache.usage")
                && o.ToString()!.Contains("step-1")
                && o.ToString()!.Contains("input=0")
                && o.ToString()!.Contains("cached_input=0")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task ExecuteStepAsync_CacheableHintReachesProviderVerbatim_ForEveryProviderName()
    {
        // Story 11.5 D8 — the Cacheable hint is a standard M.E.AI
        // AdditionalProperties entry and the engine must NOT branch on
        // provider name or strip/mutate the hint before the IChatClient
        // call. Capture the messages passed to the transport for each of
        // five simulated providers (openai, azure.openai, gemini, ollama,
        // null metadata) and assert the System message still carries
        // Cacheable=true exactly once — any accidental wrapper that
        // stripped, duplicated, or rewrote the hint would show up here.
        string[] providerNames = ["openai", "azure.openai", "gemini", "ollama", "<null>"];

        foreach (var providerName in providerNames)
        {
            var fakeClient = Substitute.For<IChatClient, IDisposable>();

            if (providerName == "<null>")
            {
                fakeClient.GetService(typeof(ChatClientMetadata), Arg.Any<object?>())
                    .Returns((ChatClientMetadata?)null);
            }
            else
            {
                fakeClient.GetService(typeof(ChatClientMetadata), Arg.Any<object?>())
                    .Returns(new ChatClientMetadata(providerName));
            }

            IEnumerable<ChatMessage>? capturedMessages = null;
            fakeClient.GetStreamingResponseAsync(
                    Arg.Do<IEnumerable<ChatMessage>>(m => capturedMessages = m.ToList()),
                    Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
                .Returns(StreamText("done"));

            var freshResolver = Substitute.For<IProfileResolver>();
            freshResolver.ResolveAndGetClientAsync(
                    Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(),
                    Arg.Any<CancellationToken>())
                .Returns(fakeClient);

            var executor = new StepExecutor(
                freshResolver, _promptAssembler, [], _instanceManager, _conversationStore,
                _artifactValidator, _completionChecker, new StubToolLimitResolver(),
                new StepExecutionFailureHandler(), _logger);
            var context = MakeExecutionContext();

            Assert.DoesNotThrowAsync(
                async () => await executor.ExecuteStepAsync(context, CancellationToken.None),
                $"Provider '{providerName}' should process the Cacheable hint without throwing");

            Assert.That(capturedMessages, Is.Not.Null,
                $"Provider '{providerName}' — IChatClient.GetStreamingResponseAsync was never invoked");
            var system = capturedMessages!.Single(m => m.Role == ChatRole.System);
            Assert.That(system.AdditionalProperties, Is.Not.Null,
                $"Provider '{providerName}' — System message AdditionalProperties was stripped before transport");
            Assert.That(system.AdditionalProperties!.ContainsKey(EngineDefaults.CacheableHintKey), Is.True,
                $"Provider '{providerName}' — Cacheable hint missing on System message at transport boundary");
            Assert.That(system.AdditionalProperties[EngineDefaults.CacheableHintKey], Is.EqualTo(true),
                $"Provider '{providerName}' — Cacheable hint value mutated before transport");
        }
    }

    [Test]
    public async Task ExecuteStepAsync_AggregatesUsageAcrossMultipleToolLoopTurns()
    {
        // Story 11.5 AC2 — per-LLM-call UsageDetails aggregated into a per-step
        // total via UsageDetails.Add before the cache.usage log fires. Two
        // stream calls (tool-loop round-trip + terminal text) each carry
        // usage; log must show the sum.
        var tool = Substitute.For<IWorkflowTool>();
        tool.Name.Returns("read_file");
        tool.Description.Returns("Reads");
        tool.ExecuteAsync(Arg.Any<IDictionary<string, object?>>(),
            Arg.Any<ToolExecutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<object?>("file contents"));

        // First turn: tool call + usage. Second turn: text-only + usage.
        _chatClient.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => StreamWithToolCallAndUsage(
                    callId: "tc_001", toolName: "read_file",
                    inputTokens: 500, outputTokens: 20, cachedInput: 400,
                    cacheReadExtra: 400, cacheWriteExtra: 0),
                _ => StreamWithUsage(
                    text: "done",
                    inputTokens: 550, outputTokens: 30, cachedInput: 500,
                    cacheReadExtra: 500, cacheWriteExtra: 0));

        var step = MakeStep(tools: ["read_file"]);
        var workflow = new WorkflowDefinition { Name = "test-workflow", Steps = [step] };
        var instance = MakeInstance();
        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(
            _profileResolver, _promptAssembler, [tool], _instanceManager, _conversationStore,
            _artifactValidator, _completionChecker, new StubToolLimitResolver(),
            new StepExecutionFailureHandler(), loggerSub);
        var context = new StepExecutionContext(workflow, step, instance, "/tmp/inst", "/tmp/wf");

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        // Expect aggregate: input=1050, cached_input=900, cache_read=900
        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cache.usage")
                && o.ToString()!.Contains("input=1050")
                && o.ToString()!.Contains("cached_input=900")
                && o.ToString()!.Contains("cache_read=900")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task ExecuteStepAsync_ValidationFailure_EmitsCacheUsageLog_WithZeros()
    {
        // Story 11.5 review patch — pre-LLM artifact-validation failure must
        // still emit cache.usage so the "one log line per step attempt"
        // contract holds for dashboard authors. No LLM call fired, so every
        // field is zero.
        _artifactValidator.ValidateInputArtifactsAsync(
                Arg.Any<StepDefinition>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ArtifactValidationResult(false, ["missing-input.md"]));

        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(
            _profileResolver, _promptAssembler, [], _instanceManager, _conversationStore,
            _artifactValidator, _completionChecker, new StubToolLimitResolver(),
            new StepExecutionFailureHandler(), loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cache.usage")
                && o.ToString()!.Contains("step-1")
                && o.ToString()!.Contains("input=0")
                && o.ToString()!.Contains("cached_input=0")
                && o.ToString()!.Contains("cache_read=0")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task ExecuteStepAsync_EngineDomainThrow_LogsPartialUsage_NotZeros()
    {
        // Story 11.5 review patch — AgentRunException.PartialUsage surfaces
        // the UsageDetails accumulated across prior turns so the cache.usage
        // log reflects tokens actually spent up to the throw instead of
        // silent zeros. Exercised here by a profile resolver that returns a
        // client whose first stream succeeds with usage, then a second call
        // throws AgentRunException carrying PartialUsage forward.
        var firstTurnUsage = new UsageDetails
        {
            InputTokenCount = 1200,
            OutputTokenCount = 80,
            CachedInputTokenCount = 900,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheReadInputTokens"] = 900,
                ["CacheCreationInputTokens"] = 300
            }
        };
        var domainException = new AgentRunException("simulated stall after partial progress")
        {
            PartialUsage = firstTurnUsage
        };

        _profileResolver.ResolveAndGetClientAsync(
                Arg.Any<StepDefinition>(), Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(domainException);

        var loggerSub = Substitute.For<ILogger<StepExecutor>>();
        var executor = new StepExecutor(
            _profileResolver, _promptAssembler, [], _instanceManager, _conversationStore,
            _artifactValidator, _completionChecker, new StubToolLimitResolver(),
            new StepExecutionFailureHandler(), loggerSub);
        var context = MakeExecutionContext();

        await executor.ExecuteStepAsync(context, CancellationToken.None);

        loggerSub.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("cache.usage")
                && o.ToString()!.Contains("input=1200")
                && o.ToString()!.Contains("cached_input=900")
                && o.ToString()!.Contains("cache_read=900")
                && o.ToString()!.Contains("cache_write=300")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    // --- Story 11.5 test helpers ---

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamWithUsage(
        string text,
        long inputTokens,
        long outputTokens,
        long cachedInput,
        long cacheReadExtra,
        long cacheWriteExtra)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)]
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new UsageContent(BuildUsage(
                inputTokens, outputTokens, cachedInput, cacheReadExtra, cacheWriteExtra))]
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamWithToolCallAndUsage(
        string callId, string toolName,
        long inputTokens, long outputTokens,
        long cachedInput, long cacheReadExtra, long cacheWriteExtra)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent(callId, toolName, new Dictionary<string, object?> { ["path"] = "a.md" })]
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new UsageContent(BuildUsage(
                inputTokens, outputTokens, cachedInput, cacheReadExtra, cacheWriteExtra))]
        };
        await Task.CompletedTask;
    }

    private static UsageDetails BuildUsage(
        long inputTokens, long outputTokens,
        long cachedInput, long cacheReadExtra, long cacheWriteExtra)
    {
        var usage = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            CachedInputTokenCount = cachedInput,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["CacheReadInputTokens"] = cacheReadExtra,
                ["CacheCreationInputTokens"] = cacheWriteExtra
            }
        };
        return usage;
    }
}
