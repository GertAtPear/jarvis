using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jarvis.Ui.Models;

namespace Jarvis.Ui.Services;

public class ChatApiService(HttpClient http, ILogger<ChatApiService> logger)
{
    public async Task<ChatResponse?> SendMessageAsync(string message, Guid? sessionId)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/chat", new { message, sessionId });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ChatResponse>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message");
            return null;
        }
    }

    public async Task<ChatResponse?> SendMessageWithAttachmentsAsync(
        string message, Guid? sessionId, List<PendingAttachment> attachments)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(message), "message");
            if (sessionId.HasValue)
                form.Add(new StringContent(sessionId.Value.ToString()), "sessionId");

            foreach (var att in attachments)
            {
                var bytes       = Convert.FromBase64String(att.Base64Data);
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(att.MimeType);
                form.Add(fileContent, "files", att.FileName);
            }

            var response = await http.PostAsync("/api/chat/upload", form);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ChatResponse>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message with attachments");
            return null;
        }
    }

    public async Task<ChatResponse?> SendDirectAsync(string agentName, string message, Guid? sessionId)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                $"/api/chat/agent/{agentName}", new { message, sessionId });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ChatResponse>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send direct message to agent {Agent}", agentName);
            return null;
        }
    }

    public async Task<List<ChatMessageDto>> GetHistoryAsync(Guid sessionId)
    {
        try
        {
            var msgs = await http.GetFromJsonAsync<List<ChatMessageDto>>(
                           $"/api/chat/{sessionId}/history") ?? [];
            // API response has no attachments field — ensure it's never null
            return msgs.Select(m => m with { Attachments = m.Attachments ?? [] }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get history for session {SessionId}", sessionId);
            return [];
        }
    }

    public async Task<List<AgentDto>> GetAgentsAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<AgentDto>>("/api/agents") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get agents");
            return [];
        }
    }

    public async Task<List<SessionSummaryDto>> GetRecentSessionsAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<SessionSummaryDto>>(
                       "/api/sessions?limit=30") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get recent sessions");
            return [];
        }
    }

    public async Task<BriefingResponse?> GetBriefingAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<BriefingResponse>("/api/briefing/today");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get briefing");
            return null;
        }
    }
}
