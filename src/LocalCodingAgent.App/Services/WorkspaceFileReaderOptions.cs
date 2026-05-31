namespace LocalCodingAgent.App.Services;

public sealed class WorkspaceFileReaderOptions
{
    public int MaxLines { get; set; } = 300;
    public int MaxCharacters { get; set; } = 12_000;
    public long MaxBytes { get; set; } = 256 * 1024;
    public string[] AllowedExtensions { get; set; } =
    [
        ".cs",
        ".csproj",
        ".sln",
        ".slnx",
        ".json",
        ".md",
        ".txt",
        ".xml",
        ".yml",
        ".yaml"
    ];
}
