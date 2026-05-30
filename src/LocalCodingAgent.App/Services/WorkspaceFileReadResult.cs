namespace LocalCodingAgent.App.Services;

public sealed record WorkspaceFileReadResult(
    bool Success,
    string RelativePath,
    string? Content,
    string? ErrorMessage,
    bool Truncated,
    int TotalLines,
    int ReturnedLines,
    int TotalCharacters,
    int ReturnedCharacters
);
