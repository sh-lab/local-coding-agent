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

var workspaceRoot = Directory.GetCurrentDirectory();
var workspaceFileReader = new WorkspaceFileReader(workspaceRoot, fileReaderOptions);
var workspaceFileLister = new WorkspaceFileLister(
    workspaceRoot,
    fileReaderOptions.AllowedExtensions,
    fileListingOptions);

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

var endpointValue = configuration["Ollama:Endpoint"];
var modelName = configuration["Ollama:ModelName"];

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

var endpoint = new Uri(endpointValue);
IChatClient chatClient = new OllamaApiClient(endpoint, modelName);

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

static string? ReadPrompt()
{
    Console.Write("Prompt> ");
    return Console.ReadLine();
}
