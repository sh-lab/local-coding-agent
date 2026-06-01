namespace LocalCodingAgent.App.Services;

public sealed class CopilotInstructionsProvider
{
    private readonly string _workspaceRoot;

    public CopilotInstructionsProvider(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<CopilotInstructionsContext?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_workspaceRoot, ".github", "copilot-instructions.md");

        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new CopilotInstructionsContext
        {
            Path = path,
            Content = content.Trim()
        };
    }
}