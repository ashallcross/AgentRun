namespace Shallai.UmbracoAgentRunner.Engine;

public class AgentRunnerException : Exception
{
    public AgentRunnerException(string message) : base(message)
    {
    }

    public AgentRunnerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
