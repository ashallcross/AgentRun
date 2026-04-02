namespace AgentRun.Umbraco.Engine;

public interface IPromptAssembler
{
    Task<string> AssemblePromptAsync(
        PromptAssemblyContext context,
        CancellationToken cancellationToken);
}
