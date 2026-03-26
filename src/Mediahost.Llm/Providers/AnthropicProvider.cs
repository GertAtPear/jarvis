using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Mediahost.Llm.Models;
using Mediahost.Shared.Services;
using SdkMessage = Anthropic.SDK.Messaging.Message;
using SdkTextContent = Anthropic.SDK.Messaging.TextContent;
using SdkImageContent = Anthropic.SDK.Messaging.ImageContent;
using SdkDocumentContent = Anthropic.SDK.Messaging.DocumentContent;
using SdkToolUseContent = Anthropic.SDK.Messaging.ToolUseContent;
using SdkToolResultContent = Anthropic.SDK.Messaging.ToolResultContent;
using OurToolUse = Mediahost.Llm.Models.ToolUseContent;

namespace Mediahost.Llm.Providers;

public sealed class AnthropicProvider(IVaultService vault) : ILlmProvider
{
    private string? _apiKey;

    public string ProviderName => "anthropic";
    public bool SupportsVision => true;
    public bool SupportsToolUse => true;

    public async Task<LlmResponse> CompleteAsync(
        string model, LlmRequest request, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(ct);
        var client = new AnthropicClient(key);

        var parameters = new MessageParameters
        {
            Model = model,
            System = [new SystemMessage(request.SystemPrompt)],
            Messages = request.Messages.Select(MapMessage).ToList(),
            MaxTokens = request.MaxTokens ?? 4096,
            Temperature = request.Temperature.HasValue ? (decimal)request.Temperature.Value : null
        };

        if (request.Tools?.Count > 0)
        {
            var hasWebSearch = request.Tools.Any(t => t.Name == "web_search");
            var functionTools = request.Tools
                .Where(t => t.Name != "web_search")
                .Select(MapTool)
                .ToList<Anthropic.SDK.Common.Tool>();

            if (hasWebSearch)
                functionTools.Add(ServerTools.GetWebSearchTool(toolVersion: ServerTools.WebSearchVersionLegacy));

            parameters.Tools = functionTools;
        }

        var response = await client.Messages.GetClaudeMessageAsync(parameters, ct);
        return MapResponse(response);
    }

    private static SdkMessage MapMessage(LlmMessage msg)
    {
        var role = msg.Role == "assistant" ? RoleType.Assistant : RoleType.User;
        return new SdkMessage { Role = role, Content = msg.Content.Select(MapContent).ToList() };
    }

    private static ContentBase MapContent(LlmContent c) => c switch
    {
        Models.TextContent t => new SdkTextContent { Text = t.Text },

        Models.ImageContent img => new SdkImageContent
        {
            Source = new ImageSource
            {
                Type = SourceType.base64,
                MediaType = img.MimeType,
                Data = img.Base64Data
            }
        },

        Models.DocumentContent doc => new SdkDocumentContent
        {
            Source = new DocumentSource
            {
                Type = SourceType.base64,
                MediaType = doc.MimeType,
                Data = doc.Base64Data
            },
            Title = doc.Title
        },

        OurToolUse tu => new SdkToolUseContent
        {
            Id = tu.Id,
            Name = tu.Name,
            Input = JsonNode.Parse(tu.Input.RootElement.GetRawText())!
        },

        Models.ToolResultContent tr => new SdkToolResultContent
        {
            ToolUseId = tr.ToolUseId,
            Content = [new SdkTextContent { Text = tr.Result }],
            IsError = tr.IsError
        },

        _ => throw new InvalidOperationException($"Unknown content type: {c.Type}")
    };

    private static Anthropic.SDK.Common.Tool MapTool(ToolDefinition def) =>
        new(new Anthropic.SDK.Common.Function(
            def.Name,
            def.Description,
            JsonNode.Parse(def.InputSchema.RootElement.GetRawText())));

    private static LlmResponse MapResponse(MessageResponse response)
    {
        string? text = null;
        var toolUses = new List<OurToolUse>();

        foreach (var block in response.Content)
        {
            switch (block)
            {
                case SdkTextContent t:
                    text = t.Text;
                    break;
                case SdkToolUseContent tu:
                    toolUses.Add(new OurToolUse(
                        tu.Id,
                        tu.Name,
                        JsonDocument.Parse(tu.Input?.ToJsonString() ?? "{}")));
                    break;
                // ServerToolUseContent and WebSearchToolResultContent are server-side
                // operations handled entirely by Anthropic. Skip them — the result is
                // already folded into Claude's text response.
                case ServerToolUseContent:
                case WebSearchToolResultContent:
                    break;
            }
        }

        // If stop_reason is tool_use but all tool calls were server-side (web_search),
        // treat as end_turn so the agentic loop exits cleanly.
        var effectiveStopReason = response.StopReason == "tool_use" && toolUses.Count == 0
            ? "end_turn"
            : response.StopReason;

        var stopReason = effectiveStopReason switch
        {
            "end_turn"      => StopReason.EndTurn,
            "tool_use"      => StopReason.ToolUse,
            "max_tokens"    => StopReason.MaxTokens,
            "stop_sequence" => StopReason.StopSequence,
            _               => StopReason.EndTurn
        };

        return new LlmResponse(
            text,
            toolUses,
            stopReason,
            new TokenUsage(response.Usage.InputTokens, response.Usage.OutputTokens));
    }

    private async Task<string> GetApiKeyAsync(CancellationToken ct)
    {
        if (_apiKey is not null) return _apiKey;
        _apiKey = await vault.GetSecretAsync("/ai/anthropic", "api_key", ct)
                  ?? throw new InvalidOperationException("Anthropic API key not found in vault.");
        return _apiKey;
    }
}
