using System.ClientModel;
using System.Text.Json;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using OpenAI;
using OpenAI.Chat;
using OurToolUse = Mediahost.Llm.Models.ToolUseContent;

namespace Mediahost.Llm.Providers;

public sealed class OpenAiProvider(IVaultService vault) : ILlmProvider
{
    private string? _apiKey;

    public string ProviderName => "openai";
    public bool SupportsVision => true;
    public bool SupportsToolUse => true;

    public async Task<LlmResponse> CompleteAsync(
        string model, LlmRequest request, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(ct);
        var client = new OpenAIClient(new ApiKeyCredential(key));
        var chatClient = client.GetChatClient(model);

        var isO1 = model.StartsWith("o1", StringComparison.OrdinalIgnoreCase);

        var messages = BuildMessages(request, isO1);
        var options = BuildOptions(request, isO1);

        var response = await chatClient.CompleteChatAsync(messages, options, ct);
        return MapResponse(response.Value);
    }

    private static List<ChatMessage> BuildMessages(LlmRequest request, bool isO1)
    {
        var messages = new List<ChatMessage>();

        if (isO1)
            messages.Add(new UserChatMessage(request.SystemPrompt));
        else
            messages.Add(new SystemChatMessage(request.SystemPrompt));

        foreach (var msg in request.Messages)
        {
            switch (msg.Role)
            {
                case "user":
                    messages.Add(new UserChatMessage(msg.Content.Select(MapUserPart).ToArray()));
                    break;

                case "assistant":
                    var textContent  = msg.Content.OfType<Models.TextContent>().FirstOrDefault()?.Text;
                    var toolUses     = msg.Content.OfType<OurToolUse>().ToList();

                    if (toolUses.Count > 0)
                    {
                        var toolCalls = toolUses.Select(tu => ChatToolCall.CreateFunctionToolCall(
                            tu.Id, tu.Name,
                            BinaryData.FromString(tu.Input.RootElement.GetRawText()))).ToList();
                        var assistantMsg = new AssistantChatMessage(toolCalls);
                        if (textContent is not null)
                            assistantMsg.Content.Add(ChatMessageContentPart.CreateTextPart(textContent));
                        messages.Add(assistantMsg);
                    }
                    else
                    {
                        messages.Add(new AssistantChatMessage(textContent ?? string.Empty));
                    }
                    break;

                case "tool_result":
                    foreach (var tr in msg.Content.OfType<Models.ToolResultContent>())
                        messages.Add(new ToolChatMessage(tr.ToolUseId, tr.Result));
                    break;
            }
        }

        return messages;
    }

    private static ChatMessageContentPart MapUserPart(LlmContent c) => c switch
    {
        Models.TextContent t    => ChatMessageContentPart.CreateTextPart(t.Text),
        Models.ImageContent img => ChatMessageContentPart.CreateImagePart(
                                       BinaryData.FromBytes(Convert.FromBase64String(img.Base64Data)),
                                       img.MimeType),
        Models.DocumentContent d => ChatMessageContentPart.CreateTextPart(
                                       $"[Document: {d.Title ?? d.MimeType}]"),
        _                       => ChatMessageContentPart.CreateTextPart(string.Empty)
    };

    private static ChatCompletionOptions BuildOptions(LlmRequest request, bool isO1)
    {
        var options = new ChatCompletionOptions();

        if (request.MaxTokens.HasValue)
            options.MaxOutputTokenCount = request.MaxTokens.Value;

        if (!isO1)
        {
            if (request.Temperature.HasValue)
                options.Temperature = request.Temperature.Value;

            if (request.Tools?.Count > 0)
            {
                // Filter out web_search — OpenAI SDK v2.1.0 doesn't support native search.
                // Anthropic and Gemini handle it natively; OpenAI is a fallback anyway.
                foreach (var tool in request.Tools.Where(t => t.Name != "web_search"))
                {
                    options.Tools.Add(ChatTool.CreateFunctionTool(
                        tool.Name,
                        tool.Description,
                        BinaryData.FromString(tool.InputSchema.RootElement.GetRawText())));
                }
            }
        }

        return options;
    }

    private static LlmResponse MapResponse(ChatCompletion response)
    {
        string? text = response.Content.FirstOrDefault(p => p.Kind == ChatMessageContentPartKind.Text)?.Text;

        var toolUses = response.ToolCalls.Select(tc => new OurToolUse(
            tc.Id,
            tc.FunctionName,
            JsonDocument.Parse(tc.FunctionArguments.ToString()))).ToList();

        var stopReason = response.FinishReason switch
        {
            ChatFinishReason.Stop          => StopReason.EndTurn,
            ChatFinishReason.ToolCalls     => StopReason.ToolUse,
            ChatFinishReason.Length        => StopReason.MaxTokens,
            _                              => StopReason.EndTurn
        };

        return new LlmResponse(
            text,
            toolUses,
            stopReason,
            new TokenUsage(response.Usage.InputTokenCount, response.Usage.OutputTokenCount));
    }

    private async Task<string> GetApiKeyAsync(CancellationToken ct)
    {
        if (_apiKey is not null) return _apiKey;
        _apiKey = await vault.GetSecretAsync("/ai/openai", "api_key", ct)
                  ?? throw new InvalidOperationException("OpenAI API key not found in vault.");
        return _apiKey;
    }
}
