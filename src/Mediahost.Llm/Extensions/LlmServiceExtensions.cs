using Mediahost.Llm.Providers;
using Mediahost.Llm.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Mediahost.Llm.Extensions;

public static class LlmServiceExtensions
{
    /// <summary>
    /// Registers the Mediahost LLM abstraction layer.
    /// Prerequisites that must be registered first:
    ///   - IVaultService (from Mediahost.Vault)
    ///   - NpgsqlDataSource (for model routing rules and usage logging)
    ///   - IConnectionMultiplexer (StackExchange.Redis, for classification cache)
    ///   - IHttpClientFactory (for Gemini REST calls)
    /// </summary>
    public static IServiceCollection AddMediahostLlm(this IServiceCollection services)
    {
        // Providers — singletons, registered as ILlmProvider for IEnumerable<ILlmProvider> injection
        services.AddSingleton<AnthropicProvider>();
        services.AddSingleton<GoogleProvider>();
        services.AddSingleton<OpenAiProvider>();

        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<GoogleProvider>());
        services.AddSingleton<ILlmProvider>(sp => sp.GetRequiredService<OpenAiProvider>());

        // Core services
        services.AddSingleton<TaskClassifierService>();
        services.AddSingleton<ModelSelectorService>();

        // Scoped services (one per request/operation)
        services.AddScoped<LlmUsageLogger>();
        services.AddScoped<LlmService>();

        return services;
    }
}
