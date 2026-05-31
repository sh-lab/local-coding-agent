using System.ComponentModel;

namespace LocalCodingAgent.App.Services;

public sealed class ReadFileTool
{
    private readonly WorkspaceFileReader _reader;

    public ReadFileTool(WorkspaceFileReader reader)
    {
        _reader = reader;
    }

    [Description("Read a text source file from the current workspace. Only relative paths inside the workspace are allowed.")]
    public string ReadFile(
        [Description("The relative file path inside the workspace, for example 'src/LocalCodingAgent.App/Program.cs'.")]
        string path)
    {
        var result = _reader.Read(path);

        if (!result.Success)
        {
            return $"ERROR: {result.ErrorMessage}";
        }

        if (!result.Truncated)
        {
            return result.Content ?? string.Empty;
        }

        return
            (result.Content ?? string.Empty) +
            Environment.NewLine +
            Environment.NewLine +
            $"[truncated] returned {result.ReturnedLines}/{result.TotalLines} lines, " +
            $"{result.ReturnedCharacters}/{result.TotalCharacters} characters.";
    }
}
