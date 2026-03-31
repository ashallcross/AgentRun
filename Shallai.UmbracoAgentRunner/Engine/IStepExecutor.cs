namespace Shallai.UmbracoAgentRunner.Engine;

public interface IStepExecutor
{
    Task ExecuteStepAsync(StepExecutionContext context, CancellationToken cancellationToken);
}
