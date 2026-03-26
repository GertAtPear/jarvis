namespace Andrew.Agent.Models;

public record ActivationResult(bool Success, string? ErrorMessage, DiscoveryResult? DiscoveryResult);
