namespace LocalCodingAgent.App.Services;

public sealed record WorkspaceFileTextResult(
    bool Success,
    string RelativePath,
    string? Content,
    string? ErrorMessage
);
