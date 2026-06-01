using System.Text;

namespace LocalCodingAgent.App.Services;

public sealed class WorkspaceFileReader
{
    private readonly HashSet<string> _allowedExtensions;
    private readonly string _workspaceRoot;
    private readonly int _maxLines;
    private readonly int _maxCharacters;
    private readonly long _maxBytes;
    private readonly StringComparison _pathComparison;

    public WorkspaceFileReader(
        string workspaceRoot,
        WorkspaceFileReaderOptions options)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        ArgumentNullException.ThrowIfNull(options);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _maxLines = options.MaxLines;
        _maxCharacters = options.MaxCharacters;
        _maxBytes = options.MaxBytes;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        _allowedExtensions = new HashSet<string>(
            (options.AllowedExtensions ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        if (_allowedExtensions.Count == 0)
        {
            throw new ArgumentException("At least one allowed extension is required.", nameof(options));
        }
    }

    public WorkspaceFileReadResult Read(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Fail(relativePath, "Path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            return Fail(relativePath, "Absolute paths are not allowed.");
        }

        var normalizedRelativePath = relativePath.Replace('\\', '/');

        if (normalizedRelativePath.StartsWith("../", StringComparison.Ordinal) ||
            normalizedRelativePath.Contains("/../", StringComparison.Ordinal) ||
            normalizedRelativePath == "..")
        {
            return Fail(relativePath, "Path traversal is not allowed.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));

        if (!IsUnderWorkspace(fullPath))
        {
            return Fail(relativePath, "Access outside the workspace is not allowed.");
        }

        if (!File.Exists(fullPath))
        {
            return Fail(relativePath, "File does not exist.");
        }

        var extension = Path.GetExtension(fullPath);
        if (!_allowedExtensions.Contains(extension))
        {
            return Fail(relativePath, $"File type '{extension}' is not allowed.");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > _maxBytes)
        {
            return Fail(relativePath, $"File is too large. Maximum allowed size is {_maxBytes} bytes.");
        }

        string[] allLines;
        try
        {
            allLines = File.ReadAllLines(fullPath);
        }
        catch (Exception ex)
        {
            return Fail(relativePath, $"Failed to read file: {ex.Message}");
        }

        var totalLines = allLines.Length;
        var totalCharacters = allLines.Sum(l => l.Length);

        var builder = new StringBuilder();
        var returnedLines = 0;
        var returnedCharacters = 0;
        var truncated = false;

        for (var i = 0; i < allLines.Length; i++)
        {
            var numberedLine = $"{i + 1}: {allLines[i]}";
            var extraLength = numberedLine.Length + Environment.NewLine.Length;

            if (returnedLines >= _maxLines || returnedCharacters + extraLength > _maxCharacters)
            {
                truncated = true;
                break;
            }

            builder.AppendLine(numberedLine);
            returnedLines++;
            returnedCharacters += extraLength;
        }

        return new WorkspaceFileReadResult(
            Success: true,
            RelativePath: normalizedRelativePath,
            Content: builder.ToString(),
            ErrorMessage: null,
            Truncated: truncated,
            TotalLines: totalLines,
            ReturnedLines: returnedLines,
            TotalCharacters: totalCharacters,
            ReturnedCharacters: returnedCharacters
        );
    }

    public WorkspaceFileTextResult ReadRawText(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new WorkspaceFileTextResult(false, relativePath ?? string.Empty, null, "Path is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            return new WorkspaceFileTextResult(false, relativePath, null, "Absolute paths are not allowed.");
        }

        var normalizedRelativePath = relativePath.Replace('\\', '/');

        if (normalizedRelativePath.StartsWith("../", StringComparison.Ordinal) ||
            normalizedRelativePath.Contains("/../", StringComparison.Ordinal) ||
            normalizedRelativePath == "..")
        {
            return new WorkspaceFileTextResult(false, relativePath, null, "Path traversal is not allowed.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));

        if (!IsUnderWorkspace(fullPath))
        {
            return new WorkspaceFileTextResult(false, relativePath, null, "Access outside the workspace is not allowed.");
        }

        if (!File.Exists(fullPath))
        {
            return new WorkspaceFileTextResult(false, relativePath, null, "File does not exist.");
        }

        var extension = Path.GetExtension(fullPath);
        if (!_allowedExtensions.Contains(extension))
        {
            return new WorkspaceFileTextResult(false, relativePath, null, $"File type '{extension}' is not allowed.");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > _maxBytes)
        {
            return new WorkspaceFileTextResult(false, relativePath, null,
                $"File is too large. Maximum allowed size is {_maxBytes} bytes.");
        }

        try
        {
            var content = File.ReadAllText(fullPath);
            return new WorkspaceFileTextResult(true, normalizedRelativePath, content, null);
        }
        catch (Exception ex)
        {
            return new WorkspaceFileTextResult(false, relativePath, null, $"Failed to read file: {ex.Message}");
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

    private static WorkspaceFileReadResult Fail(string relativePath, string message)
    {
        return new WorkspaceFileReadResult(
            Success: false,
            RelativePath: relativePath ?? string.Empty,
            Content: null,
            ErrorMessage: message,
            Truncated: false,
            TotalLines: 0,
            ReturnedLines: 0,
            TotalCharacters: 0,
            ReturnedCharacters: 0
        );
    }
}
