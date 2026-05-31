using Microsoft.Agents.AI;

namespace LocalCodingAgent.App.Services;

public sealed class PlanService
{
    private readonly WorkspaceFileLister _fileLister;
    private readonly FileSummaryService _summaryService;
    private readonly AiArtifactStore _artifactStore;
    private readonly AIAgent _plannerAgent;

    public PlanService(
        WorkspaceFileLister fileLister,
        FileSummaryService summaryService,
        AiArtifactStore artifactStore,
        AIAgent plannerAgent)
    {
        _fileLister = fileLister;
        _summaryService = summaryService;
        _artifactStore = artifactStore;
        _plannerAgent = plannerAgent;
    }

    public async Task<(string PlanText, string SavedPath)> CreatePlanAsync(
        string instruction,
        string directoryPath = ".",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            throw new ArgumentException("Instruction is required.", nameof(instruction));
        }

        var listResult = _fileLister.ListFiles(directoryPath);
        if (!listResult.Success)
        {
            throw new InvalidOperationException(listResult.ErrorMessage ?? "Failed to list files.");
        }

        var summaries = new List<FileSummaryRecord>();

        foreach (var file in listResult.Files)
        {
            var summary = await _summaryService.GetOrCreateSummaryAsync(file, cancellationToken);
            summaries.Add(summary);
        }

        var prompt = BuildPlanPrompt(instruction, summaries, listResult.Truncated, listResult.TotalFiles, listResult.ReturnedFiles);
        var response = await _plannerAgent.RunAsync(prompt);
        var planText = response?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(planText))
        {
            throw new InvalidOperationException("The planner agent returned an empty plan.");
        }

        var savedPath = await _artifactStore.SavePlanAsync(instruction, planText, cancellationToken);
        return (planText, savedPath);
    }

    private static string BuildPlanPrompt(
        string instruction,
        IReadOnlyList<FileSummaryRecord> summaries,
        bool filesTruncated,
        int totalFiles,
        int returnedFiles)
    {
        var summariesText = string.Join(
            Environment.NewLine + Environment.NewLine,
            summaries.Select(summary =>
$"""
### {summary.SourcePath}
{summary.SummaryText}
"""));

        var truncationNote = filesTruncated
            ? $"注意: 対象ファイル一覧は一部のみです。{returnedFiles}/{totalFiles} ファイルについてサマリーを使っています。"
            : "注意: 対象ファイル一覧に対するサマリーを使っています。";

        return $"""
あなたはローカルのコーディングエージェント用の作業計画アシスタントです。
以下の「ユーザー指示」と「ファイルサマリー」をもとに、まだ実行せず、作業計画だけを作ってください。

要件:
- 実行はしない
- 事実ベースで書く
- サマリーに根拠がないことは断定しない
- 曖昧な点があれば「確認事項」として書く
- 日本語で書く
- Markdown で出力する
- 次の見出しを必ず含める:
  - # Goal
  - # Relevant Files
  - # Proposed Changes
  - # Risks / Unknowns
  - # User Approval Checklist

ユーザー指示:
{instruction}

{truncationNote}

ファイルサマリー:
{summariesText}
""";
    }
}