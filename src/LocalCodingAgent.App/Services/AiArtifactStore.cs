using System.Security.Cryptography;
using System.Text.Json;

namespace LocalCodingAgent.App.Services;

public sealed class AiArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _workspaceRoot;
    private readonly string _artifactRoot;
    private readonly string _summariesRoot;
    private readonly string _plansRoot;
    private readonly StringComparison _pathComparison;

    public AiArtifactStore(string workspaceRoot, AiArtifactStoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        ArgumentNullException.ThrowIfNull(options);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var rootDirectory = string.IsNullOrWhiteSpace(options.RootDirectory)
            ? ".ai-work"
            : options.RootDirectory.Trim();

        var summariesDirectory = string.IsNullOrWhiteSpace(options.SummariesDirectory)
            ? "summaries"
            : options.SummariesDirectory.Trim();

        var plansDirectory = string.IsNullOrWhiteSpace(options.PlansDirectory)
            ? "plans"
            : options.PlansDirectory.Trim();

        _artifactRoot = Path.GetFullPath(Path.Combine(_workspaceRoot, rootDirectory));
        _summariesRoot = Path.GetFullPath(Path.Combine(_artifactRoot, summariesDirectory));
        _plansRoot = Path.GetFullPath(Path.Combine(_artifactRoot, plansDirectory));

        if (!IsUnderWorkspace(_artifactRoot) ||
            !IsUnderWorkspace(_summariesRoot) ||
            !IsUnderWorkspace(_plansRoot))
        {
            throw new InvalidOperationException("Artifact directories must stay inside the workspace.");
        }
    }

    public string WorkspaceRoot => _workspaceRoot;
    public string ArtifactRoot => _artifactRoot;
    public string SummariesRoot => _summariesRoot;
    public string PlansRoot => _plansRoot;

    public string GetSummaryPath(string sourceRelativePath)
    {
        var normalizedRelativePath = NormalizeSourceRelativePath(sourceRelativePath);
        var sourceFullPath = GetSourceFullPath(normalizedRelativePath);

        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException("Source file does not exist.", sourceFullPath);
        }

        var summaryRelativePath = normalizedRelativePath.Replace('\\', '/');
        var summaryFullPath = Path.Combine(_summariesRoot, summaryRelativePath + ".summary.json");
        return summaryFullPath;
    }

    public async Task SaveSummaryAsync(FileSummaryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var summaryPath = GetSummaryPath(record.SourcePath);
        var directory = Path.GetDirectoryName(summaryPath)
            ?? throw new InvalidOperationException("Failed to determine summary directory.");

        Directory.CreateDirectory(directory);

        await using var stream = File.Create(summaryPath);
        await JsonSerializer.SerializeAsync(stream, record, JsonOptions, cancellationToken);
    }

    public async Task<FileSummaryRecord?> TryLoadSummaryAsync(
        string sourceRelativePath,
        CancellationToken cancellationToken = default)
    {
        var summaryPath = GetSummaryPath(sourceRelativePath);

        if (!File.Exists(summaryPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(summaryPath);
        return await JsonSerializer.DeserializeAsync<FileSummaryRecord>(stream, JsonOptions, cancellationToken);
    }

    public async Task<bool> IsSummaryCurrentAsync(
        string sourceRelativePath,
        CancellationToken cancellationToken = default)
    {
        var record = await TryLoadSummaryAsync(sourceRelativePath, cancellationToken);
        if (record is null)
        {
            return false;
        }

        var currentMetadata = GetSourceMetadata(sourceRelativePath);

        return record.SourceLastWriteTimeUtc == currentMetadata.LastWriteTimeUtc
            && record.SourceLength == currentMetadata.Length
            && string.Equals(record.SourceHash, currentMetadata.Hash, StringComparison.OrdinalIgnoreCase);
    }

    public FileSummaryRecord CreateSummaryRecord(string sourceRelativePath, string summaryText)
    {
        if (string.IsNullOrWhiteSpace(summaryText))
        {
            throw new ArgumentException("Summary text is required.", nameof(summaryText));
        }

        var normalizedRelativePath = NormalizeSourceRelativePath(sourceRelativePath);
        var metadata = GetSourceMetadata(normalizedRelativePath);

        return new FileSummaryRecord
        {
            SourcePath = normalizedRelativePath.Replace('\\', '/'),
            SourceLastWriteTimeUtc = metadata.LastWriteTimeUtc,
            SourceLength = metadata.Length,
            SourceHash = metadata.Hash,
            GeneratedAtUtc = DateTime.UtcNow,
            SummaryText = summaryText
        };
    }

    public async Task<string> SavePlanAsync(
        string instruction,
        string planMarkdown,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planMarkdown))
        {
            throw new ArgumentException("Plan content is required.", nameof(planMarkdown));
        }

        Directory.CreateDirectory(_plansRoot);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var slug = ToSafeSlug(instruction);
        var fileName = string.IsNullOrWhiteSpace(slug)
            ? $"{timestamp}.plan.md"
            : $"{timestamp}-{slug}.plan.md";

        var fullPath = Path.Combine(_plansRoot, fileName);
        await File.WriteAllTextAsync(fullPath, planMarkdown, cancellationToken);

        return fullPath;
    }

    private (DateTime LastWriteTimeUtc, long Length, string Hash) GetSourceMetadata(string sourceRelativePath)
    {
        var normalizedRelativePath = NormalizeSourceRelativePath(sourceRelativePath);
        var sourceFullPath = GetSourceFullPath(normalizedRelativePath);

        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException("Source file does not exist.", sourceFullPath);
        }

        var fileInfo = new FileInfo(sourceFullPath);
        var hash = ComputeSha256(sourceFullPath);

        return (fileInfo.LastWriteTimeUtc, fileInfo.Length, hash);
    }

    private string GetSourceFullPath(string sourceRelativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, sourceRelativePath));

        if (!IsUnderWorkspace(fullPath))
        {
            throw new InvalidOperationException("Access outside the workspace is not allowed.");
        }

        return fullPath;
    }

    private string NormalizeSourceRelativePath(string sourceRelativePath)
    {
        if (string.IsNullOrWhiteSpace(sourceRelativePath))
        {
            throw new ArgumentException("Source relative path is required.", nameof(sourceRelativePath));
        }

        if (Path.IsPathRooted(sourceRelativePath))
        {
            throw new InvalidOperationException("Absolute source paths are not allowed.");
        }

        var normalized = sourceRelativePath.Replace('\\', '/');

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

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string ToSafeSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}