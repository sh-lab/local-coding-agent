namespace LocalCodingAgent.App.Services;

public sealed class WorkspaceFileListingOptions
{
    public int MaxFiles { get; set; } = 200;

    public string[] ExcludedDirectories { get; set; } =
    [
        ".git",
        "bin",
        "obj",
        ".vs"
    ];
}
