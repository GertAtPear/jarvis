namespace Rex.Agent.Models;

public record ShellResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}
