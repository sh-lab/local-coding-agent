namespace LocalCodingAgent.App.Services;

public sealed class PlanExecutionPreview
{
    public string PlanId { get; init; } = string.Empty;
    public string PlanPath { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<string> RelevantFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProposedMinimalChanges { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RisksOrUnknowns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ApprovalChecklist { get; init; } = Array.Empty<string>();
    public string RawPlanMarkdown { get; init; } = string.Empty;
}