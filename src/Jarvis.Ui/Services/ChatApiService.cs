using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jarvis.Ui.Auth;
using Jarvis.Ui.Models;
using Microsoft.AspNetCore.Components;

namespace Jarvis.Ui.Services;

public class ChatApiService(
    HttpClient http,
    JarvisAuthStateProvider authState,
    NavigationManager nav,
    ILogger<ChatApiService> logger)
{
    private async Task SetAuthHeaderAsync()
    {
        var token = await authState.GetTokenAsync();
        if (token != null)
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>Returns true if the response was 401, logs out, and redirects to /login.</summary>
    private async Task<bool> CheckUnauthorizedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized) return false;
        await HandleUnauthorizedAsync();
        return true;
    }

    private async Task HandleUnauthorizedAsync()
    {
        logger.LogWarning("Session token expired — redirecting to login");
        await authState.LogoutAsync();
        nav.NavigateTo("/login", forceLoad: true);
    }

    public async Task<ChatResponse?> SendMessageAsync(string message, Guid? sessionId)
    {
        try
        {
            await SetAuthHeaderAsync();
            var response = await http.PostAsJsonAsync("/api/chat", new { message, sessionId });
            if (await CheckUnauthorizedAsync(response)) return null;
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
            await SetAuthHeaderAsync();
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
            if (await CheckUnauthorizedAsync(response)) return null;
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
            await SetAuthHeaderAsync();
            var response = await http.PostAsJsonAsync(
                $"/api/chat/agent/{agentName}", new { message, sessionId });
            if (await CheckUnauthorizedAsync(response)) return null;
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
            await SetAuthHeaderAsync();
            var msgs = await http.GetFromJsonAsync<List<ChatMessageDto>>(
                           $"/api/chat/{sessionId}/history") ?? [];
            // API response has no attachments field — ensure it's never null
            return msgs.Select(m => m with { Attachments = m.Attachments ?? [] }).ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = HandleUnauthorizedAsync();
            return [];
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
            await SetAuthHeaderAsync();
            return await http.GetFromJsonAsync<List<AgentDto>>("/api/agents") ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = HandleUnauthorizedAsync();
            return [];
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
            await SetAuthHeaderAsync();
            return await http.GetFromJsonAsync<List<SessionSummaryDto>>(
                       "/api/sessions?limit=30") ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = HandleUnauthorizedAsync();
            return [];
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
            await SetAuthHeaderAsync();
            return await http.GetFromJsonAsync<BriefingResponse>("/api/briefing/today");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = HandleUnauthorizedAsync();
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get briefing");
            return null;
        }
    }

    // ── Agent message bus API ─────────────────────────────────────────────────

    public async Task<List<AgentActivityMessageDto>> GetAgentMessagesAsync(int limit = 100)
    {
        try
        {
            await SetAuthHeaderAsync();
            return await http.GetFromJsonAsync<List<AgentActivityMessageDto>>(
                       $"/api/agent-messages?limit={limit}") ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _ = HandleUnauthorizedAsync();
            return [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get agent messages");
            return [];
        }
    }

    public async Task<bool> ApproveAgentMessageAsync(long id)
    {
        try
        {
            await SetAuthHeaderAsync();
            var resp = await http.PostAsJsonAsync($"/api/agent-messages/{id}/approve", new { });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve agent message {Id}", id);
            return false;
        }
    }

    public async Task<bool> DenyAgentMessageAsync(long id)
    {
        try
        {
            await SetAuthHeaderAsync();
            var resp = await http.PostAsJsonAsync($"/api/agent-messages/{id}/deny", new { });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deny agent message {Id}", id);
            return false;
        }
    }

    // ── Device API ────────────────────────────────────────────────────────────

    public async Task<List<DeviceDto>> GetDevicesAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<DeviceDto>>("/api/devices") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get devices");
            return [];
        }
    }

    public async Task<DeviceDto?> GetDeviceAsync(Guid id)
    {
        try
        {
            return await http.GetFromJsonAsync<DeviceDto>($"/api/devices/{id}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get device {Id}", id);
            return null;
        }
    }

    public async Task<RegisterDeviceResponseDto?> RegisterDeviceAsync(string name, string? platform = null)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/api/devices/register",
                new RegisterDeviceRequestDto(name, platform));
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<RegisterDeviceResponseDto>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register device");
            return null;
        }
    }

    public async Task<List<DevicePermissionDto>> GetDevicePermissionsAsync(Guid id)
    {
        try
        {
            return await http.GetFromJsonAsync<List<DevicePermissionDto>>(
                $"/api/devices/{id}/permissions") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get device permissions for {Id}", id);
            return [];
        }
    }

    public async Task<List<DeviceToolLogDto>> GetDeviceLogAsync(Guid id, int limit = 50)
    {
        try
        {
            return await http.GetFromJsonAsync<List<DeviceToolLogDto>>(
                $"/api/devices/{id}/log?limit={limit}") ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get device log for {Id}", id);
            return [];
        }
    }

    public async Task<bool> DeleteDeviceAsync(Guid id)
    {
        try
        {
            var resp = await http.DeleteAsync($"/api/devices/{id}");
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete device {Id}", id);
            return false;
        }
    }
}
