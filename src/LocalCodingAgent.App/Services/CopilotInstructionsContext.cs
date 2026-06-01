namespace LocalCodingAgent.App.Services;

public sealed class CopilotInstructionsContext
{
    public string Path { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}