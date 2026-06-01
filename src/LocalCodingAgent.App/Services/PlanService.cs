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
       CopilotInstructionsContext? copilotInstructions = null,
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

        var prompt = BuildPlanPrompt(
            instruction,
            summaries,
            listResult.Truncated,
            listResult.TotalFiles,
            listResult.ReturnedFiles,
            copilotInstructions);

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
        int returnedFiles,
        CopilotInstructionsContext? copilotInstructions)
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

        var copilotInstructionsBlock =
            copilotInstructions is null
                ? "## Repository Custom Instructions\n(none)"
                : $"""
## Repository Custom Instructions
Path: {copilotInstructions.Path}

{copilotInstructions.Content}
""";

        return $"""
あなたはローカルのコーディングエージェント用の作業計画アシスタントです。
以下の「ユーザー指示」と「ファイルサマリー」をもとに、まだ実行せず、作業計画だけを作ってください。

最重要ルール:
- サマリーに根拠がないことを事実として断定しない
- 既存の型・メソッド・クラス・設定キー・コマンドは、サマリーに根拠があるものだけを「既存」として扱う
- 推測が入る場合は、必ず「確認事項」または「仮定」に書く
- 新規追加する案は、必ず「提案」として書く
- まず「最小変更案」を優先する
- 大きなリファクタ案は「将来案」または「第二段階」として分ける
- 実行はしない
- ユーザー承認のための計画を書く
- Relevant Files に同じファイルを重複して出さない
- Relevant Files は最大 8 件までにする
- Proposed Minimal Changes では、まず Program.cs 内で完結する関数分割や小さな抽出を優先する
- 新規クラス追加や大きな構造変更は、必要なら Optional Future Refactors に分離する
- 既存の型・メソッド・設定キーは、サマリーに根拠があるものだけを書く
- Confirmation Items は今回の変更に直接関係する確認事項だけを書く
- Proposed Minimal Changes は最大 5 項目までにする
- Optional Future Refactors は 0 件でもよい
- Relevant Files には、既に存在するファイルだけを書く
- 新規追加候補のファイルは Relevant Files に入れない
- 新規追加候補は Planned Output Files にだけ書く
- Relevant Files は、サマリーまたは既存ファイルとして根拠があるものだけを書く
- Planned Output Files の Kind が "new" のファイルは、Relevant Files に含めない

出力ルール:
- 日本語で書く
- Markdown で出力する
- 以下の見出しをこの順番で必ず含める

# Goal
- ユーザー指示を短く言い換えた目的を書く

# Confirmed Facts
- サマリーから明確に読み取れる事実だけを書く
- ファイル名や責務は、サマリーに根拠があるものだけ書く

# Relevant Files
- 今回の作業の理解や影響確認に使う、既に存在するファイルだけを書く
- 存在しないファイルや新規追加候補は書かない
- 各ファイルについて「なぜ関係があるか」を短く書く
- サマリーに根拠があるものだけを書く

# Planned Output Files
- 実際に output に書き出す対象ファイルだけを書く
- Path, Kind, Reason の3列を持つ Markdown table で書く
- Kind は modified または new のどちらかに限定する
- new のファイルは新規作成候補としてここにだけ書く
- Planned Output Files に書かれたファイルだけを実行対象とする

# Proposed Minimal Changes
- 最小変更で実現する案を書く
- 既存コードを大きく壊さない順序で書く
- 変更単位はレビューしやすくする

# Optional Future Refactors
- 今回はやらなくてもよいが、将来的に有効な改善があれば書く
- ここには大きな設計変更を書いてよい
- ただし、今回の必須変更と混ぜない

# Risks / Unknowns
- サマリーだけでは断定できない点を書く
- 不明な点、要確認な点、推測が必要な点を書く

# User Approval Checklist
- ユーザーが承認前に確認すべき項目を箇条書きで書く

禁止事項:
- 存在未確認の型・メソッド・設定キーを、既に存在するもののように書かない
- 「〜のはず」「〜と思われる」を事実のように書かない
- いきなり全面的なDI化、全面的な責務分離、全面書き換えを唯一案として出さない
- 実行手順や変更結果を完了済みのように書かない
- 実際に出力しないファイルを Planned Output Files に含めない
- 存在しないファイルを Relevant Files に含めない
- 新規追加候補を Relevant Files に含めない
- Planned Output Files に書かれていないファイルを実出力対象として扱わない

{truncationNote}

{copilotInstructionsBlock}

ユーザー指示:
{instruction}

ファイルサマリー:
{summariesText}
""";
    }
}