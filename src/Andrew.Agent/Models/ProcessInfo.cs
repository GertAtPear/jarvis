namespace Andrew.Agent.Models;

public class ProcessInfo
{
    public string User { get; set; } = default!;
    public int Pid { get; set; }
    public decimal CpuPercent { get; set; }
    public decimal MemPercent { get; set; }
    public string Command { get; set; } = default!;
}
