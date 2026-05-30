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

var allowedExtensions = configuration
.GetSection("WorkspaceFileReader:AllowedExtensions")
.GetChildren()
.Select(x => x.Value)
.Where(x => !string.IsNullOrWhiteSpace(x))
.Cast<string>()
.ToArray();

if (args.Length >= 2 && string.Equals(args[0], "read-file", StringComparison.OrdinalIgnoreCase))
{
    var workspaceRoot = Directory.GetCurrentDirectory();
    var reader = new WorkspaceFileReader(workspaceRoot, allowedExtensions);
    var result = reader.Read(args[1]);

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
    instructions: "You are a concise local coding assistant. Answer clearly and briefly.");

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
