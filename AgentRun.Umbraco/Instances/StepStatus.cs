using System.Text.Json.Serialization;

namespace AgentRun.Umbraco.Instances;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepStatus
{
    Pending,
    Active,
    Complete,
    Error,
    Cancelled
}
