using System.Text.Json;
using Mediahost.Llm.Models;

namespace Nadia.Agent.Tools;

public static class NadiaToolDefinitions
{
    public static readonly IReadOnlyList<ToolDefinition> All =
    [
        new("get_network_overview",
            "Get an overview of all network interfaces with their latest latency, active status, and recent failover events.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("get_interface_status",
            "Get detailed status and latency history for a specific network interface.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name": {"type":"string","description":"Interface name (e.g. wan-primary, lan-main)"}
              },
              "required":["name"]
            }
            """)),

        new("get_latency_history",
            "Get latency history for an interface over the past N hours.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "interface_name": {"type":"string","description":"Interface name"},
                "hours": {"type":"integer","description":"Hours of history to return (default 24)"}
              },
              "required":["interface_name"]
            }
            """)),

        new("get_wifi_inventory",
            "Get all discovered WiFi nodes/access points.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("get_failover_history",
            "Get recent WAN failover events.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "limit": {"type":"integer","description":"Max results (default 10)"}
              },
              "required":[]
            }
            """)),

        new("run_ping",
            "Run a live ping to a host and return RTT and packet loss.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to ping (IP or hostname)"}
              },
              "required":["host"]
            }
            """)),

        new("run_traceroute",
            "Run a traceroute/tracepath to a host.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "host": {"type":"string","description":"Host to trace (IP or hostname)"}
              },
              "required":["host"]
            }
            """)),

        new("check_dns",
            "Perform a live DNS lookup for a hostname.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "hostname": {"type":"string","description":"Hostname to resolve"},
                "record_type": {"type":"string","description":"Record type (default A)"}
              },
              "required":["hostname"]
            }
            """)),

        new("get_vpn_status",
            "Get VPN connection status from Andrew's network facts.",
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""")),

        new("register_interface",
            "Register a new network interface for monitoring.",
            JsonDocument.Parse("""
            {
              "type":"object",
              "properties": {
                "name":         {"type":"string","description":"Short identifier (e.g. wan-primary)"},
                "display_name": {"type":"string","description":"Human readable name"},
                "if_type":      {"type":"string","description":"Type: wan|lan|vpn|loopback"},
                "ip_address":   {"type":"string","description":"IP address (optional)"},
                "subnet":       {"type":"string","description":"CIDR subnet (optional, e.g. 192.168.1.0/24)"}
              },
              "required":["name","display_name","if_type"]
            }
            """))
    ];
}
