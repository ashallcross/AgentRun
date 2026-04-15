namespace AgentRun.Umbraco.Instances;

public sealed class InstanceAlreadyRunningException : InvalidOperationException
{
    public InstanceAlreadyRunningException(string instanceId)
        : base($"Instance {instanceId} is already running. Concurrent execution is not permitted.")
    {
        InstanceId = instanceId;
    }

    public string InstanceId { get; }
}
