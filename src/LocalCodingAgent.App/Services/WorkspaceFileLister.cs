namespace LocalCodingAgent.App.Services;

public sealed class WorkspaceFileLister
{
    private readonly string _workspaceRoot;
    private readonly HashSet<string> _allowedExtensions;
    private readonly HashSet<string> _excludedDirectories;
    private readonly int _maxFiles;
    private readonly StringComparison _pathComparison;

    public WorkspaceFileLister(
        string workspaceRoot,
        IEnumerable<string> allowedExtensions,
        WorkspaceFileListingOptions options)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        ArgumentNullException.ThrowIfNull(allowedExtensions);
        ArgumentNullException.ThrowIfNull(options);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        _allowedExtensions = new HashSet<string>(
            allowedExtensions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        if (_allowedExtensions.Count == 0)
        {
            throw new ArgumentException("At least one allowed extension is required.", nameof(allowedExtensions));
        }

        _excludedDirectories = new HashSet<string>(
            (options.ExcludedDirectories ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);

        _maxFiles = options.MaxFiles > 0 ? options.MaxFiles : 200;
    }

    public WorkspaceFileListResult ListFiles(string relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return Fail(relativeDirectory, "Directory path is required.");
        }

        if (Path.IsPathRooted(relativeDirectory))
        {
            return Fail(relativeDirectory, "Absolute paths are not allowed.");
        }

        var normalizedRelativeDirectory = relativeDirectory.Replace('\\', '/');

        if (normalizedRelativeDirectory.StartsWith("../", StringComparison.Ordinal) ||
            normalizedRelativeDirectory.Contains("/../", StringComparison.Ordinal) ||
            normalizedRelativeDirectory == "..")
        {
            return Fail(relativeDirectory, "Path traversal is not allowed.");
        }

        var fullDirectoryPath = Path.GetFullPath(Path.Combine(_workspaceRoot, relativeDirectory));

        if (!IsUnderWorkspace(fullDirectoryPath))
        {
            return Fail(relativeDirectory, "Access outside the workspace is not allowed.");
        }

        if (!Directory.Exists(fullDirectoryPath))
        {
            return Fail(relativeDirectory, "Directory does not exist.");
        }

        var allFiles = new List<string>();
        Traverse(fullDirectoryPath, allFiles);

        allFiles.Sort(StringComparer.OrdinalIgnoreCase);

        var totalFiles = allFiles.Count;
        var returnedFiles = allFiles.Take(_maxFiles).ToArray();
        var truncated = totalFiles > returnedFiles.Length;

        return new WorkspaceFileListResult(
            Success: true,
            RelativeDirectory: normalizedRelativeDirectory,
            Files: returnedFiles,
            ErrorMessage: null,
            Truncated: truncated,
            TotalFiles: totalFiles,
            ReturnedFiles: returnedFiles.Length
        );
    }

    private void Traverse(string directory, List<string> files)
    {
        foreach (var subDirectory in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(subDirectory);
            if (_excludedDirectories.Contains(name))
            {
                continue;
            }

            Traverse(subDirectory, files);
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var extension = Path.GetExtension(file);
            if (!_allowedExtensions.Contains(extension))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(_workspaceRoot, file)
                .Replace('\\', '/');

            files.Add(relativePath);
        }
    }

    private bool IsUnderWorkspace(string fullPath)
    {
        var workspaceWithSeparator = EnsureTrailingSeparator(_workspaceRoot);
        var targetWithSeparator = EnsureTrailingSeparator(fullPath);

        return targetWithSeparator.StartsWith(workspaceWithSeparator, _pathComparison);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static WorkspaceFileListResult Fail(string relativeDirectory, string message)
    {
        return new WorkspaceFileListResult(
            Success: false,
            RelativeDirectory: relativeDirectory ?? string.Empty,
            Files: Array.Empty<string>(),
            ErrorMessage: message,
            Truncated: false,
            TotalFiles: 0,
            ReturnedFiles: 0
        );
    }
}
