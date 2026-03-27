using System.Text.Json;
using Mediahost.Llm.Models;

namespace Lexi.Agent.Tools;

public static class LexiToolDefinitions
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new("get_security_overview",
            "Get a security overview: cert expiry count, unexpected ports, unresolved anomalies, unacknowledged CVEs, and unknown network devices.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("get_certificate_status",
            "Get TLS certificate status. Optionally filter by host.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to filter by (optional — returns all if not specified)"}
              },
              "required":[]
            }
            """)),

        new("get_expiring_certs",
            "Get certificates expiring within the specified number of days, sorted by urgency.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "days": {"type":"integer","description":"Alert threshold in days (default 30)"}
              },
              "required":[]
            }
            """)),

        new("get_open_ports",
            "Get open ports. Optionally filter by host or show only unexpected ports.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to filter (optional)"},
                "unexpected_only": {"type":"boolean","description":"Show only ports not marked as expected (default false)"}
              },
              "required":[]
            }
            """)),

        new("get_access_anomalies",
            "Get unresolved access anomalies (failed login attempts, brute force, etc.).",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "severity": {"type":"string","description":"Filter by severity (optional)"},
                "source_ip": {"type":"string","description":"Filter by source IP (optional)"}
              },
              "required":[]
            }
            """)),

        new("get_cve_alerts",
            "Get unacknowledged CVE alerts for software in the inventory.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "severity": {"type":"string","description":"Filter by severity: CRITICAL|HIGH|MEDIUM (optional)"},
                "package": {"type":"string","description":"Filter by package name (optional)"}
              },
              "required":[]
            }
            """)),

        new("get_software_inventory",
            "Get software inventory. Optionally filter by host or package manager.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to filter (optional)"},
                "package_manager": {"type":"string","description":"apt|yum|pip|npm (optional)"}
              },
              "required":[]
            }
            """)),

        new("get_network_devices",
            "Get all network devices discovered on the LAN, including WiFi devices from Nadia. Highlights unknown/unrecognised devices.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("run_cert_check",
            "Run a live TLS certificate check against a host.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Hostname or IP"},
                "port": {"type":"integer","description":"Port (default 443)"}
              },
              "required":["host"]
            }
            """)),

        new("run_port_scan",
            "Run a port scan on a registered server (reads server list from Andrew).",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to scan"}
              },
              "required":["host"]
            }
            """)),

        new("run_network_scan",
            "Scan the LAN for connected devices using ARP/nmap. Cross-references against known devices.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("mark_anomaly_resolved",
            "Mark an access anomaly as resolved.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "id": {"type":"string","description":"Anomaly UUID"},
                "notes": {"type":"string","description":"Resolution notes (optional)"}
              },
              "required":["id"]
            }
            """)),

        new("mark_port_expected",
            "Mark an open port as expected/legitimate to stop alerting on it.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "id": {"type":"string","description":"Open port record UUID"}
              },
              "required":["id"]
            }
            """)),

        new("mark_cve_acknowledged",
            "Acknowledge a CVE as accepted risk.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "id":     {"type":"string","description":"CVE alert UUID"},
                "reason": {"type":"string","description":"Acceptance reason (optional)"}
              },
              "required":["id"]
            }
            """)),

        new("mark_device_known",
            "Mark a network device as known/expected.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "id":          {"type":"string","description":"Device UUID"},
                "device_name": {"type":"string","description":"Friendly name for the device"},
                "notes":       {"type":"string","description":"Additional notes (optional)"}
              },
              "required":["id","device_name"]
            }
            """))
    ];
}
