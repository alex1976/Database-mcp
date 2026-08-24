using ModelContextProtocol;

namespace DatabaseMcp.Configuration;

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql,
}

public static class DatabaseProviderExtensions
{
    public static DatabaseProvider Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "sqlserver" or "mssql" or "sql-server" => DatabaseProvider.SqlServer,
        "postgresql" or "postgres" or "pg" or "npgsql" => DatabaseProvider.PostgreSql,
        _ => throw new McpException($"Unsupported DB_PROVIDER '{value}'. Use 'sqlserver' or 'postgresql'."),
    };
}
