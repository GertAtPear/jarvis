// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using System.Net.Sockets;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Mediahost.Tools.Ssh;

public sealed class SshTool(ILogger<SshTool> logger) : ISshTool
{
    public Task<ToolResult<string>> RunCommandAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        string command,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var connInfo = BuildConnectionInfo(target, credentials);
            using var ssh = new SshClient(connInfo);
            ssh.Connect();
            using var cmd = ssh.RunCommand(command);
            var stdout = cmd.Result;
            var stderr = cmd.Error;
            ssh.Disconnect();
            sw.Stop();

            if (cmd.ExitStatus != 0 && string.IsNullOrWhiteSpace(stdout))
            {
                logger.LogDebug("SSH command on {Host} exited with code {Code} in {Ms}ms",
                    target.Hostname, cmd.ExitStatus, sw.ElapsedMilliseconds);
                return Task.FromResult(ToolResult<string>.Fail(
                    $"Exit code {cmd.ExitStatus}: {stderr}", sw.ElapsedMilliseconds));
            }

            logger.LogDebug("SSH command on {Host} completed in {Ms}ms", target.Hostname, sw.ElapsedMilliseconds);
            return Task.FromResult(ToolResult<string>.Ok(stdout, sw.ElapsedMilliseconds));
        }
        catch (Exception ex) when (ex is SshException or SocketException or SshAuthenticationException)
        {
            sw.Stop();
            logger.LogWarning("SSH connection to {Host}:{Port} failed after {Ms}ms: {Message}",
                target.Hostname, target.Port, sw.ElapsedMilliseconds, ex.Message);
            return Task.FromResult(ToolResult<string>.Fail(
                $"SSH connection failed: {ex.Message}", sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Unexpected error during SSH command on {Host}", target.Hostname);
            return Task.FromResult(ToolResult<string>.Fail(ex.Message, sw.ElapsedMilliseconds));
        }
    }

    public Task<ToolResult> TestConnectionAsync(
        ConnectionTarget target,
        SshCredentials credentials,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var connInfo = BuildConnectionInfo(target, credentials);
            using var ssh = new SshClient(connInfo);
            ssh.Connect();
            ssh.Disconnect();
            sw.Stop();
            logger.LogInformation("SSH connection test to {Host}:{Port} succeeded in {Ms}ms",
                target.Hostname, target.Port, sw.ElapsedMilliseconds);
            return Task.FromResult(ToolResult.Ok(sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("SSH connection test to {Host}:{Port} failed in {Ms}ms: {Message}",
                target.Hostname, target.Port, sw.ElapsedMilliseconds, ex.Message);
            return Task.FromResult(ToolResult.Fail(ex.Message, sw.ElapsedMilliseconds));
        }
    }

    private static ConnectionInfo BuildConnectionInfo(ConnectionTarget target, SshCredentials credentials)
    {
        AuthenticationMethod authMethod = credentials.IsKeyBased
            ? new PrivateKeyAuthenticationMethod(credentials.Username,
                new PrivateKeyFile(credentials.KeyFilePath!))
            : new PasswordAuthenticationMethod(credentials.Username, credentials.Password!);

        return new ConnectionInfo(target.ResolvedHost, target.Port, credentials.Username, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }
}
