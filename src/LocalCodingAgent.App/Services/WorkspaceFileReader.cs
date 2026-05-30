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
        IEnumerable<string> allowedExtensions,
        int maxLines = 300,
        int maxCharacters = 12_000,
        long maxBytes = 256 * 1024)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        if (allowedExtensions is null)
        {
            throw new ArgumentNullException(nameof(allowedExtensions));
        }

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _maxLines = maxLines;
        _maxCharacters = maxCharacters;
        _maxBytes = maxBytes;
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