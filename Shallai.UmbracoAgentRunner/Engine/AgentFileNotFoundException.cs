namespace Shallai.UmbracoAgentRunner.Engine;

public sealed class AgentFileNotFoundException : AgentRunnerException
{
    public AgentFileNotFoundException(string agentPath)
        : base($"Agent file not found: '{agentPath}'")
    {
    }
}
