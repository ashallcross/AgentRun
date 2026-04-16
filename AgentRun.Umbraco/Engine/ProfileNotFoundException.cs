namespace AgentRun.Umbraco.Engine;

public class ProfileNotFoundException : AgentRunException
{
    public ProfileNotFoundException(string profileAlias)
        : base($"Profile '{profileAlias}' not found in Umbraco.AI configuration")
    {
    }

    public ProfileNotFoundException(string profileAlias, Exception innerException)
        : base($"Profile '{profileAlias}' not found in Umbraco.AI configuration", innerException)
    {
    }
}
