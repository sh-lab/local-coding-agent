namespace LocalCodingAgent.App.Services;

public sealed class InProgressPlanInfo
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanDirectory { get; init; } = string.Empty;
    public string PlanPath { get; init; } = string.Empty;
    public string PlanMarkdown { get; init; } = string.Empty;
    public PlanMetadata Metadata { get; init; } = new();
}