using System.Net.Http.Json;
using Jarvis.Ui.Models;

namespace Jarvis.Ui.Services;

public class UsageApiService(HttpClient http, ILogger<UsageApiService> logger)
{
    public async Task<UsageSummaryDto?> GetSummaryAsync(DateOnly from, DateOnly to, string? agentName = null)
    {
        try
        {
            var url = $"/api/usage/summary?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
                      + (agentName is not null ? $"&agentName={Uri.EscapeDataString(agentName)}" : "");
            return await http.GetFromJsonAsync<UsageSummaryDto>(url);
        }
        catch (Exception ex) { logger.LogError(ex, "GetSummaryAsync failed"); return null; }
    }

    public async Task<List<DailyUsageDto>> GetDailyAsync(DateOnly from, DateOnly to)
    {
        try
        {
            return await http.GetFromJsonAsync<List<DailyUsageDto>>(
                $"/api/usage/daily?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetDailyAsync failed"); return []; }
    }

    public async Task<List<AgentUsageDto>> GetByAgentAsync(DateOnly from, DateOnly to)
    {
        try
        {
            return await http.GetFromJsonAsync<List<AgentUsageDto>>(
                $"/api/usage/by-agent?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetByAgentAsync failed"); return []; }
    }

    public async Task<List<RoutingAnalyticDto>> GetRoutingAnalyticsAsync(DateOnly from, DateOnly to)
    {
        try
        {
            return await http.GetFromJsonAsync<List<RoutingAnalyticDto>>(
                $"/api/usage/routing-analytics?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetRoutingAnalyticsAsync failed"); return []; }
    }

    public async Task<List<EscalationRecordDto>> GetEscalationsAsync(DateOnly from, DateOnly to)
    {
        try
        {
            return await http.GetFromJsonAsync<List<EscalationRecordDto>>(
                $"/api/usage/escalations?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetEscalationsAsync failed"); return []; }
    }

    public async Task<List<SlowRequestDto>> GetSlowestAsync(int limit = 20)
    {
        try
        {
            return await http.GetFromJsonAsync<List<SlowRequestDto>>(
                $"/api/usage/slowest?limit={limit}") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetSlowestAsync failed"); return []; }
    }

    public async Task<List<CostRateDto>> GetCostRatesAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<CostRateDto>>("/api/usage/cost-rates") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetCostRatesAsync failed"); return []; }
    }

    public async Task<bool> UpsertCostRateAsync(string providerName, string modelId,
        decimal inputCostPer1k, decimal outputCostPer1k)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/api/usage/cost-rates",
                new { providerName, modelId, inputCostPer1k, outputCostPer1k });
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { logger.LogError(ex, "UpsertCostRateAsync failed"); return false; }
    }

    public async Task<List<RoutingRuleDto>> GetRoutingRulesAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<RoutingRuleDto>>("/api/routing-rules") ?? [];
        }
        catch (Exception ex) { logger.LogError(ex, "GetRoutingRulesAsync failed"); return []; }
    }

    public async Task<bool> InvalidateCacheAsync()
    {
        try
        {
            var resp = await http.PostAsync("/api/routing-rules/cache/invalidate", null);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { logger.LogError(ex, "InvalidateCacheAsync failed"); return false; }
    }
}
