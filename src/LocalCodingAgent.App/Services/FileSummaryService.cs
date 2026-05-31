using Microsoft.Agents.AI;

namespace LocalCodingAgent.App.Services;

public sealed class FileSummaryService
{
    private readonly WorkspaceFileReader _reader;
    private readonly AiArtifactStore _artifactStore;
    private readonly AIAgent _summarizerAgent;

    public FileSummaryService(
        WorkspaceFileReader reader,
        AiArtifactStore artifactStore,
        AIAgent summarizerAgent)
    {
        _reader = reader;
        _artifactStore = artifactStore;
        _summarizerAgent = summarizerAgent;
    }

    public async Task<FileSummaryRecord> GetOrCreateSummaryAsync(
        string sourceRelativePath,
        CancellationToken cancellationToken = default)
    {
        if (await _artifactStore.IsSummaryCurrentAsync(sourceRelativePath, cancellationToken))
        {
            var existing = await _artifactStore.TryLoadSummaryAsync(sourceRelativePath, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        var readResult = _reader.Read(sourceRelativePath);
        if (!readResult.Success)
        {
            throw new InvalidOperationException(readResult.ErrorMessage ?? "Failed to read source file.");
        }

        var prompt = BuildSummaryPrompt(sourceRelativePath, readResult);
        var summaryResponse = await _summarizerAgent.RunAsync(prompt);
        var summaryText = summaryResponse?.ToString()?.Trim();

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            throw new InvalidOperationException("The summarizer agent returned an empty summary.");
        }

        var record = _artifactStore.CreateSummaryRecord(sourceRelativePath, summaryText);
        await _artifactStore.SaveSummaryAsync(record, cancellationToken);

        return record;
    }

    private static string BuildSummaryPrompt(string sourceRelativePath, WorkspaceFileReadResult readResult)
    {
        var truncationNote = readResult.Truncated
            ? $"注意: この入力は一部のみです。返されているのは {readResult.ReturnedLines}/{readResult.TotalLines} 行、{readResult.ReturnedCharacters}/{readResult.TotalCharacters} 文字です。"
            : "注意: この入力はファイル全文です。";

        return $"""
あなたはローカルのコーディングエージェント用の要約作成アシスタントです。
以下のファイル内容を、日本語で簡潔かつ事実ベースで要約してください。

要件:
- 推測を書かない
- 実際に読み取れる内容だけを書く
- まず責務を短くまとめる
- そのあとに重要な依存関係・設定・主要な振る舞いを書く
- 変更対象を判断する参考になるように書く
- 箇条書きを使ってよい
- 冗長にしすぎない
- 10行前後を目安にする

対象ファイル:
{sourceRelativePath}

{truncationNote}

ファイル内容:
{readResult.Content}
""";
    }
}
