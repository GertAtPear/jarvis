namespace Andrew.Agent.Models;

public class DiscoveryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public ServerInfo? Server { get; set; }
    public List<ContainerInfo> Containers { get; set; } = [];
    public List<ApplicationInfo> Applications { get; set; } = [];
    public List<ProcessInfo> Processes { get; set; } = [];
    public List<int> ListeningPorts { get; set; } = [];
    public List<string> SystemdServices { get; set; } = [];
    public int DurationMs { get; set; }
}
