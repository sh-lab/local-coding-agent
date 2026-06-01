using System.Text.RegularExpressions;

namespace LocalCodingAgent.App.Services;

public sealed class PlanExecutionService
{
    private readonly AiArtifactStore _artifactStore;

    public PlanExecutionService(AiArtifactStore artifactStore)
    {
        _artifactStore = artifactStore;
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

        return new PlanExecutionPreview
        {
            PlanId = currentPlan.PlanId,
            PlanPath = currentPlan.PlanPath,
            Goal = ExtractGoal(sections),
            RelevantFiles = ExtractListItems(sections, "Relevant Files"),
            ProposedMinimalChanges = ExtractNumberedOrBulletedItems(sections, "Proposed Minimal Changes"),
            RisksOrUnknowns = ExtractListItems(sections, "Risks / Unknowns"),
            ApprovalChecklist = ExtractChecklistItems(sections, "User Approval Checklist"),
            RawPlanMarkdown = currentPlan.PlanMarkdown
        };
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

        // 見出しの # や空白を雑に除去して判定
        normalized = normalized.TrimStart('#', ' ', '\t').Trim();

        // さらに "## # Goal" のような崩れに強くする
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
}