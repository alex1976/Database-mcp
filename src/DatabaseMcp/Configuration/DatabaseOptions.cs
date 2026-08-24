using Microsoft.Data.SqlClient;

namespace DatabaseMcp.Configuration;

/// <summary>
/// Connection and safety-limit settings, sourced from environment variables per the CLAUDE.md spec.
/// </summary>
public sealed class DatabaseOptions
{
    public required string Host { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string Database { get; init; }
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }

    /// <summary>Default row cap for inline query results (query_data / execute_sql).</summary>
    public int DefaultMaxRows { get; init; } = 1000;

    /// <summary>Hard upper bound a caller cannot exceed for inline results, to protect the LLM context window.</summary>
    public int HardMaxRows { get; init; } = 10_000;

    public int CommandTimeoutSeconds { get; init; } = 30;
    public int ConnectTimeoutSeconds { get; init; } = 15;

    /// <summary>Directory where export_data writes CSV/TXT files.</summary>
    public string ExportDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "exports");

    public static DatabaseOptions FromEnvironment()
    {
        string host = RequireEnv("SQLSERVER_HOST");
        string database = RequireEnv("SQLSERVER_DATABASE");
        string? user = Environment.GetEnvironmentVariable("SQLSERVER_USER");
        string? password = Environment.GetEnvironmentVariable("SQLSERVER_PASSWORD");

        return new DatabaseOptions
        {
            Host = host,
            User = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            Database = database,
            Encrypt = ParseBool(Environment.GetEnvironmentVariable("SQLSERVER_ENCRYPT"), defaultValue: true),
            TrustServerCertificate = ParseBool(Environment.GetEnvironmentVariable("SQLSERVER_TRUST_CERT"), defaultValue: false),
            DefaultMaxRows = ParseInt(Environment.GetEnvironmentVariable("SQLSERVER_DEFAULT_MAX_ROWS"), 1000),
            HardMaxRows = ParseInt(Environment.GetEnvironmentVariable("SQLSERVER_HARD_MAX_ROWS"), 10_000),
            CommandTimeoutSeconds = ParseInt(Environment.GetEnvironmentVariable("SQLSERVER_COMMAND_TIMEOUT_SECONDS"), 30),
            ConnectTimeoutSeconds = ParseInt(Environment.GetEnvironmentVariable("SQLSERVER_CONNECT_TIMEOUT_SECONDS"), 15),
            ExportDirectory = Environment.GetEnvironmentVariable("SQLSERVER_EXPORT_DIR") is { Length: > 0 } dir
                ? dir
                : Path.Combine(AppContext.BaseDirectory, "exports"),
        };
    }

    public string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Host,
            InitialCatalog = Database,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectTimeoutSeconds,
            ApplicationName = "Database-MCP",
        };

        if (User is not null && Password is not null)
        {
            builder.UserID = User;
            builder.Password = Password;
        }
        else
        {
            // On-prem convenience: fall back to Windows Integrated Security when no credentials are supplied.
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

    private static bool ParseBool(string? raw, bool defaultValue) =>
        bool.TryParse(raw, out bool parsed) ? parsed : defaultValue;

    private static int ParseInt(string? raw, int defaultValue) =>
        int.TryParse(raw, out int parsed) && parsed > 0 ? parsed : defaultValue;
}
