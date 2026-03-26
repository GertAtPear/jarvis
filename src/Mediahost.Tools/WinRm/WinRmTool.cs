// CREDENTIAL SAFETY: Never log SshCredentials, WinRmCredentials, SqlCredentials, or any password/key values.
// Log only: hostname, operation name, duration, success/failure.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Mediahost.Tools.Interfaces;
using Mediahost.Tools.Models;
using Microsoft.Extensions.Logging;

namespace Mediahost.Tools.WinRm;

/// <summary>
/// Executes PowerShell commands on Windows hosts via WinRM (SOAP over HTTP, NTLM auth).
/// Prerequisite on target: run "winrm quickconfig -q" once in elevated PowerShell.
/// Self-contained — does not reference Mediahost.Agents to avoid circular dependency.
/// </summary>
public sealed class WinRmTool(ILogger<WinRmTool> logger) : IWinRmTool
{
    private const string ResourceUri =
        "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/cmd";

    public async Task<ToolResult<string>> RunCommandAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        string psCommand,
        int timeoutSeconds = 30,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var scheme = credentials.UseHttps ? "https" : "http";
        var port = target.Port > 0 ? target.Port : (credentials.UseHttps ? 5986 : 5985);
        var url = $"{scheme}://{target.ResolvedHost}:{port}/wsman";

        try
        {
            using var http = BuildHttpClient(credentials, timeoutSeconds);

            string shellId;
            try
            {
                shellId = await CreateShellAsync(http, url, timeoutSeconds, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                logger.LogWarning("WinRM CreateShell failed on {Host}:{Port} after {Ms}ms: {Message}",
                    target.Hostname, port, sw.ElapsedMilliseconds, ex.Message);
                return ToolResult<string>.Fail($"WinRM connection failed: {ex.Message}", sw.ElapsedMilliseconds);
            }

            try
            {
                var commandId = await RunCommandInternalAsync(http, url, shellId, psCommand, ct);
                var (stdout, stderr, exitCode) = await ReceiveOutputAsync(http, url, shellId, commandId, ct);
                sw.Stop();

                logger.LogDebug("WinRM command on {Host} exited {Code} in {Ms}ms",
                    target.Hostname, exitCode, sw.ElapsedMilliseconds);

                var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
                return exitCode == 0
                    ? ToolResult<string>.Ok(output, sw.ElapsedMilliseconds)
                    : ToolResult<string>.Fail($"Exit code {exitCode}: {stderr}", sw.ElapsedMilliseconds);
            }
            finally
            {
                try { await DeleteShellAsync(http, url, shellId, ct); }
                catch (Exception ex) { logger.LogDebug(ex, "WinRM shell cleanup failed (non-fatal)"); }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Unexpected WinRM error on {Host}", target.Hostname);
            return ToolResult<string>.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<ToolResult> TestConnectionAsync(
        ConnectionTarget target,
        WinRmCredentials credentials,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var scheme = credentials.UseHttps ? "https" : "http";
        var port = target.Port > 0 ? target.Port : (credentials.UseHttps ? 5986 : 5985);
        var url = $"{scheme}://{target.ResolvedHost}:{port}/wsman";

        try
        {
            using var http = BuildHttpClient(credentials, 15);
            var msgId = NewMessageId();
            var body = $"""
                <env:Envelope
                  xmlns:env="http://www.w3.org/2003/05/soap-envelope"
                  xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                  xmlns:wsmid="http://schemas.dmtf.org/wbem/wsman/identity/1/wsmanidentity.xsd">
                  <env:Header>
                    <wsa:To>{url}</wsa:To>
                    <wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo>
                    <wsa:Action mustUnderstand="true">http://schemas.dmtf.org/wbem/wsman/identity/1/wsmanidentity/Identify</wsa:Action>
                    <wsa:MessageID>uuid:{msgId}</wsa:MessageID>
                  </env:Header>
                  <env:Body><wsmid:Identify/></env:Body>
                </env:Envelope>
                """;

            var response = await PostSoapAsync(http, url, body, ct);
            sw.Stop();

            var doc = XDocument.Parse(response);
            var hasIdentify = doc.Descendants()
                .Any(e => e.Name.LocalName == "IdentifyResponse"
                       || e.Name.LocalName == "ProductVendor"
                       || e.Name.LocalName == "ProtocolVersion");

            if (hasIdentify)
            {
                logger.LogInformation("WinRM connection test to {Host}:{Port} succeeded in {Ms}ms",
                    target.Hostname, port, sw.ElapsedMilliseconds);
                return ToolResult.Ok(sw.ElapsedMilliseconds);
            }

            return ToolResult.Fail("WinRM responded but Identify response was unexpected", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning("WinRM connection test to {Host}:{Port} failed in {Ms}ms: {Message}",
                target.Hostname, port, sw.ElapsedMilliseconds, ex.Message);
            return ToolResult.Fail(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    // ── Create Shell ──────────────────────────────────────────────────────────

    private static async Task<string> CreateShellAsync(
        HttpClient http, string url, int timeout, CancellationToken ct)
    {
        var msgId = NewMessageId();
        var body = $"""
            <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"
              xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
              xmlns:wsman="http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd"
              xmlns:rsp="http://schemas.microsoft.com/wbem/wsman/1/windows/shell">
              <env:Header>
                <wsa:To>{url}</wsa:To>
                <wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo>
                <wsman:ResourceURI mustUnderstand="true">{ResourceUri}</wsman:ResourceURI>
                <wsa:Action mustUnderstand="true">http://schemas.xmlsoap.org/ws/2004/09/transfer/Create</wsa:Action>
                <wsa:MessageID>uuid:{msgId}</wsa:MessageID>
                <wsman:OperationTimeout>PT{timeout}S</wsman:OperationTimeout>
              </env:Header>
              <env:Body>
                <rsp:Shell>
                  <rsp:InputStreams>stdin</rsp:InputStreams>
                  <rsp:OutputStreams>stdout stderr</rsp:OutputStreams>
                </rsp:Shell>
              </env:Body>
            </env:Envelope>
            """;

        var response = await PostSoapAsync(http, url, body, ct);
        var doc = XDocument.Parse(response);

        return doc.Descendants()
                   .Where(e => e.Name.LocalName == "Selector" &&
                               e.Attribute("Name")?.Value == "ShellId")
                   .Select(e => e.Value)
                   .FirstOrDefault()
               ?? throw new InvalidOperationException(
                   "WinRM CreateShell response did not contain a ShellId");
    }

    // ── Run Command ───────────────────────────────────────────────────────────

    private static async Task<string> RunCommandInternalAsync(
        HttpClient http, string url, string shellId, string psCommand, CancellationToken ct)
    {
        var msgId = NewMessageId();
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psCommand));

        var body = $"""
            <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"
              xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
              xmlns:wsman="http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd"
              xmlns:rsp="http://schemas.microsoft.com/wbem/wsman/1/windows/shell">
              <env:Header>
                <wsa:To>{url}</wsa:To>
                <wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo>
                <wsman:ResourceURI mustUnderstand="true">{ResourceUri}</wsman:ResourceURI>
                <wsa:Action mustUnderstand="true">http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Command</wsa:Action>
                <wsa:MessageID>uuid:{msgId}</wsa:MessageID>
                <wsman:SelectorSet>
                  <wsman:Selector Name="ShellId">{shellId}</wsman:Selector>
                </wsman:SelectorSet>
              </env:Header>
              <env:Body>
                <rsp:CommandLine>
                  <rsp:Command>powershell.exe</rsp:Command>
                  <rsp:Arguments>-NonInteractive -NoProfile -EncodedCommand {encoded}</rsp:Arguments>
                </rsp:CommandLine>
              </env:Body>
            </env:Envelope>
            """;

        var response = await PostSoapAsync(http, url, body, ct);
        var doc = XDocument.Parse(response);

        return doc.Descendants()
                   .FirstOrDefault(e => e.Name.LocalName == "CommandId")?.Value
               ?? throw new InvalidOperationException(
                   "WinRM Command response did not contain a CommandId");
    }

    // ── Receive Output ────────────────────────────────────────────────────────

    private static async Task<(string stdout, string stderr, int exitCode)> ReceiveOutputAsync(
        HttpClient http, string url, string shellId, string commandId, CancellationToken ct)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = 0;

        while (true)
        {
            var msgId = NewMessageId();
            var body = $"""
                <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"
                  xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
                  xmlns:wsman="http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd"
                  xmlns:rsp="http://schemas.microsoft.com/wbem/wsman/1/windows/shell">
                  <env:Header>
                    <wsa:To>{url}</wsa:To>
                    <wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo>
                    <wsman:ResourceURI mustUnderstand="true">{ResourceUri}</wsman:ResourceURI>
                    <wsa:Action mustUnderstand="true">http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Receive</wsa:Action>
                    <wsa:MessageID>uuid:{msgId}</wsa:MessageID>
                    <wsman:SelectorSet>
                      <wsman:Selector Name="ShellId">{shellId}</wsman:Selector>
                    </wsman:SelectorSet>
                  </env:Header>
                  <env:Body>
                    <rsp:Receive>
                      <rsp:DesiredStream CommandId="{commandId}">stdout stderr</rsp:DesiredStream>
                    </rsp:Receive>
                  </env:Body>
                </env:Envelope>
                """;

            var response = await PostSoapAsync(http, url, body, ct);
            var doc = XDocument.Parse(response);

            foreach (var stream in doc.Descendants().Where(e => e.Name.LocalName == "Stream"))
            {
                var name = stream.Attribute("Name")?.Value;
                var content = stream.Value;
                if (string.IsNullOrEmpty(content)) continue;

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(content));
                if (name == "stdout") stdout.Append(decoded);
                else if (name == "stderr") stderr.Append(decoded);
            }

            var state = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "CommandState");

            if (state?.Attribute("State")?.Value.EndsWith("/CommandState/Done") == true)
            {
                var exitEl = state.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ExitCode");
                if (exitEl is not null && int.TryParse(exitEl.Value, out var code))
                    exitCode = code;
                break;
            }
        }

        return (stdout.ToString().Trim(), stderr.ToString().Trim(), exitCode);
    }

    // ── Delete Shell ──────────────────────────────────────────────────────────

    private static async Task DeleteShellAsync(
        HttpClient http, string url, string shellId, CancellationToken ct)
    {
        var msgId = NewMessageId();
        var body = $"""
            <env:Envelope xmlns:env="http://www.w3.org/2003/05/soap-envelope"
              xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"
              xmlns:wsman="http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd">
              <env:Header>
                <wsa:To>{url}</wsa:To>
                <wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo>
                <wsman:ResourceURI mustUnderstand="true">{ResourceUri}</wsman:ResourceURI>
                <wsa:Action mustUnderstand="true">http://schemas.xmlsoap.org/ws/2004/09/transfer/Delete</wsa:Action>
                <wsa:MessageID>uuid:{msgId}</wsa:MessageID>
                <wsman:SelectorSet>
                  <wsman:Selector Name="ShellId">{shellId}</wsman:Selector>
                </wsman:SelectorSet>
              </env:Header>
              <env:Body/>
            </env:Envelope>
            """;

        await PostSoapAsync(http, url, body, ct);
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private static HttpClient BuildHttpClient(WinRmCredentials credentials, int timeoutSeconds)
    {
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(credentials.Username, credentials.Password),
            UseDefaultCredentials = false,
            PreAuthenticate = false
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds + 15)
        };
    }

    private static async Task<string> PostSoapAsync(
        HttpClient http, string url, string body, CancellationToken ct)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/soap+xml");
        using var response = await http.PostAsync(url, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            string? fault = null;
            try
            {
                var doc = XDocument.Parse(responseBody);
                fault = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Text")?.Value;
            }
            catch { /* ignore parse failure */ }

            throw new HttpRequestException(
                $"WinRM HTTP {(int)response.StatusCode}: " +
                (fault ?? responseBody[..Math.Min(300, responseBody.Length)]));
        }

        return responseBody;
    }

    private static string NewMessageId() =>
        Guid.NewGuid().ToString("D").ToUpperInvariant();
}
