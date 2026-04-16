using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentRun.Umbraco.Engine;

// Non-executable tool declaration that carries only metadata for the LLM.
// Internal visibility keeps package consumers from hand-rolling an AITool
// declaration that bypasses the FunctionInvokingChatClient auto-execution
// boundary — our ToolLoop handles execution via the declaredTools dictionary,
// not the Umbraco.AI middleware pipeline.
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
