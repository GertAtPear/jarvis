using Npgsql;

namespace Jarvis.Api.Data;

public class DbConnectionFactory(IConfiguration config)
{
    public NpgsqlConnection Create() =>
        new(config.GetConnectionString("Postgres"));
}
