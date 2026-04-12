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
        return new StepExecutor(
            _profileResolver,
            _promptAssembler,
            tools ?? [],
            _instanceManager,
            _conversationStore,
            _artifactValidator,
            _completionChecker,
            new StubToolLimitResolver(),
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
        var executor = new StepExecutor(_profileResolver, _promptAssembler, [], _instanceManager, _conversationStore, _artifactValidator, _completionChecker, new StubToolLimitResolver(), loggerSub);
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
}
