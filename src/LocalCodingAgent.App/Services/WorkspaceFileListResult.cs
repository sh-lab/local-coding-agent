namespace LocalCodingAgent.App.Services;

public sealed record WorkspaceFileListResult(
    bool Success,
    string RelativeDirectory,
    IReadOnlyList<string> Files,
    string? ErrorMessage,
    bool Truncated,
    int TotalFiles,
    int ReturnedFiles
);
