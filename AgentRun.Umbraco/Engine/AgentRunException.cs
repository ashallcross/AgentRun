namespace AgentRun.Umbraco.Engine;

public class AgentRunException : Exception
{
    public AgentRunException(string message) : base(message)
    {
    }

    public AgentRunException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
