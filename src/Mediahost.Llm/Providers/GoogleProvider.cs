using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using OurToolUse = Mediahost.Llm.Models.ToolUseContent;

namespace Mediahost.Llm.Providers;

public sealed class GoogleProvider(IVaultService vault, IHttpClientFactory httpClientFactory) : ILlmProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private string? _apiKey;

    public string ProviderName => "google";
    public bool SupportsVision => true;
    public bool SupportsToolUse => true;

    public async Task<LlmResponse> CompleteAsync(
        string model, LlmRequest request, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(ct);
        var client = httpClientFactory.CreateClient("google");

        var body = BuildRequestBody(request);
        var url = $"{BaseUrl}/{model}:generateContent?key={key}";

        var httpResponse = await client.PostAsJsonAsync(url, body, ct);
        httpResponse.EnsureSuccessStatusCode();

        var json = await httpResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
                   ?? throw new InvalidOperationException("Empty response from Gemini API.");

        return MapResponse(json);
    }

    private static JsonObject BuildRequestBody(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.SystemPrompt })
            },
            ["contents"] = BuildContents(request.Messages)
        };

        if (request.Tools?.Count > 0)
        {
            var hasWebSearch = request.Tools.Any(t => t.Name == "web_search");
            var toolsArray = new JsonArray();

            var functionTools = request.Tools.Where(t => t.Name != "web_search").ToList();
            if (functionTools.Count > 0)
            {
                var declarations = new JsonArray();
                foreach (var tool in functionTools)
                {
                    declarations.Add(new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.InputSchema.RootElement.GetRawText())
                    });
                }
                toolsArray.Add(new JsonObject { ["function_declarations"] = declarations });
            }

            // Google Search grounding is a separate tool entry, not a function declaration
            if (hasWebSearch)
                toolsArray.Add(new JsonObject { ["google_search"] = new JsonObject() });

            if (toolsArray.Count > 0)
                body["tools"] = toolsArray;
        }

        var config = new JsonObject();
        if (request.MaxTokens.HasValue)
            config["maxOutputTokens"] = request.MaxTokens.Value;
        if (request.Temperature.HasValue)
            config["temperature"] = request.Temperature.Value;
        if (config.Count > 0)
            body["generationConfig"] = config;

        return body;
    }

    private static JsonArray BuildContents(IReadOnlyList<LlmMessage> messages)
    {
        var contents = new JsonArray();

        foreach (var msg in messages)
        {
            // Gemini uses "user"/"model"; tool results go as user messages
            var role = msg.Role == "assistant" ? "model" : "user";
            var parts = new JsonArray();

            foreach (var c in msg.Content)
            {
                switch (c)
                {
                    case Models.TextContent t:
                        parts.Add(new JsonObject { ["text"] = t.Text });
                        break;

                    case Models.ImageContent img:
                        parts.Add(new JsonObject
                        {
                            ["inline_data"] = new JsonObject
                            {
                                ["mime_type"] = img.MimeType,
                                ["data"] = img.Base64Data
                            }
                        });
                        break;

                    case Models.DocumentContent doc:
                        parts.Add(new JsonObject
                        {
                            ["inline_data"] = new JsonObject
                            {
                                ["mime_type"] = doc.MimeType,
                                ["data"] = doc.Base64Data
                            }
                        });
                        break;

                    case OurToolUse tu:
                        parts.Add(new JsonObject
                        {
                            ["function_call"] = new JsonObject
                            {
                                ["name"] = tu.Name,
                                ["args"] = JsonNode.Parse(tu.Input.RootElement.GetRawText())
                            }
                        });
                        break;

                    case Models.ToolResultContent tr:
                        parts.Add(new JsonObject
                        {
                            ["function_response"] = new JsonObject
                            {
                                // Gemini requires the function name, not the tool-use ID
                                ["name"] = !string.IsNullOrEmpty(tr.ToolName) ? tr.ToolName : tr.ToolUseId,
                                ["response"] = new JsonObject { ["result"] = tr.Result }
                            }
                        });
                        break;
                }
            }

            contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
        }

        return contents;
    }

    private static LlmResponse MapResponse(JsonDocument doc)
    {
        var candidate = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content");

        string? text = null;
        var toolUses = new List<OurToolUse>();

        foreach (var part in candidate.GetProperty("parts").EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProp))
                text = textProp.GetString();

            if (part.TryGetProperty("functionCall", out var fc))
            {
                var name = fc.GetProperty("name").GetString()!;
                var args = fc.GetProperty("args");
                var id = $"gemini-{Guid.NewGuid():N}";
                toolUses.Add(new OurToolUse(id, name, JsonDocument.Parse(args.GetRawText())));
            }
        }

        var finishReason = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("finishReason")
            .GetString();

        var stopReason = finishReason switch
        {
            "STOP"       => StopReason.EndTurn,
            "MAX_TOKENS" => StopReason.MaxTokens,
            _            => toolUses.Count > 0 ? StopReason.ToolUse : StopReason.EndTurn
        };

        var usage = doc.RootElement.GetProperty("usageMetadata");
        var inputTokens  = usage.TryGetProperty("promptTokenCount",     out var inp) ? inp.GetInt32() : 0;
        var outputTokens = usage.TryGetProperty("candidatesTokenCount", out var out_) ? out_.GetInt32() : 0;

        return new LlmResponse(text, toolUses, stopReason, new TokenUsage(inputTokens, outputTokens));
    }

    private async Task<string> GetApiKeyAsync(CancellationToken ct)
    {
        if (_apiKey is not null) return _apiKey;
        _apiKey = await vault.GetSecretAsync("/ai/gemini", "api_key", ct)
                  ?? throw new InvalidOperationException("Google AI API key not found in vault.");
        return _apiKey;
    }
}
