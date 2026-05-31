using System.ComponentModel;

namespace LocalCodingAgent.App.Services;

public sealed class ListFilesTool
{
    private readonly WorkspaceFileLister _lister;

    public ListFilesTool(WorkspaceFileLister lister)
    {
        _lister = lister;
    }

    [Description("List readable source and config files from a workspace directory. Only relative directory paths inside the workspace are allowed.")]
    public string ListFiles(
        [Description("A relative directory path inside the workspace. Use '.' for the workspace root or a subdirectory like 'src'.")]
        string directoryPath)
    {
        var result = _lister.ListFiles(directoryPath);

        if (!result.Success)
        {
            return $"ERROR: {result.ErrorMessage}";
        }

        if (result.Files.Count == 0)
        {
            return $"No readable files were found under '{result.RelativeDirectory}'.";
        }

        var text = string.Join(Environment.NewLine, result.Files);

        if (!result.Truncated)
        {
            return text;
        }

        return text +
               Environment.NewLine +
               Environment.NewLine +
               $"[truncated] returned {result.ReturnedFiles}/{result.TotalFiles} files.";
    }
}
