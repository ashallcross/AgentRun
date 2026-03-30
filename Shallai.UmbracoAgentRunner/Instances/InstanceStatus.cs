using System.Text.Json.Serialization;

namespace Shallai.UmbracoAgentRunner.Instances;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstanceStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
