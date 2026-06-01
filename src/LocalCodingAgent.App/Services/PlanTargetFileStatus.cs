namespace LocalCodingAgent.App.Services;

public sealed class PlanTargetFileStatus
{
    public string SourcePath { get; init; } = string.Empty;
    public bool Exists { get; init; }
    public bool Readable { get; init; }
    public bool SummaryIsCurrent { get; init; }
    public string Note { get; init; } = string.Empty;
}