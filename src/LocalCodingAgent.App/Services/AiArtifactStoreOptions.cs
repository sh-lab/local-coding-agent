namespace LocalCodingAgent.App.Services;

public sealed class AiArtifactStoreOptions
{
    public string RootDirectory { get; set; } = ".ai-work";
    public string SummariesDirectory { get; set; } = "summaries";
    public string PlansDirectory { get; set; } = "plans";
}
