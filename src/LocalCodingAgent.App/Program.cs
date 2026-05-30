using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

var endpoint = new Uri("http://localhost:11434");
var modelName = "gpt-oss:20b";

IChatClient chatClient = new OllamaApiClient(endpoint, modelName);

AIAgent agent = chatClient.AsAIAgent(
    name: "LocalCodingAgent",
    instructions: "You are a concise local coding assistant. Answer clearly and briefly.");

var prompt = "C#でリストを逆順にする最も簡単な方法を1つ教えてください。";
var response = await agent.RunAsync(prompt);

Console.WriteLine(response);