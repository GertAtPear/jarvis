using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Mediahost.Agents.Services;

public record WinRmResult(string Stdout, string Stderr, int ExitCode);

/// <summary>
/// Executes PowerShell commands on Windows servers via WinRM (SOAP over HTTP with NTLM auth).
/// No external NuGet package needed — uses HttpClientHandler + NetworkCredential for NTLM.
/// Prerequisite on target: run "winrm quickconfig -q" once in elevated PowerShell.
/// </summary>
public class WinRmService(ILogger<WinRmService> logger)
{
    private const string ResourceUri = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/cmd";

    public async Task<WinRmResult> ExecuteAsync(
        string host, int port, string user, string password,
        string psCommand, int timeoutSeconds = 30, CancellationToken ct = default)
    {
        var url = $"http://{host}:{port}/wsman";

        using var handler = new HttpClientHandler
        {
            Credentials           = new NetworkCredential(user, password),
            UseDefaultCredentials = false,
            PreAuthenticate       = false
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds + 15)
        };

        string shellId;
        try
        {
            shellId = await CreateShellAsync(http, url, timeoutSeconds, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"WinRM connection to {host}:{port} failed: {ex.Message}", ex);
        }

        try
        {
            var commandId = await RunCommandAsync(http, url, shellId, psCommand, ct);
            var (stdout, stderr, exitCode) = await ReceiveOutputAsync(http, url, shellId, commandId, ct);
            logger.LogInformation("[winrm] {Host}: exit {Code}", host, exitCode);
            return new WinRmResult(stdout, stderr, exitCode);
        }
        finally
        {
            try { await DeleteShellAsync(http, url, shellId, ct); }
            catch (Exception ex) { logger.LogDebug(ex, "WinRM shell cleanup failed (non-fatal)"); }
        }
    }

    // ── Create Shell ──────────────────────────────────────────────────────────

    private async Task<string> CreateShellAsync(
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
                   "WinRM CreateShell response did not contain a ShellId selector");
    }

    // ── Run Command ───────────────────────────────────────────────────────────

    private async Task<string> RunCommandAsync(
        HttpClient http, string url, string shellId, string psCommand, CancellationToken ct)
    {
        var msgId   = NewMessageId();
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
                   "WinRM Command response did not contain CommandId");
    }

    // ── Receive Output ────────────────────────────────────────────────────────

    private async Task<(string stdout, string stderr, int exitCode)> ReceiveOutputAsync(
        HttpClient http, string url, string shellId, string commandId, CancellationToken ct)
    {
        var stdout   = new StringBuilder();
        var stderr   = new StringBuilder();
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
            var doc      = XDocument.Parse(response);

            foreach (var stream in doc.Descendants().Where(e => e.Name.LocalName == "Stream"))
            {
                var name    = stream.Attribute("Name")?.Value;
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

    private async Task DeleteShellAsync(
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

    // ── HTTP helper ───────────────────────────────────────────────────────────

    private static async Task<string> PostSoapAsync(
        HttpClient http, string url, string body, CancellationToken ct)
    {
        using var content  = new StringContent(body, Encoding.UTF8, "application/soap+xml");
        using var response = await http.PostAsync(url, content, ct);
        var responseBody   = await response.Content.ReadAsStringAsync(ct);

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

    private static string NewMessageId() => Guid.NewGuid().ToString("D").ToUpperInvariant();
}
