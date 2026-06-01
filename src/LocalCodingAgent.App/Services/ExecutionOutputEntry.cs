namespace LocalCodingAgent.App.Services;

public sealed class ExecutionOutputEntry
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // "new" | "modified"
}
