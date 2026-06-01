using System.Text.Json;

namespace LocalCodingAgent.App.Services;

public sealed class ExecutionOutputStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _workspaceRoot;
    private readonly string _outputRoot;
    private readonly HashSet<string> _allowedExtensions;
    private readonly StringComparison _pathComparison;

    public ExecutionOutputStore(
        string workspaceRoot,
        string outputRoot,
        IEnumerable<string> allowedExtensions)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new ArgumentException("Output root is required.", nameof(outputRoot));
        }

        ArgumentNullException.ThrowIfNull(allowedExtensions);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _outputRoot = Path.GetFullPath(outputRoot);
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

        if (!IsUnderWorkspace(_outputRoot))
        {
            throw new InvalidOperationException("Output root must stay inside the workspace.");
        }
    }

    public string OutputRoot => _outputRoot;

    public string GetPlanOutputRoot(string planId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            throw new ArgumentException("Plan ID is required.", nameof(planId));
        }

        return Path.Combine(_outputRoot, planId);
    }

    public async Task<string> WriteFileAsync(
        string planId,
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Output content is required.", nameof(content));
        }

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var extension = Path.GetExtension(normalizedRelativePath);

        if (!_allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException($"File type '{extension}' is not allowed for output.");
        }

        var planOutputRoot = GetPlanOutputRoot(planId);
        var fullOutputPath = Path.GetFullPath(Path.Combine(planOutputRoot, normalizedRelativePath));

        if (!IsUnderDirectory(fullOutputPath, planOutputRoot))
        {
            throw new InvalidOperationException("Output path escapes the plan output directory.");
        }

        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("Failed to determine output directory.");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullOutputPath, content, cancellationToken);

        return fullOutputPath;
    }

    public async Task<string> SaveManifestAsync(
        ExecutionOutputManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var planOutputRoot = GetPlanOutputRoot(manifest.PlanId);
        Directory.CreateDirectory(planOutputRoot);

        var manifestPath = Path.Combine(planOutputRoot, "manifest.json");

        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);

        return manifestPath;
    }

    private string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Absolute paths are not allowed.");
        }

        var normalized = relativePath.Replace('\\', '/');

        if (normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized == "..")
        {
            throw new InvalidOperationException("Path traversal is not allowed.");
        }

        return normalized;
    }

    private bool IsUnderWorkspace(string fullPath)
    {
        return IsUnderDirectory(fullPath, _workspaceRoot);
    }

    private bool IsUnderDirectory(string fullPath, string rootDirectory)
    {
        var rootWithSeparator = EnsureTrailingSeparator(rootDirectory);
        var targetWithSeparator = EnsureTrailingSeparator(fullPath);

        return targetWithSeparator.StartsWith(rootWithSeparator, _pathComparison);
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
}