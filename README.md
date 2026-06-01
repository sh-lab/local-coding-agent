# local-coding-agent

ローカル LLM を使って、コードベースの要約、作業 plan の作成、承認済み plan に基づく sandbox 出力の生成を行う実験的なコーディングエージェントです。

このプロジェクトの主な目的は、次の 2 点です。

- ローカル環境で動作するコーディングエージェントのワークフローを段階的に構築すること
- GitHub Copilot などの支援ツールに毎回同じ前提を伝えるコストを下げること

**現在の状態: 開発中 (Work in Progress)**

まだ検証段階のため、構成や挙動は今後変更される可能性があります。

## License

MIT

## 概要

`local-coding-agent` は、ローカルで動く LLM を利用して、次の流れを段階的に実現するためのプロジェクトです。

1. コードベースのサマリーを作る
2. サマリーとユーザー指示から作業 plan を作る
3. 人間が plan を確認し、必要なら修正する
4. plan を承認する
5. 承認済み plan に基づいて sandbox 出力を生成する

現時点では、**workspace 本体は直接変更しません。**  
生成結果はまず `./.ai-work/output/<plan-id>/...` に出力されます。

## 現在できること

- workspace 内のファイルを安全に読む
- workspace 内の読み取り可能ファイルを一覧する
- ファイルのサマリーを生成して保存する
- サマリーから作業 plan を生成する
- plan を承認して `in-progress` に移す
- `execute-plan --dry-run` で承認済み plan の実行前確認を行う
- `execute-plan` で承認済み plan から sandbox 出力を生成する
- 実行成功後、plan を `completed` に移す
- `.github/copilot-instructions.md` を読み、plan 作成と実装生成に反映する

## ディレクトリ構成

AI 関連の生成物は `.ai-work` 配下に保存されます。

- `.ai-work/summaries/`  
  ファイルごとのサマリー

- `.ai-work/plans/pending/`  
  未承認の plan

- `.ai-work/plans/in-progress/`  
  承認済みで現在の処理対象になっている plan

- `.ai-work/plans/completed/`  
  実行完了した plan

- `.ai-work/output/<plan-id>/`  
  実行結果の sandbox 出力

- `.ai-work/output/<plan-id>/manifest.json`  
  生成したファイル一覧

## 使い方

### 前提条件

- .NET SDK がインストールされていること
- Ollama がローカルで動作していること
- `appsettings.json` または `appsettings.Local.json` に必要な設定が含まれていること

最低限、次の設定が必要です。

- `Ollama:Endpoint`
- `Ollama:ModelName`
- `Ollama:RequestTimeoutSeconds`
- `WorkspaceFileReader`
- `WorkspaceFileListing`
- `AiArtifactStore`

### 1. ファイルを読む

workspace 内のファイルを読みます。


dotnet run --project src/LocalCodingAgent.App -- read-file src/LocalCodingAgent.App/Program.cs


### 2. ファイル一覧を取得する

読み取り対象のファイル一覧を取得します。


dotnet run --project src/LocalCodingAgent.App -- list-files src


### 3. 単一ファイルのサマリーを作る


dotnet run --project src/LocalCodingAgent.App -- summarize-file src/LocalCodingAgent.App/Program.cs


### 4. ディレクトリ配下のサマリーをまとめて作る


dotnet run --project src/LocalCodingAgent.App -- summarize-all --dir src


### 5. 作業 plan を作る

ユーザー指示と既存サマリーをもとに plan を作成します。


dotnet run --project src/LocalCodingAgent.App -- create-plan --dir src "Program.cs の責務を整理して構成読込とCLI分岐を分離したい"


生成された plan は `pending` 配下に保存されます。

### 6. plan を確認・必要なら修正する

生成された `plan.md` をエディタで直接開いて確認します。  
必要なら手で修正してから承認します。

例:


code .ai-work/plans/pending/plan.md


### 7. plan を承認する

承認すると、plan は `in-progress` に移動します。


dotnet run --project src/LocalCodingAgent.App -- approve-plan ""


または `plan.md` のパスを直接指定しても構いません。

### 8. 実行前確認をする

承認済み plan に対して dry-run を行います。


dotnet run --project src/LocalCodingAgent.App -- execute-plan --dry-run


### 9. sandbox 出力を生成する

承認済み plan に基づいて、変更対象ファイルまたは新規ファイルを  
`./.ai-work/output/<plan-id>/...` に生成します。


dotnet run --project src/LocalCodingAgent.App -- execute-plan


成功すると、plan は `completed` に移動します。

## 現在のワークフロー

現在の運用フローは次のとおりです。

1. サマリーを作成する
2. plan を作成する
3. 人間が plan を確認する
4. 必要なら plan を修正する
5. plan を承認する
6. dry-run で実行前確認をする
7. sandbox 出力を生成する
8. 成功した plan を `completed` に移す

## `.github/copilot-instructions.md` について

このプロジェクトは、repository-wide の GitHub Copilot custom instructions である  
`.github/copilot-instructions.md` を読むことを想定しています。

このファイルが存在する場合、現在は主に次の用途で利用します。

- `create-plan` 実行時の plan 生成
- `execute-plan` 実行時の実装生成

これにより、プロジェクト固有のルールやコーディング方針を、plan 作成と実装生成の両方に反映しやすくなります。

## 現在の制限

- まだ **開発中** のプロジェクトです
- workspace 本体は直接変更しません
- 生成結果は sandbox 出力として `.ai-work/output` に保存されます
- 生成コードはそのまま採用せず、人間のレビュー前提です
- `.github/copilot-instructions.md` には対応していますが、`.instructions.md` や `AGENTS.md` の完全対応は未実装です
- 自動 diff 適用や workspace 本体への反映は未実装です
- 生成品質は使用モデルと prompt 設計に強く依存します

## 今後の予定

今後は、次のような改善を検討しています。

- 生成 output と元ファイルの差分確認をしやすくする
- sandbox output を安全に workspace 本体へ反映する仕組みを作る
- 実行対象ファイルや生成制約の精度を高める
- より多くの repository instructions / project instructions に対応する
- 他プロジェクトでの適用検証を進める

## 注意

このプロジェクトは、ローカル LLM と AI 支援ツールを使った開発ワークフローの**実験的実装**です。  
生成された output は、必ず人間がレビューしてから扱ってください。