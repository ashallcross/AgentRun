using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentRun.Umbraco.Engine;

// Story 10.7a Track C: promoted from inner class of StepExecutor.
// Internal visibility — only StepExecutor constructs these; keeping access
// narrow so package consumers can't accidentally hand-roll an AITool
// declaration that bypasses the FunctionInvokingChatClient auto-execution
// boundary.
//
// Non-executable tool declaration that carries only metadata for the LLM.
// FunctionInvokingChatClient in the Umbraco.AI middleware pipeline cannot
// auto-execute this — our ToolLoop handles execution via the declaredTools
// dictionary.
internal sealed class ToolDeclaration : AIFunctionDeclaration
{
    private static readonly JsonElement EmptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    public override string Name { get; }
    public override string Description { get; }
    public override JsonElement JsonSchema { get; }

    public ToolDeclaration(string name, string description, JsonElement? parameterSchema)
    {
        Name = name;
        Description = description;
        JsonSchema = parameterSchema ?? EmptySchema;
    }
}
