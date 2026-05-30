using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

var endpoint = new Uri("http://localhost:11434");
var modelName = "gpt-oss:20b";

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