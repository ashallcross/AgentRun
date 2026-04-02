using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentRun.Umbraco.Workflows;

public sealed class WorkflowParser : IWorkflowParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public WorkflowDefinition Parse(string yamlContent)
    {
        ArgumentNullException.ThrowIfNull(yamlContent);

        try
        {
            var result = _deserializer.Deserialize<WorkflowDefinition>(yamlContent);
            return result ?? new WorkflowDefinition();
        }
        catch (YamlException ex)
        {
            throw new InvalidOperationException($"Failed to parse workflow YAML: {ex.Message}", ex);
        }
    }
}
