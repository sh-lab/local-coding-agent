using LocalCodingAgent.App.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .Build();

var fileReaderOptions =
    configuration.GetSection("WorkspaceFileReader").Get<WorkspaceFileReaderOptions>()
    ?? throw new InvalidOperationException("WorkspaceFileReader settings are missing.");

var fileListingOptions =
    configuration.GetSection("WorkspaceFileListing").Get<WorkspaceFileListingOptions>()
    ?? throw new InvalidOperationException("WorkspaceFileListing settings are missing.");

var artifactStoreOptions =
    configuration.GetSection("AiArtifactStore").Get<AiArtifactStoreOptions>()
    ?? throw new InvalidOperationException("AiArtifactStore settings are missing.");

var workspaceRoot = Directory.GetCurrentDirectory();
var workspaceFileReader = new WorkspaceFileReader(workspaceRoot, fileReaderOptions);
var workspaceFileLister = new WorkspaceFileLister(
    workspaceRoot,
    fileReaderOptions.AllowedExtensions,
    fileListingOptions);

var aiArtifactStore = new AiArtifactStore(workspaceRoot, artifactStoreOptions);

var readFileTool = new ReadFileTool(workspaceFileReader);
var listFilesTool = new ListFilesTool(workspaceFileLister);

if (args.Length >= 2 && string.Equals(args[0], "read-file", StringComparison.OrdinalIgnoreCase))
{
    var result = workspaceFileReader.Read(args[1]);

    if (!result.Success)
    {
        Console.Error.WriteLine($"Error: {result.ErrorMessage}");
        return;
    }

    Console.WriteLine(result.Content);

    if (result.Truncated)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"[truncated] returned {result.ReturnedLines}/{result.TotalLines} lines, " +
            $"{result.ReturnedCharacters}/{result.TotalCharacters} characters.");
    }

    return;
}

if (args.Length >= 2 && string.Equals(args[0], "list-files", StringComparison.OrdinalIgnoreCase))
{
    var result = workspaceFileLister.ListFiles(args[1]);

    if (!result.Success)
    {
        Console.Error.WriteLine($"Error: {result.ErrorMessage}");
        return;
    }

    foreach (var file in result.Files)
    {
        Console.WriteLine(file);
    }

    if (result.Truncated)
    {
        Console.WriteLine();
        Console.WriteLine($"[truncated] returned {result.ReturnedFiles}/{result.TotalFiles} files.");
    }

    return;
}

if (args.Length >= 2 && string.Equals(args[0], "summary-path", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(aiArtifactStore.GetSummaryPath(args[1]));
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "summary-status", StringComparison.OrdinalIgnoreCase))
{
    var status = await GetSummaryStatusAsync(aiArtifactStore, args[1]);
    Console.WriteLine(status);
    return;
}

var endpointValue = configuration["Ollama:Endpoint"];
var modelName = configuration["Ollama:ModelName"];
var timeoutSecondsValue = configuration["Ollama:RequestTimeoutSeconds"];

if (string.IsNullOrWhiteSpace(endpointValue))
{
    Console.Error.WriteLine("Ollama:Endpoint is not configured.");
    return;
}

if (string.IsNullOrWhiteSpace(modelName))
{
    Console.Error.WriteLine("Ollama:ModelName is not configured.");
    return;
}

var timeoutSeconds = int.TryParse(timeoutSecondsValue, out var parsedTimeoutSeconds)
    ? parsedTimeoutSeconds
    : 600;

var endpoint = new Uri(endpointValue);

var ollamaHttpClient = new HttpClient
{
    BaseAddress = endpoint,
    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
};

IChatClient chatClient = new OllamaApiClient(ollamaHttpClient, modelName);

if (args.Length >= 2 && string.Equals(args[0], "summarize-file", StringComparison.OrdinalIgnoreCase))
{
    AIAgent summarizerAgent = CreateSummarizerAgent(chatClient);

    var summaryService = new FileSummaryService(
        workspaceFileReader,
        aiArtifactStore,
        summarizerAgent);

    var record = await summaryService.GetOrCreateSummaryAsync(args[1]);

    Console.WriteLine(record.SummaryText);
    Console.WriteLine();
    Console.WriteLine($"Saved: {aiArtifactStore.GetSummaryPath(args[1])}");
    return;
}

if (args.Length >= 1 && string.Equals(args[0], "summarize-all", StringComparison.OrdinalIgnoreCase))
{
    AIAgent summarizerAgent = CreateSummarizerAgent(chatClient);

    var summaryService = new FileSummaryService(
        workspaceFileReader,
        aiArtifactStore,
        summarizerAgent);

    var directoryPath =
        args.Length >= 3 && string.Equals(args[1], "--dir", StringComparison.OrdinalIgnoreCase)
            ? args[2]
            : ".";

    var listResult = workspaceFileLister.ListFiles(directoryPath);

    if (!listResult.Success)
    {
        Console.Error.WriteLine($"Error: {listResult.ErrorMessage}");
        return;
    }

    var generated = 0;
    var current = 0;
    var failed = 0;

    foreach (var file in listResult.Files)
    {
        try
        {
            var wasCurrent = await aiArtifactStore.IsSummaryCurrentAsync(file);
            _ = await summaryService.GetOrCreateSummaryAsync(file);

            if (wasCurrent)
            {
                current++;
                Console.WriteLine($"[current] {file}");
            }
            else
            {
                generated++;
                Console.WriteLine($"[generated] {file}");
            }
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"[failed] {file} :: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Done. generated={generated}, current={current}, failed={failed}, total={listResult.ReturnedFiles}");

    if (listResult.Truncated)
    {
        Console.WriteLine($"[truncated] returned {listResult.ReturnedFiles}/{listResult.TotalFiles} files.");
    }

    return;
}

if (args.Length >= 2 && string.Equals(args[0], "read-summary", StringComparison.OrdinalIgnoreCase))
{
    var record = await aiArtifactStore.TryLoadSummaryAsync(args[1]);
    if (record is null)
    {
        Console.Error.WriteLine("Error: summary file does not exist.");
        return;
    }

    var status = await GetSummaryStatusAsync(aiArtifactStore, args[1]);

    Console.WriteLine($"SourcePath: {record.SourcePath}");
    Console.WriteLine($"GeneratedAtUtc: {record.GeneratedAtUtc:O}");
    Console.WriteLine($"SourceLastWriteTimeUtc: {record.SourceLastWriteTimeUtc:O}");
    Console.WriteLine($"SourceLength: {record.SourceLength}");
    Console.WriteLine($"Status: {status}");
    Console.WriteLine();
    Console.WriteLine(record.SummaryText);
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "approve-plan", StringComparison.OrdinalIgnoreCase))
{
    var result = await aiArtifactStore.ApprovePlanAsync(args[1]);

    Console.WriteLine($"Approved: {result.PlanId}");
    Console.WriteLine($"MovedTo: {result.PlanDirectory}");
    Console.WriteLine($"PlanFile: {result.PlanPath}");
    return;
}

if (args.Length >= 1 && string.Equals(args[0], "execute-plan", StringComparison.OrdinalIgnoreCase))
{
    var isDryRun =
        args.Length >= 2 &&
        string.Equals(args[1], "--dry-run", StringComparison.OrdinalIgnoreCase);

    if (!isDryRun)
    {
        Console.Error.WriteLine("Error: only '--dry-run' is supported at this stage.");
        return;
    }

    var executionService = new PlanExecutionService(aiArtifactStore);
    var preview = await executionService.GetDryRunPreviewAsync();

    if (preview is null)
    {
        Console.WriteLine("No in-progress plan exists.");
        return;
    }

    Console.WriteLine($"PlanId: {preview.PlanId}");
    Console.WriteLine($"PlanPath: {preview.PlanPath}");
    Console.WriteLine();

    if (!string.IsNullOrWhiteSpace(preview.Goal))
    {
        Console.WriteLine("# Goal");
        Console.WriteLine(preview.Goal);
        Console.WriteLine();
    }

    Console.WriteLine("# Proposed Minimal Changes");
    if (preview.ProposedMinimalChanges.Count == 0)
    {
        Console.WriteLine("(none)");
    }
    else
    {
        foreach (var item in preview.ProposedMinimalChanges)
        {
            Console.WriteLine($"- {item}");
        }
    }
    Console.WriteLine();

    Console.WriteLine("# Relevant Files");
    if (preview.RelevantFiles.Count == 0)
    {
        Console.WriteLine("(none)");
    }
    else
    {
        foreach (var item in preview.RelevantFiles)
        {
            Console.WriteLine($"- {item}");
        }
    }
    Console.WriteLine();

    Console.WriteLine("# Risks / Unknowns");
    if (preview.RisksOrUnknowns.Count == 0)
    {
        Console.WriteLine("(none)");
    }
    else
    {
        foreach (var item in preview.RisksOrUnknowns)
        {
            Console.WriteLine($"- {item}");
        }
    }
    Console.WriteLine();

    Console.WriteLine("# User Approval Checklist");
    if (preview.ApprovalChecklist.Count == 0)
    {
        Console.WriteLine("(none)");
    }
    else
    {
        foreach (var item in preview.ApprovalChecklist)
        {
            Console.WriteLine($"- {item}");
        }
    }

    return;
}

if (args.Length >= 1 && string.Equals(args[0], "read-current-plan", StringComparison.OrdinalIgnoreCase))
{
    var currentPlan = await aiArtifactStore.TryGetCurrentInProgressPlanAsync();

    if (currentPlan is null)
    {
        Console.WriteLine("No in-progress plan exists.");
        return;
    }

    Console.WriteLine($"PlanId: {currentPlan.PlanId}");
    Console.WriteLine($"PlanDirectory: {currentPlan.PlanDirectory}");
    Console.WriteLine($"PlanFile: {currentPlan.PlanPath}");
    Console.WriteLine($"Status: {currentPlan.Metadata.Status}");
    Console.WriteLine($"CreatedAtUtc: {currentPlan.Metadata.CreatedAtUtc:O}");
    Console.WriteLine($"ApprovedAtUtc: {currentPlan.Metadata.ApprovedAtUtc:O}");
    Console.WriteLine();
    Console.WriteLine(currentPlan.PlanMarkdown);
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "create-plan", StringComparison.OrdinalIgnoreCase))
{
    var (directoryPath, instruction) = ParseCreatePlanArguments(args);

    AIAgent summarizerAgent = CreateSummarizerAgent(chatClient);
    AIAgent plannerAgent = CreatePlannerAgent(chatClient);

    var summaryService = new FileSummaryService(
        workspaceFileReader,
        aiArtifactStore,
        summarizerAgent);

    var planService = new PlanService(
        workspaceFileLister,
        summaryService,
        aiArtifactStore,
        plannerAgent);

    var result = await planService.CreatePlanAsync(instruction, directoryPath);

    Console.WriteLine(result.PlanText);
    Console.WriteLine();
    Console.WriteLine($"Saved: {result.SavedPath}");
    return;
}

AIAgent agent = chatClient.AsAIAgent(
    name: "LocalCodingAgent",
    instructions:
        "You are a concise local coding assistant. " +
        "If you do not know which file to inspect, use the list-files tool first. " +
        "If you need file content, use the read-file tool. " +
        "Only use tools with relative paths inside the workspace.",
    tools:
    [
        AIFunctionFactory.Create(listFilesTool.ListFiles),
        AIFunctionFactory.Create(readFileTool.ReadFile)
    ]);

var prompt = args.Length > 0
    ? string.Join(" ", args)
    : ReadPrompt();

if (string.IsNullOrWhiteSpace(prompt))
{
    Console.Error.WriteLine("Prompt is required.");
    return;
}

var response = await agent.RunAsync(prompt);
Console.WriteLine(response);

static AIAgent CreateSummarizerAgent(IChatClient chatClient)
{
    return chatClient.AsAIAgent(
        name: "FileSummarizer",
        instructions:
            "You create concise, factual Japanese summaries of source and configuration files " +
            "for a coding agent. Do not invent anything. Summarize only what is present.");
}

static AIAgent CreatePlannerAgent(IChatClient chatClient)
{
    return chatClient.AsAIAgent(
        name: "Planner",
        instructions:
            """
            You create Japanese work plans for a local coding agent.
            You do not execute anything.
            You produce a proposal that will be shown to the user for approval.

            Rules:
            - Be strictly grounded in the provided file summaries.
            - Do not state unverified assumptions as facts.
            - If a type, method, class, setting key, or file is not explicitly supported by the summaries, treat it as unknown.
            - When proposing a new class, method, interface, or refactoring structure, clearly mark it as a proposal.
            - Prefer the smallest viable change first.
            - Separate "existing facts" from "proposed changes".
            - If the summaries are insufficient, include confirmation items instead of guessing.
            - Do not optimize for ideal architecture unless the user explicitly asks for a large redesign.
            - Favor staged, reviewable changes over broad rewrites.
            """
    );
}

static (string DirectoryPath, string Instruction) ParseCreatePlanArguments(string[] args)
{
    if (args.Length >= 4 && string.Equals(args[1], "--dir", StringComparison.OrdinalIgnoreCase))
    {
        return (args[2], string.Join(" ", args.Skip(3)));
    }

    return (".", string.Join(" ", args.Skip(1)));
}

static async Task<string> GetSummaryStatusAsync(AiArtifactStore store, string sourcePath)
{
    var record = await store.TryLoadSummaryAsync(sourcePath);
    if (record is null)
    {
        return "Missing";
    }

    var isCurrent = await store.IsSummaryCurrentAsync(sourcePath);
    return isCurrent ? "Current" : "Stale";
}

static string? ReadPrompt()
{
    Console.Write("Prompt> ");
    return Console.ReadLine();
}