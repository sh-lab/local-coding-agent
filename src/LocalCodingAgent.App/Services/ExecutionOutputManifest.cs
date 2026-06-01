namespace LocalCodingAgent.App.Services;

public sealed class ExecutionOutputManifest
{
    public string PlanId { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public List<ExecutionOutputEntry> Files { get; set; } = [];
}
