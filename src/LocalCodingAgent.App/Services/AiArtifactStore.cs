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
    private readonly string _pendingPlansRoot;
    private readonly string _inProgressPlansRoot;
    private readonly string _completedPlansRoot;
    private readonly StringComparison _pathComparison;
    private readonly string _outputRoot;

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

        var pendingPlansDirectory = string.IsNullOrWhiteSpace(options.PendingPlansDirectory)
            ? "pending"
            : options.PendingPlansDirectory.Trim();

        var inProgressPlansDirectory = string.IsNullOrWhiteSpace(options.InProgressPlansDirectory)
            ? "in-progress"
            : options.InProgressPlansDirectory.Trim();

        var completedPlansDirectory = string.IsNullOrWhiteSpace(options.CompletedPlansDirectory)
            ? "completed"
            : options.CompletedPlansDirectory.Trim();

        _artifactRoot = Path.GetFullPath(Path.Combine(_workspaceRoot, rootDirectory));
        _summariesRoot = Path.GetFullPath(Path.Combine(_artifactRoot, summariesDirectory));

        _plansRoot = Path.GetFullPath(Path.Combine(_artifactRoot, plansDirectory));
        _pendingPlansRoot = Path.GetFullPath(Path.Combine(_plansRoot, pendingPlansDirectory));
        _inProgressPlansRoot = Path.GetFullPath(Path.Combine(_plansRoot, inProgressPlansDirectory));
        _completedPlansRoot = Path.GetFullPath(Path.Combine(_plansRoot, completedPlansDirectory));

        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
        ? "output"
        : options.OutputDirectory.Trim();

        _outputRoot = Path.GetFullPath(Path.Combine(_artifactRoot, outputDirectory));

        if (!IsUnderWorkspace(_artifactRoot) ||
            !IsUnderWorkspace(_summariesRoot) ||
            !IsUnderWorkspace(_plansRoot) ||
            !IsUnderWorkspace(_pendingPlansRoot) ||
            !IsUnderWorkspace(_inProgressPlansRoot) ||
            !IsUnderWorkspace(_completedPlansRoot) ||
            !IsUnderWorkspace(_outputRoot))
        {
            throw new InvalidOperationException("Artifact directories must stay inside the workspace.");
        }
    }

    public string WorkspaceRoot => _workspaceRoot;
    public string ArtifactRoot => _artifactRoot;
    public string SummariesRoot => _summariesRoot;
    public string PlansRoot => _plansRoot;
    public string PendingPlansRoot => _pendingPlansRoot;
    public string InProgressPlansRoot => _inProgressPlansRoot;
    public string CompletedPlansRoot => _completedPlansRoot;
    public string OutputRoot => _outputRoot;
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

        Directory.CreateDirectory(_pendingPlansRoot);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var slug = ToSafeSlug(instruction);
        var planId = string.IsNullOrWhiteSpace(slug)
            ? timestamp
            : $"{timestamp}-{slug}";

        var planDirectory = Path.Combine(_pendingPlansRoot, planId);
        Directory.CreateDirectory(planDirectory);

        var planPath = Path.Combine(planDirectory, "plan.md");
        var metadataPath = Path.Combine(planDirectory, "meta.json");

        await File.WriteAllTextAsync(planPath, planMarkdown, cancellationToken);

        var metadata = new PlanMetadata
        {
            PlanId = planId,
            Instruction = instruction,
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = null
        };

        await using var stream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);

        return planPath;
    }

    public async Task<(string PlanId, string PlanDirectory, string PlanPath)> ApprovePlanAsync(
        string planFileOrPlanId,
        CancellationToken cancellationToken = default)
    {
        var sourcePlanDirectory = ResolvePendingPlanDirectory(planFileOrPlanId);
        var sourcePlanPath = Path.Combine(sourcePlanDirectory, "plan.md");
        var sourceMetadataPath = Path.Combine(sourcePlanDirectory, "meta.json");

        if (!File.Exists(sourcePlanPath))
        {
            throw new FileNotFoundException("plan.md was not found.", sourcePlanPath);
        }

        if (!File.Exists(sourceMetadataPath))
        {
            throw new FileNotFoundException("meta.json was not found.", sourceMetadataPath);
        }

        Directory.CreateDirectory(_inProgressPlansRoot);

        var existingInProgressPlans = Directory.EnumerateDirectories(_inProgressPlansRoot).ToArray();
        if (existingInProgressPlans.Length > 0)
        {
            throw new InvalidOperationException(
                $"An in-progress plan already exists: {Path.GetFileName(existingInProgressPlans[0])}");
        }

        var metadata = await LoadPlanMetadataAsync(sourceMetadataPath, cancellationToken)
            ?? throw new InvalidOperationException("Failed to load plan metadata.");

        metadata.Status = "in-progress";
        metadata.ApprovedAtUtc = DateTime.UtcNow;

        await using (var stream = File.Create(sourceMetadataPath))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
        }

        var destinationPlanDirectory = Path.Combine(_inProgressPlansRoot, metadata.PlanId);

        if (Directory.Exists(destinationPlanDirectory))
        {
            throw new InvalidOperationException("The destination in-progress plan directory already exists.");
        }

        Directory.Move(sourcePlanDirectory, destinationPlanDirectory);

        return (
            metadata.PlanId,
            destinationPlanDirectory,
            Path.Combine(destinationPlanDirectory, "plan.md"));
    }

    public async Task<(string PlanId, string PlanDirectory, string PlanPath)> CompleteCurrentInProgressPlanAsync(
    CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_inProgressPlansRoot);
        Directory.CreateDirectory(_completedPlansRoot);

        var existingInProgressPlans = Directory.EnumerateDirectories(_inProgressPlansRoot).ToArray();

        if (existingInProgressPlans.Length == 0)
        {
            throw new InvalidOperationException("No in-progress plan exists.");
        }

        if (existingInProgressPlans.Length > 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one in-progress plan, but found {existingInProgressPlans.Length}.");
        }

        var sourcePlanDirectory = existingInProgressPlans[0];
        var sourcePlanPath = Path.Combine(sourcePlanDirectory, "plan.md");
        var sourceMetadataPath = Path.Combine(sourcePlanDirectory, "meta.json");

        if (!File.Exists(sourcePlanPath))
        {
            throw new FileNotFoundException("plan.md was not found.", sourcePlanPath);
        }

        if (!File.Exists(sourceMetadataPath))
        {
            throw new FileNotFoundException("meta.json was not found.", sourceMetadataPath);
        }

        var metadata = await LoadPlanMetadataAsync(sourceMetadataPath, cancellationToken)
            ?? throw new InvalidOperationException("Failed to load plan metadata.");

        metadata.Status = "completed";
        metadata.CompletedAtUtc = DateTime.UtcNow;

        await using (var stream = File.Create(sourceMetadataPath))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
        }

        var destinationPlanDirectory = Path.Combine(_completedPlansRoot, metadata.PlanId);

        if (Directory.Exists(destinationPlanDirectory))
        {
            throw new InvalidOperationException("The destination completed plan directory already exists.");
        }

        Directory.Move(sourcePlanDirectory, destinationPlanDirectory);

        return (
            metadata.PlanId,
            destinationPlanDirectory,
            Path.Combine(destinationPlanDirectory, "plan.md"));
    }

    public async Task<InProgressPlanInfo?> TryGetCurrentInProgressPlanAsync(
    CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_inProgressPlansRoot);

        var planDirectories = Directory.EnumerateDirectories(_inProgressPlansRoot).ToArray();

        if (planDirectories.Length == 0)
        {
            return null;
        }

        if (planDirectories.Length > 1)
        {
            throw new InvalidOperationException(
                $"Expected zero or one in-progress plan, but found {planDirectories.Length}.");
        }

        var planDirectory = planDirectories[0];
        var planPath = Path.Combine(planDirectory, "plan.md");
        var metadataPath = Path.Combine(planDirectory, "meta.json");

        if (!File.Exists(planPath))
        {
            throw new FileNotFoundException("plan.md was not found.", planPath);
        }

        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("meta.json was not found.", metadataPath);
        }

        var metadata = await LoadPlanMetadataAsync(metadataPath, cancellationToken)
            ?? throw new InvalidOperationException("Failed to load plan metadata.");

        var planMarkdown = await File.ReadAllTextAsync(planPath, cancellationToken);

        return new InProgressPlanInfo
        {
            PlanId = metadata.PlanId,
            PlanDirectory = planDirectory,
            PlanPath = planPath,
            PlanMarkdown = planMarkdown,
            Metadata = metadata
        };
    }

    private async Task<PlanMetadata?> LoadPlanMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<PlanMetadata>(stream, JsonOptions, cancellationToken);
    }

    private string ResolvePendingPlanDirectory(string planFileOrPlanId)
    {
        if (string.IsNullOrWhiteSpace(planFileOrPlanId))
        {
            throw new ArgumentException("Plan identifier is required.", nameof(planFileOrPlanId));
        }

        Directory.CreateDirectory(_pendingPlansRoot);

        if (File.Exists(planFileOrPlanId))
        {
            var fullPath = Path.GetFullPath(planFileOrPlanId);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Failed to determine plan directory.");

            if (!IsUnderDirectory(directory, _pendingPlansRoot))
            {
                throw new InvalidOperationException("The specified plan file is not under pending plans.");
            }

            return directory;
        }

        if (Directory.Exists(planFileOrPlanId))
        {
            var fullPath = Path.GetFullPath(planFileOrPlanId);

            if (!IsUnderDirectory(fullPath, _pendingPlansRoot))
            {
                throw new InvalidOperationException("The specified plan directory is not under pending plans.");
            }

            return fullPath;
        }

        var directDirectory = Path.Combine(_pendingPlansRoot, planFileOrPlanId);
        if (Directory.Exists(directDirectory))
        {
            return directDirectory;
        }

        var directPlanFile = Path.Combine(_pendingPlansRoot, planFileOrPlanId);
        if (File.Exists(directPlanFile))
        {
            var directory = Path.GetDirectoryName(directPlanFile)
                ?? throw new InvalidOperationException("Failed to determine plan directory.");

            return directory;
        }

        throw new FileNotFoundException("The specified pending plan could not be found.", planFileOrPlanId);
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