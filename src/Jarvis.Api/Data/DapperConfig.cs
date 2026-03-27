using System.Data;
using System.Text.Json;
using Dapper;

namespace Jarvis.Api.Data;

public static class DapperConfig
{
    public static void Configure()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new JsonDocumentHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter param, DateTimeOffset value) =>
            param.Value = value.UtcDateTime;

        public override DateTimeOffset Parse(object value) =>
            value is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero) : (DateTimeOffset)value;
    }

    private sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override void SetValue(IDbDataParameter param, DateTimeOffset? value) =>
            param.Value = value.HasValue ? value.Value.UtcDateTime : DBNull.Value;

        public override DateTimeOffset? Parse(object value) =>
            value is null or DBNull ? null
            : value is DateTime dt ? new DateTimeOffset(dt, TimeSpan.Zero)
            : (DateTimeOffset?)value;
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter param, DateOnly value) =>
            param.Value = value.ToDateTime(TimeOnly.MinValue);

        public override DateOnly Parse(object value) =>
            value is DateTime dt ? DateOnly.FromDateTime(dt) : DateOnly.Parse(value.ToString()!);
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
