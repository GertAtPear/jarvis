using System.Data;
using System.Text.Json;
using Dapper;

namespace Jarvis.Api.Data;

public static class DapperConfig
{
    public static void Configure()
    {
        SqlMapper.AddTypeHandler(new JsonDocumentHandler());
    }

    private sealed class JsonDocumentHandler : SqlMapper.TypeHandler<JsonDocument>
    {
        public override void SetValue(IDbDataParameter param, JsonDocument? value)
        {
            param.Value = value is null ? DBNull.Value : (object)JsonSerializer.Serialize(value);
        }

        public override JsonDocument Parse(object value) =>
            JsonDocument.Parse(value.ToString()!);
    }
}
