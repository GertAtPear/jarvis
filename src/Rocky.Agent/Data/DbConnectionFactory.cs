using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Rocky.Agent.Data;

public class DbConnectionFactory(IConfiguration config)
{
    public NpgsqlConnection Create() =>
        new(config.GetConnectionString("Postgres"));
}
