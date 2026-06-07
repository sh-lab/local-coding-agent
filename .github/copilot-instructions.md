# Copilot instructions for `local-coding-agent`

## Build, test, and run

- Build the app with `dotnet build src/LocalCodingAgent.App/LocalCodingAgent.App.csproj`.
- Run the CLI with `dotnet run --project src/LocalCodingAgent.App -- <command>`.
- Common commands are documented in `README.md`: `read-file`, `list-files`, `summarize-file`, `summarize-all --dir <path>`, `create-plan --dir <path> "<instruction>"`, `approve-plan <plan-id-or-path>`, and `execute-plan [--dry-run]`.
- There is currently no separate test project in the repository. `dotnet test src/LocalCodingAgent.App/LocalCodingAgent.App.csproj` only validates the app project build and does not run repository tests.
- There is no dedicated lint command or formatter config checked in.

## Runtime configuration

- The app is a single .NET 10 console project at `src/LocalCodingAgent.App`.
- Configuration is loaded from `appsettings.json`, optional `appsettings.Local.json`, and environment variables prefixed with `LOCALCODINGAGENT_`.
- Runtime config is **not** read from the repo root by default. `Program.cs` resolves the config directory from `--config-dir`, then `LOCALCODINGAGENT_CONFIG_DIR`, and otherwise falls back to `~/Library/Application Support/local-coding-agent` on macOS.
- `appsettings.Local.json` is ignored by git. `.ai-work/` is also ignored and is the expected location for generated summaries, plans, and sandbox output.

## High-level architecture

- `Program.cs` is the composition root and command dispatcher. It loads config, instantiates services directly, creates three specialized AI agents (summarizer, planner, implementation), and routes commands through an `if`/`return` command chain rather than a DI container or subcommand framework.
- Workspace access is intentionally constrained:
  - `WorkspaceFileReader` reads relative paths only, rejects path traversal and absolute paths, enforces an allowed extension list, and truncates numbered output for agent consumption.
  - `WorkspaceFileLister` applies the same workspace boundary rules plus excluded directories and max-file limits.
  - `ExecutionOutputStore` enforces the same path safety rules when writing generated files under `.ai-work/output/<plan-id>/`.
- The agent workflow is artifact-driven:
  1. `FileSummaryService` reads a file, asks the summarizer for a **Japanese factual summary**, and stores a metadata-backed summary JSON in `.ai-work/summaries/...`.
  2. `PlanService` lists files, ensures summaries exist/current, then asks the planner for a **Japanese Markdown plan** and saves `plan.md` plus `meta.json` under `.ai-work/plans/pending/<plan-id>/`.
  3. `AiArtifactStore` owns plan state transitions: `pending -> in-progress -> completed`.
  4. `PlanExecutionService` parses the approved `plan.md`, extracts the expected sections, checks `Planned Output Files`, and blocks execution if an existing target file is unreadable or its summary is stale.
  5. `PlanOutputGenerationService` generates **full-file content** one target at a time and writes sandbox results plus `manifest.json` under `.ai-work/output/<plan-id>/`.
- Repository-wide Copilot instructions are part of the product flow, not just contributor docs: `CopilotInstructionsProvider` loads `.github/copilot-instructions.md`, and both `create-plan` and `execute-plan` pass its content into the AI prompts.

## Key repository conventions

- Keep planner and execution behavior in sync. `PlanService` defines the required plan shape, and `PlanExecutionService` parses it by exact section names:
  - `Goal`
  - `Confirmed Facts`
  - `Relevant Files`
  - `Planned Output Files`
  - `Proposed Minimal Changes`
  - `Optional Future Refactors`
  - `Risks / Unknowns`
  - `User Approval Checklist`
- The `Planned Output Files` section must remain a Markdown table with `Path`, `Kind`, and `Reason`, and `Kind` must stay `modified` or `new`. If you change the planner prompt format, update the parser in `PlanExecutionService` in the same change.
- The codebase assumes minimal, reviewable changes. Planner instructions explicitly bias toward small edits, especially keeping work inside `Program.cs` before introducing new types or broader restructuring.
- Prompts and generated planning/summarization content are intentionally Japanese. Preserve that unless there is a deliberate product decision to change the output language across the pipeline.
- Existing-file execution depends on summary freshness. If a change affects how summaries are generated, stored, or validated, check the interaction between `FileSummaryService`, `AiArtifactStore.IsSummaryCurrentAsync`, and `PlanExecutionService`.
- Keep the allowed-extension and workspace-boundary rules aligned across `WorkspaceFileReader`, `WorkspaceFileLister`, `ExecutionOutputStore`, and the corresponding settings in `appsettings.json`. Divergence there changes what the agent can inspect or generate.
- The product currently generates sandbox output only. Do not add logic that writes AI-generated code directly back into the workspace unless that behavior is intentionally being introduced end-to-end.
