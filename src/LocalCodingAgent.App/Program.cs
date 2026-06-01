using LocalCodingAgent.App.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;

var commandArgs = StripGlobalOptions(args);
var configuration = LoadConfiguration(args);

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
var copilotInstructionsProvider = new CopilotInstructionsProvider(workspaceRoot);
var workspaceFileReader = new WorkspaceFileReader(workspaceRoot, fileReaderOptions);
var workspaceFileLister = new WorkspaceFileLister(
    workspaceRoot,
    fileReaderOptions.AllowedExtensions,
    fileListingOptions);

var aiArtifactStore = new AiArtifactStore(workspaceRoot, artifactStoreOptions);

var readFileTool = new ReadFileTool(workspaceFileReader);
var listFilesTool = new ListFilesTool(workspaceFileLister);

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "read-file", StringComparison.OrdinalIgnoreCase))
{
    var result = workspaceFileReader.Read(commandArgs[1]);

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

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "list-files", StringComparison.OrdinalIgnoreCase))
{
    var result = workspaceFileLister.ListFiles(commandArgs[1]);

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

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "summary-path", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(aiArtifactStore.GetSummaryPath(commandArgs[1]));
    return;
}

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "summary-status", StringComparison.OrdinalIgnoreCase))
{
    var status = await GetSummaryStatusAsync(aiArtifactStore, commandArgs[1]);
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

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "summarize-file", StringComparison.OrdinalIgnoreCase))
{
    AIAgent summarizerAgent = CreateSummarizerAgent(chatClient);

    var summaryService = new FileSummaryService(
        workspaceFileReader,
        aiArtifactStore,
        summarizerAgent);

    var record = await summaryService.GetOrCreateSummaryAsync(commandArgs[1]);

    Console.WriteLine(record.SummaryText);
    Console.WriteLine();
    Console.WriteLine($"Saved: {aiArtifactStore.GetSummaryPath(commandArgs[1])}");
    return;
}

if (commandArgs.Length >= 1 && string.Equals(commandArgs[0], "summarize-all", StringComparison.OrdinalIgnoreCase))
{
    AIAgent summarizerAgent = CreateSummarizerAgent(chatClient);

    var summaryService = new FileSummaryService(
        workspaceFileReader,
        aiArtifactStore,
        summarizerAgent);

    var directoryPath =
        commandArgs.Length >= 3 && string.Equals(commandArgs[1], "--dir", StringComparison.OrdinalIgnoreCase)
            ? commandArgs[2]
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

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "read-summary", StringComparison.OrdinalIgnoreCase))
{
    var record = await aiArtifactStore.TryLoadSummaryAsync(commandArgs[1]);
    if (record is null)
    {
        Console.Error.WriteLine("Error: summary file does not exist.");
        return;
    }

    var status = await GetSummaryStatusAsync(aiArtifactStore, commandArgs[1]);

    Console.WriteLine($"SourcePath: {record.SourcePath}");
    Console.WriteLine($"GeneratedAtUtc: {record.GeneratedAtUtc:O}");
    Console.WriteLine($"SourceLastWriteTimeUtc: {record.SourceLastWriteTimeUtc:O}");
    Console.WriteLine($"SourceLength: {record.SourceLength}");
    Console.WriteLine($"Status: {status}");
    Console.WriteLine();
    Console.WriteLine(record.SummaryText);
    return;
}

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "approve-plan", StringComparison.OrdinalIgnoreCase))
{
    var result = await aiArtifactStore.ApprovePlanAsync(commandArgs[1]);

    Console.WriteLine($"Approved: {result.PlanId}");
    Console.WriteLine($"MovedTo: {result.PlanDirectory}");
    Console.WriteLine($"PlanFile: {result.PlanPath}");
    return;
}

if (commandArgs.Length >= 1 && string.Equals(commandArgs[0], "execute-plan", StringComparison.OrdinalIgnoreCase))
{
    var isDryRun =
        commandArgs.Length >= 2 &&
        string.Equals(commandArgs[1], "--dry-run", StringComparison.OrdinalIgnoreCase);

    var executionService = new PlanExecutionService(aiArtifactStore, workspaceFileReader);
    var preview = await executionService.GetDryRunPreviewAsync();

    if (preview is null)
    {
        Console.WriteLine("No in-progress plan exists.");
        return;
    }

    if (isDryRun)
    {
        Console.WriteLine($"PlanId: {preview.PlanId}");
        Console.WriteLine($"PlanPath: {preview.PlanPath}");
        Console.WriteLine($"Executable: {preview.CanExecute}");
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(preview.Goal))
        {
            Console.WriteLine("# Goal");
            Console.WriteLine(preview.Goal);
            Console.WriteLine();
        }

        Console.WriteLine("# Planned Output Files");
        if (preview.PlannedOutputFiles.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            foreach (var file in preview.PlannedOutputFiles)
            {
                Console.WriteLine($"- {file.Path} | Kind={file.Kind} | Reason={file.Reason}");
            }
        }
        Console.WriteLine();

        Console.WriteLine("# Reconfirmed Target Files");
        if (preview.ReconfirmedTargetFiles.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            foreach (var file in preview.ReconfirmedTargetFiles)
            {
                Console.WriteLine(
                    $"- {file.SourcePath} | Exists={file.Exists} | Readable={file.Readable} | SummaryCurrent={file.SummaryIsCurrent} | Note={file.Note}");
            }
        }
        Console.WriteLine();

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

        Console.WriteLine("# Execution Readiness");
        if (preview.CanExecute)
        {
            Console.WriteLine("This plan is executable in principle.");
        }
        else
        {
            Console.WriteLine("This plan is NOT executable yet.");
            Console.WriteLine();
            Console.WriteLine("Blocking reasons:");
            foreach (var reason in preview.BlockingReasons)
            {
                Console.WriteLine($"- {reason}");
            }
        }

        return;
    }

    var implementationAgent = CreateImplementationAgent(chatClient);
    var outputStore = new ExecutionOutputStore(
        workspaceRoot,
        aiArtifactStore.OutputRoot,
        fileReaderOptions.AllowedExtensions);

    var copilotInstructions = await copilotInstructionsProvider.TryLoadAsync();

    var generationService = new PlanOutputGenerationService(
        executionService,
        workspaceFileReader,
        aiArtifactStore,
        outputStore,
        implementationAgent,
        copilotInstructions);

    var result = await generationService.ExecuteToOutputAsync();
    var completed = await aiArtifactStore.CompleteCurrentInProgressPlanAsync();

    Console.WriteLine();
    Console.WriteLine($"PlanId: {result.Preview.PlanId}");
    Console.WriteLine($"OutputRoot: {outputStore.GetPlanOutputRoot(result.Preview.PlanId)}");
    Console.WriteLine($"Manifest: {result.ManifestPath}");
    Console.WriteLine($"CompletedTo: {completed.PlanDirectory}");
    Console.WriteLine($"CompletedPlanFile: {completed.PlanPath}");
    return;
}

if (commandArgs.Length >= 1 && string.Equals(commandArgs[0], "read-current-plan", StringComparison.OrdinalIgnoreCase))
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

if (commandArgs.Length >= 2 && string.Equals(commandArgs[0], "create-plan", StringComparison.OrdinalIgnoreCase))
{
    var (directoryPath, instruction) = ParseCreatePlanArguments(commandArgs);

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

    var copilotInstructions = await copilotInstructionsProvider.TryLoadAsync();

    var result = await planService.CreatePlanAsync(instruction, directoryPath, copilotInstructions);

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

var prompt = commandArgs.Length > 0
    ? string.Join(" ", commandArgs)
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

static AIAgent CreateImplementationAgent(IChatClient chatClient)
{
    return chatClient.AsAIAgent(
        name: "ImplementationAgent",
        instructions:
            """
            You generate full source files for a local coding agent.

            Hard requirements:
            - Output only the full file content.
            - Do not include explanations.
            - Do not include markdown fences.
            - Keep the change as small as possible.
            - Preserve existing behavior unless the approved plan explicitly changes it.
            - Do not invent new helper classes or new files unless they are explicitly included in Planned Output Files.
            - Do not reference variables, methods, tuple members, types, or files that are not available in the current file context or explicitly described in the plan/context.
            - The result must be internally consistent and compile in principle with the surrounding existing code.

            For modified files:
            - Use the current file content as the base.
            - Preserve unrelated logic.
            - Refactor minimally.

            For new files:
            - Only generate them if they are explicitly listed in Planned Output Files.
            - Keep them minimal and aligned with the approved plan.
            """
    );
}

static (string DirectoryPath, string Instruction) ParseCreatePlanArguments(string[] commandArgs)
{
    if (commandArgs.Length >= 4 && string.Equals(commandArgs[1], "--dir", StringComparison.OrdinalIgnoreCase))
    {
        return (commandArgs[2], string.Join(" ", commandArgs.Skip(3)));
    }

    return (".", string.Join(" ", commandArgs.Skip(1)));
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

static IConfigurationRoot LoadConfiguration(string[] args)
{
    var configDirectory = ResolveConfigDirectory(args);

    if (!Directory.Exists(configDirectory))
    {
        throw new InvalidOperationException(
            $"Configuration directory does not exist: {configDirectory}");
    }

    var appSettingsPath = Path.Combine(configDirectory, "appsettings.json");
    if (!File.Exists(appSettingsPath))
    {
        throw new InvalidOperationException(
            $"Configuration file does not exist: {appSettingsPath}");
    }

    return new ConfigurationBuilder()
        .SetBasePath(configDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Local.json", optional: true)
        .AddEnvironmentVariables(prefix: "LOCALCODINGAGENT_")
        .Build();
}

static string ResolveConfigDirectory(string[] args)
{
    var optionValue = TryGetGlobalOptionValue(args, "--config-dir");
    if (!string.IsNullOrWhiteSpace(optionValue))
    {
        return Path.GetFullPath(optionValue);
    }

    var envValue = Environment.GetEnvironmentVariable("LOCALCODINGAGENT_CONFIG_DIR");
    if (!string.IsNullOrWhiteSpace(envValue))
    {
        return Path.GetFullPath(envValue);
    }

    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
    {
        throw new InvalidOperationException("Failed to resolve the user home directory.");
    }

    return Path.Combine(home, "Library", "Application Support", "local-coding-agent");
}

static string? TryGetGlobalOptionValue(string[] args, string optionName)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static string[] StripGlobalOptions(string[] args)
{
    var result = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--config-dir", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            continue;
        }

        result.Add(args[i]);
    }

    return result.ToArray();
}