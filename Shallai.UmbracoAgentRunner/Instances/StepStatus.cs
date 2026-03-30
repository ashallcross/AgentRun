using System.Text.Json.Serialization;

namespace Shallai.UmbracoAgentRunner.Instances;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepStatus
{
    Pending,
    Active,
    Complete,
    Error
}
