namespace LocalCodingAgent.App.Services;

public sealed class FileSummaryRecord
{
    public string SourcePath { get; set; } = string.Empty;
    public DateTime SourceLastWriteTimeUtc { get; set; }
    public long SourceLength { get; set; }
    public string SourceHash { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string SummaryText { get; set; } = string.Empty;
}
