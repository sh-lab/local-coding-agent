namespace LocalCodingAgent.App.Services;

public sealed class PlannedOutputFile
{
    public string Path { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty; // "modified" | "new"
    public string Reason { get; init; } = string.Empty;
}