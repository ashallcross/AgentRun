using System.Text.Json.Serialization;

namespace AgentRun.Umbraco.Instances;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstanceStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
