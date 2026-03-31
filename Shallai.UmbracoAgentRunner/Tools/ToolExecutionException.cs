namespace Shallai.UmbracoAgentRunner.Tools;

public class ToolExecutionException : Engine.AgentRunnerException
{
    public ToolExecutionException(string message) : base(message)
    {
    }

    public ToolExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
