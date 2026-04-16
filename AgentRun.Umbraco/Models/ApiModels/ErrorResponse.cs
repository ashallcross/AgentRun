namespace AgentRun.Umbraco.Models.ApiModels;

public sealed class ErrorResponse
{
    public required string Error { get; init; }
    public required string Message { get; init; }
}
