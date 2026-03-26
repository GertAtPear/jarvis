using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Mediahost.Agents.Data;

public class DbConnectionFactory(IConfiguration config)
{
    public NpgsqlConnection Create() =>
        new(config.GetConnectionString("Postgres"));
}
