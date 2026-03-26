namespace Mediahost.Tools.Models;

public enum DatabaseType { PostgreSQL, MySQL, MariaDB }

public record SqlCredentials(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    DatabaseType Type)
{
    public string BuildConnectionString() => Type switch
    {
        DatabaseType.PostgreSQL =>
            $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password};Timeout=15;CommandTimeout=30",
        DatabaseType.MySQL or DatabaseType.MariaDB =>
            $"Server={Host};Port={Port};Database={Database};User={Username};Password={Password};ConnectionTimeout=15",
        _ => throw new NotSupportedException($"DatabaseType {Type} is not supported")
    };
}
