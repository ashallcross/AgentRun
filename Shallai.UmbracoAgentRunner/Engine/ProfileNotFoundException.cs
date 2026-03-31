namespace Shallai.UmbracoAgentRunner.Engine;

public class ProfileNotFoundException : AgentRunnerException
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
