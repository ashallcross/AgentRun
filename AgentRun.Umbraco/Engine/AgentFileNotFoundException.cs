namespace AgentRun.Umbraco.Engine;

public sealed class AgentFileNotFoundException : AgentRunException
{
    public AgentFileNotFoundException(string agentPath)
        : base($"Agent file not found: '{agentPath}'")
    {
    }
}
