using System.Text.RegularExpressions;

namespace LocalCodingAgent.App.Services;

public sealed class PlanExecutionService
{
    private readonly AiArtifactStore _artifactStore;
    private readonly WorkspaceFileReader _fileReader;

    public PlanExecutionService(
        AiArtifactStore artifactStore,
        WorkspaceFileReader fileReader)
    {
        _artifactStore = artifactStore;
        _fileReader = fileReader;
    }

    public async Task<PlanExecutionPreview?> GetDryRunPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var currentPlan = await _artifactStore.TryGetCurrentInProgressPlanAsync(cancellationToken);

        if (currentPlan is null)
        {
            return null;
        }

        var sections = ParseSections(currentPlan.PlanMarkdown);

        var relevantFiles = ExtractRelevantFiles(sections);
        var reconfirmedTargetFiles = await ReconfirmTargetFilesAsync(relevantFiles, cancellationToken);
        var blockingReasons = BuildBlockingReasons(relevantFiles, reconfirmedTargetFiles);

        return new PlanExecutionPreview
        {
            PlanId = currentPlan.PlanId,
            PlanPath = currentPlan.PlanPath,
            Goal = ExtractGoal(sections),
            RelevantFiles = relevantFiles,
            ReconfirmedTargetFiles = reconfirmedTargetFiles,
            ProposedMinimalChanges = ExtractNumberedOrBulletedItems(sections, "Proposed Minimal Changes"),
            RisksOrUnknowns = ExtractListItems(sections, "Risks / Unknowns"),
            ApprovalChecklist = ExtractChecklistItems(sections, "User Approval Checklist"),
            BlockingReasons = blockingReasons,
            CanExecute = blockingReasons.Count == 0,
            RawPlanMarkdown = currentPlan.PlanMarkdown
        };
    }

    private async Task<IReadOnlyList<PlanTargetFileStatus>> ReconfirmTargetFilesAsync(
        IReadOnlyList<string> relevantFiles,
        CancellationToken cancellationToken)
    {
        var results = new List<PlanTargetFileStatus>();

        foreach (var file in relevantFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var readResult = _fileReader.Read(file);

            var exists = !string.Equals(readResult.ErrorMessage, "File does not exist.", StringComparison.OrdinalIgnoreCase);
            var readable = readResult.Success;
            var summaryIsCurrent = false;

            if (exists)
            {
                try
                {
                    summaryIsCurrent = await _artifactStore.IsSummaryCurrentAsync(file, cancellationToken);
                }
                catch
                {
                    summaryIsCurrent = false;
                }
            }

            var note = readResult.Success
                ? "OK"
                : readResult.ErrorMessage ?? "Unknown error";

            results.Add(new PlanTargetFileStatus
            {
                SourcePath = file,
                Exists = exists,
                Readable = readable,
                SummaryIsCurrent = summaryIsCurrent,
                Note = note
            });
        }

        return results;
    }

    private static Dictionary<string, List<string>> ParseSections(string markdown)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? currentSection = null;

        foreach (var rawLine in markdown.Split(Environment.NewLine))
        {
            var line = rawLine.TrimEnd();

            var sectionName = TryGetSectionName(line);
            if (sectionName is not null)
            {
                currentSection = sectionName;
                if (!sections.ContainsKey(sectionName))
                {
                    sections[sectionName] = new List<string>();
                }
                continue;
            }

            if (currentSection is not null)
            {
                sections[currentSection].Add(line);
            }
        }

        return sections;
    }

    private static string? TryGetSectionName(string line)
    {
        var normalized = line.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = normalized.TrimStart('#', ' ', '\t').Trim();
        normalized = normalized.TrimStart('#', ' ', '\t').Trim();

        var knownSections = new[]
        {
            "Goal",
            "Confirmed Facts",
            "Relevant Files",
            "Proposed Minimal Changes",
            "Optional Future Refactors",
            "Risks / Unknowns",
            "User Approval Checklist"
        };

        foreach (var section in knownSections)
        {
            if (normalized.StartsWith(section, StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }

    private static string ExtractGoal(Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("Goal", out var lines))
        {
            return string.Empty;
        }

        var contentLines = lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.TrimStart('-', ' ', '\t'))
            .ToArray();

        return string.Join(" ", contentLines).Trim();
    }

    private static IReadOnlyList<string> ExtractRelevantFiles(Dictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("Relevant Files", out var lines))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Markdown table separator / header lines
            if (line.StartsWith("|---", StringComparison.Ordinal) ||
                line.StartsWith("| ファイル", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Markdown table row
            if (line.StartsWith("|", StringComparison.Ordinal))
            {
                // 最初の列から `path` を優先抽出
                var matches = Regex.Matches(line, @"`([^`]+)`");
                if (matches.Count > 0)
                {
                    var path = matches[0].Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        results.Add(path);
                        continue;
                    }
                }

                var cells = line.Split('|', StringSplitOptions.TrimEntries);
                if (cells.Length >= 2)
                {
                    var candidate = cells[1].Trim().Trim('`');
                    if (LooksLikePath(candidate))
                    {
                        results.Add(candidate);
                        continue;
                    }
                }
            }

            // Fallback for bullet list
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var candidate = line[2..].Trim().Trim('`');
                if (LooksLikePath(candidate))
                {
                    results.Add(candidate);
                }
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikePath(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return candidate.Contains('/', StringComparison.Ordinal) ||
               candidate.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".h", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase) ||
               candidate.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractListItems(
        Dictionary<string, List<string>> sections,
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var lines))
        {
            return Array.Empty<string>();
        }

        return lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => x.StartsWith("- ") || x.StartsWith("* "))
            .Select(x => x[2..].Trim())
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractChecklistItems(
        Dictionary<string, List<string>> sections,
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var lines))
        {
            return Array.Empty<string>();
        }

        return lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Where(x => x.StartsWith("- ["))
            .Select(x => x.TrimStart('-', ' ').Trim())
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractNumberedOrBulletedItems(
        Dictionary<string, List<string>> sections,
        string sectionName)
    {
        if (!sections.TryGetValue(sectionName, out var lines))
        {
            return Array.Empty<string>();
        }

        var items = new List<string>();

        foreach (var line in lines.Select(x => x.Trim()))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                items.Add(line[2..].Trim());
                continue;
            }

            if (Regex.IsMatch(line, @"^\d+\.\s+"))
            {
                items.Add(Regex.Replace(line, @"^\d+\.\s+", "").Trim());
            }
        }

        return items;
    }

    private static IReadOnlyList<string> BuildBlockingReasons(
    IReadOnlyList<string> relevantFiles,
    IReadOnlyList<PlanTargetFileStatus> reconfirmedTargetFiles)
    {
        var reasons = new List<string>();

        if (relevantFiles.Count == 0)
        {
            reasons.Add("Relevant Files が空のため、実行対象ファイルを特定できません。");
            return reasons;
        }

        foreach (var file in reconfirmedTargetFiles)
        {
            if (!file.Exists)
            {
                reasons.Add($"対象ファイルが存在しません: {file.SourcePath}");
                continue;
            }

            if (!file.Readable)
            {
                reasons.Add($"対象ファイルを読み取れません: {file.SourcePath} ({file.Note})");
                continue;
            }

            if (!file.SummaryIsCurrent)
            {
                reasons.Add($"対象ファイルの summary が最新ではありません: {file.SourcePath}");
            }
        }

        return reasons;
    }
}