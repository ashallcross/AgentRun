namespace AgentRun.Umbraco.Engine;

public interface IStepExecutor
{
    Task ExecuteStepAsync(StepExecutionContext context, CancellationToken cancellationToken);
}
