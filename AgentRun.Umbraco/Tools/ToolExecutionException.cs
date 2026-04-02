namespace AgentRun.Umbraco.Tools;

public class ToolExecutionException : Engine.AgentRunException
{
    public ToolExecutionException(string message) : base(message)
    {
    }

    public ToolExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
