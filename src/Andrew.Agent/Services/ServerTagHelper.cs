using System.Text.Json;
using System.Text.Json.Nodes;
using Andrew.Agent.Models;

namespace Andrew.Agent.Services;

public static class ServerTagHelper
{
    public static string GetConnectionType(ServerInfo server)
    {
        if (server.Tags?.RootElement.ValueKind != JsonValueKind.Object)
            return "ssh";
        return server.Tags.RootElement.TryGetProperty("connection_type", out var ct)
            ? ct.GetString() ?? "ssh"
            : "ssh";
    }

    /// <summary>
    /// Updates the server's Tags to include the given connection_type, preserving other keys.
    /// </summary>
    public static void ApplyConnectionType(ServerInfo server, string connectionType)
    {
        var obj = server.Tags?.RootElement.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(server.Tags.RootElement.GetRawText())?.AsObject() ?? new JsonObject()
            : new JsonObject();

        obj["connection_type"] = connectionType;
        server.Tags = JsonDocument.Parse(obj.ToJsonString());
    }
}
