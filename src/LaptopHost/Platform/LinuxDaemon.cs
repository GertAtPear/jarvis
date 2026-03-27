namespace LaptopHost.Platform;

/// <summary>
/// Linux-specific helpers for the Local Agent Host.
/// Prints a systemd unit file template for easy service installation.
/// </summary>
public static class LinuxDaemon
{
    /// <summary>
    /// Prints a systemd user service unit file to stdout.
    /// Install with: laptop-host --install | sudo tee /etc/systemd/system/laptop-host.service
    /// Then: sudo systemctl daemon-reload && sudo systemctl enable --now laptop-host
    /// </summary>
    public static void PrintSystemdUnit(string binaryPath)
    {
        var unit = $"""
            [Unit]
            Description=Mediahost AI Local Agent Host
            Documentation=https://github.com/mediahost/mediahost-ai
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=simple
            ExecStart={binaryPath}
            Restart=on-failure
            RestartSec=5
            StandardOutput=journal
            StandardError=journal
            SyslogIdentifier=laptop-host

            [Install]
            WantedBy=default.target
            """;

        Console.WriteLine(unit);
    }
}
