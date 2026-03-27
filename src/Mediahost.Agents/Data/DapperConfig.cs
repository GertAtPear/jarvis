using System.Data;
using System.Text.Json;
using Dapper;

namespace Mediahost.Agents.Data;

public static class DapperConfig
{
    public static void Configure()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyHandler());
        SqlMapper.AddTypeHandler(new JsonDocumentHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter param, DateOnly value)
        {
            param.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) =>
            value switch
            {
                DateTime dt => DateOnly.FromDateTime(dt),
                DateOnly d  => d,
                _           => DateOnly.Parse(value.ToString()!)
            };
    }

    private sealed class TimeOnlyHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override void SetValue(IDbDataParameter param, TimeOnly value)
        {
            param.Value = value.ToTimeSpan();
        }

        public override TimeOnly Parse(object value) =>
            value switch
            {
                TimeSpan ts => TimeOnly.FromTimeSpan(ts),
                TimeOnly t  => t,
                _           => TimeOnly.Parse(value.ToString()!)
            };
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
