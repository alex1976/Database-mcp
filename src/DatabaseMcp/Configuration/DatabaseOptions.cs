using Microsoft.Data.SqlClient;
using Npgsql;

namespace DatabaseMcp.Configuration;

/// <summary>
/// Connection and safety-limit settings, sourced from environment variables. The server targets
/// exactly one database at a time; <see cref="Provider"/> (env var <c>DB_PROVIDER</c>, default
/// <c>sqlserver</c>) selects which engine that is and which connection variables apply.
/// </summary>
public sealed class DatabaseOptions
{
    public required DatabaseProvider Provider { get; init; }

    public required string Host { get; init; }
    public int? Port { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public required string Database { get; init; }

    /// <summary>SQL Server only: encrypt the connection.</summary>
    public bool Encrypt { get; init; } = true;

    /// <summary>SQL Server only: trust the server certificate (self-signed certs).</summary>
    public bool TrustServerCertificate { get; init; }

    /// <summary>PostgreSQL only: libpq-style SSL mode (Disable, Prefer, Require, VerifyCA, VerifyFull).</summary>
    public string PostgresSslMode { get; init; } = "Prefer";

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
        DatabaseProvider provider = DatabaseProviderExtensions.Parse(Environment.GetEnvironmentVariable("DB_PROVIDER"));

        return provider switch
        {
            DatabaseProvider.PostgreSql => FromPostgresEnvironment(),
            _ => FromSqlServerEnvironment(),
        };
    }

    private static DatabaseOptions FromSqlServerEnvironment()
    {
        string? user = Environment.GetEnvironmentVariable("SQLSERVER_USER");
        string? password = Environment.GetEnvironmentVariable("SQLSERVER_PASSWORD");

        return new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            Host = RequireEnv("SQLSERVER_HOST"),
            Database = RequireEnv("SQLSERVER_DATABASE"),
            User = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            Encrypt = ParseBool(Environment.GetEnvironmentVariable("SQLSERVER_ENCRYPT"), defaultValue: true),
            TrustServerCertificate = ParseBool(Environment.GetEnvironmentVariable("SQLSERVER_TRUST_CERT"), defaultValue: false),
            DefaultMaxRows = ParseInt(FirstNonEmpty("DB_DEFAULT_MAX_ROWS", "SQLSERVER_DEFAULT_MAX_ROWS"), 1000),
            HardMaxRows = ParseInt(FirstNonEmpty("DB_HARD_MAX_ROWS", "SQLSERVER_HARD_MAX_ROWS"), 10_000),
            CommandTimeoutSeconds = ParseInt(FirstNonEmpty("DB_COMMAND_TIMEOUT_SECONDS", "SQLSERVER_COMMAND_TIMEOUT_SECONDS"), 30),
            ConnectTimeoutSeconds = ParseInt(FirstNonEmpty("DB_CONNECT_TIMEOUT_SECONDS", "SQLSERVER_CONNECT_TIMEOUT_SECONDS"), 15),
            ExportDirectory = FirstNonEmpty("DB_EXPORT_DIR", "SQLSERVER_EXPORT_DIR") is { Length: > 0 } dir
                ? dir
                : Path.Combine(AppContext.BaseDirectory, "exports"),
        };
    }

    private static DatabaseOptions FromPostgresEnvironment()
    {
        string? user = Environment.GetEnvironmentVariable("POSTGRES_USER");
        string? password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        return new DatabaseOptions
        {
            Provider = DatabaseProvider.PostgreSql,
            Host = RequireEnv("POSTGRES_HOST"),
            Port = ParseInt(Environment.GetEnvironmentVariable("POSTGRES_PORT"), 5432),
            Database = RequireEnv("POSTGRES_DATABASE"),
            User = string.IsNullOrWhiteSpace(user) ? null : user,
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            // To trust a self-signed server certificate, set POSTGRES_SSL_MODE=Require (encrypts
            // without validating the chain) rather than VerifyCA/VerifyFull.
            PostgresSslMode = Environment.GetEnvironmentVariable("POSTGRES_SSL_MODE") is { Length: > 0 } mode ? mode : "Prefer",
            DefaultMaxRows = ParseInt(FirstNonEmpty("DB_DEFAULT_MAX_ROWS", "POSTGRES_DEFAULT_MAX_ROWS"), 1000),
            HardMaxRows = ParseInt(FirstNonEmpty("DB_HARD_MAX_ROWS", "POSTGRES_HARD_MAX_ROWS"), 10_000),
            CommandTimeoutSeconds = ParseInt(FirstNonEmpty("DB_COMMAND_TIMEOUT_SECONDS", "POSTGRES_COMMAND_TIMEOUT_SECONDS"), 30),
            ConnectTimeoutSeconds = ParseInt(FirstNonEmpty("DB_CONNECT_TIMEOUT_SECONDS", "POSTGRES_CONNECT_TIMEOUT_SECONDS"), 15),
            ExportDirectory = FirstNonEmpty("DB_EXPORT_DIR", "POSTGRES_EXPORT_DIR") is { Length: > 0 } dir
                ? dir
                : Path.Combine(AppContext.BaseDirectory, "exports"),
        };
    }

    public string BuildConnectionString() => Provider switch
    {
        DatabaseProvider.PostgreSql => BuildPostgresConnectionString(),
        _ => BuildSqlServerConnectionString(),
    };

    private string BuildSqlServerConnectionString()
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

    private string BuildPostgresConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port ?? 5432,
            Database = Database,
            Username = User,
            Password = Password,
            Timeout = ConnectTimeoutSeconds,
            ApplicationName = "Database-MCP",
            // Npgsql no longer has a separate "trust server certificate" switch: certificate
            // validation is governed entirely by SslMode (Require = encrypt without validating
            // the chain, i.e. "trust", vs VerifyCA/VerifyFull = validate it).
            SslMode = Enum.Parse<SslMode>(PostgresSslMode, ignoreCase: true),
        };

        return builder.ConnectionString;
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

    private static string? FirstNonEmpty(params string[] names) =>
        names
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static bool ParseBool(string? raw, bool defaultValue) =>
        bool.TryParse(raw, out bool parsed) ? parsed : defaultValue;

    private static int ParseInt(string? raw, int defaultValue) =>
        int.TryParse(raw, out int parsed) && parsed > 0 ? parsed : defaultValue;
}
