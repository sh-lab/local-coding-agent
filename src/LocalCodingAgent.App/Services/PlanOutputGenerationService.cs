using Microsoft.Agents.AI;

namespace LocalCodingAgent.App.Services;

public sealed class PlanOutputGenerationService
{
    private readonly PlanExecutionService _executionService;
    private readonly WorkspaceFileReader _fileReader;
    private readonly AiArtifactStore _artifactStore;
    private readonly ExecutionOutputStore _outputStore;
    private readonly AIAgent _implementationAgent;
    private readonly CopilotInstructionsContext? _copilotInstructions;

    public PlanOutputGenerationService(
        PlanExecutionService executionService,
        WorkspaceFileReader fileReader,
        AiArtifactStore artifactStore,
        ExecutionOutputStore outputStore,
        AIAgent implementationAgent,
        CopilotInstructionsContext? copilotInstructions)
    {
        _executionService = executionService;
        _fileReader = fileReader;
        _artifactStore = artifactStore;
        _outputStore = outputStore;
        _implementationAgent = implementationAgent;
        _copilotInstructions = copilotInstructions;
    }

    public async Task<(PlanExecutionPreview Preview, string ManifestPath)> ExecuteToOutputAsync(
        CancellationToken cancellationToken = default)
    {
        var preview = await _executionService.GetDryRunPreviewAsync(cancellationToken);

        if (preview is null)
        {
            throw new InvalidOperationException("No in-progress plan exists.");
        }

        if (!preview.CanExecute)
        {
            var message = string.Join(Environment.NewLine, preview.BlockingReasons);
            throw new InvalidOperationException(
                $"The approved plan is not executable yet.{Environment.NewLine}{message}");
        }

        var manifest = new ExecutionOutputManifest
        {
            PlanId = preview.PlanId,
            GeneratedAtUtc = DateTime.UtcNow
        };

        foreach (var target in preview.PlannedOutputFiles)
        {
            var currentContent = string.Empty;

            if (target.Kind == "modified")
            {
                var current = _fileReader.ReadRawText(target.Path);
                if (!current.Success || current.Content is null)
                {
                    throw new InvalidOperationException(
                        $"Failed to read the full current file content for '{target.Path}': {current.ErrorMessage}");
                }

                currentContent = current.Content;
            }

            var contextSummaries = await BuildContextSummariesAsync(preview.RelevantFiles, cancellationToken);

            var generatedContent = await GenerateFileContentAsync(
                preview,
                target,
                currentContent,
                contextSummaries);

            var outputPath = await _outputStore.WriteFileAsync(
                preview.PlanId,
                target.Path,
                generatedContent,
                cancellationToken);

            manifest.Files.Add(new ExecutionOutputEntry
            {
                Path = target.Path,
                Kind = target.Kind
            });

            Console.WriteLine($"[output] {target.Kind} -> {outputPath}");
        }

        var manifestPath = await _outputStore.SaveManifestAsync(manifest, cancellationToken);
        return (preview, manifestPath);
    }

    private async Task<string> BuildContextSummariesAsync(
        IReadOnlyList<string> relevantFiles,
        CancellationToken cancellationToken)
    {
        var blocks = new List<string>();

        foreach (var file in relevantFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var summary = await _artifactStore.TryLoadSummaryAsync(file, cancellationToken);
            if (summary is null)
            {
                continue;
            }

            blocks.Add($"""
### {summary.SourcePath}
{summary.SummaryText}
""");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private async Task<string> GenerateFileContentAsync(
        PlanExecutionPreview preview,
        PlannedOutputFile target,
        string currentContent,
        string contextSummaries)
    {
        var currentContentBlock =
            target.Kind == "modified"
                ? $"""
## Current File Content
{currentContent}
"""
                : """
## Current File Content
(new file; no current content)
""";

        var copilotInstructionsBlock =
            _copilotInstructions is null
                ? "## Repository Custom Instructions\n(none)"
                : $"""
## Repository Custom Instructions
Path: {_copilotInstructions.Path}

{_copilotInstructions.Content}
""";

        var prompt = $"""
あなたはローカルのコーディングエージェントの実装アシスタントです。
以下の承認済み plan に従って、指定された1ファイルの最終的な全文だけを生成してください。

重要な制約:
- 出力はそのファイルの全文だけにする
- Markdown コードフェンスは付けない
- 解説文を付けない
- 対象ファイル以外は出力しない
- target file の kind が "modified" の場合は、現在の内容をベースに更新する
- target file の kind が "new" の場合は、新規ファイルとして完成形を出力する
- 参照情報は context summaries と repository custom instructions を使う
- plan と矛盾する実装をしない
- 不明点があっても、可能な範囲で最小の妥当な実装を出す
- 既存ファイルのスタイルや構造は、plan が要求しない限り壊さない
- Planned Output Files に含まれない新規ファイルを前提にしない
- 現在のファイルや plan に存在しない変数・型・メソッド・tuple メンバーを勝手に参照しない

## Plan Goal
{preview.Goal}

## Planned Output Target
Path: {target.Path}
Kind: {target.Kind}
Reason: {target.Reason}

## Proposed Minimal Changes
{string.Join(Environment.NewLine, preview.ProposedMinimalChanges.Select(x => "- " + x))}

{copilotInstructionsBlock}

{currentContentBlock}

## Context Summaries
{contextSummaries}
""";

        var response = await _implementationAgent.RunAsync(prompt);
        var text = response?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                $"The implementation agent returned empty content for '{target.Path}'.");
        }

        return StripCodeFence(text);
    }

    private static string StripCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed
            .Split(Environment.NewLine)
            .ToList();

        if (lines.Count > 0 && lines[0].StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }
}