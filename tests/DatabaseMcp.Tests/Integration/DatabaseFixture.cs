using System.Data.Common;
using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using DatabaseMcp.Security;

namespace DatabaseMcp.Tests.Integration;

/// <summary>
/// Creates a uniquely named temp table with known data in the configured database before the
/// integration tests run, and drops it afterwards, so the tests don't depend on any particular
/// pre-existing schema (e.g. a sample database) being present. Works against whichever provider
/// DB_PROVIDER/SQLSERVER_*/POSTGRES_* env vars select, using only DDL/types that are portable
/// across both engines (VARCHAR/INT/DECIMAL — no NVARCHAR).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public DatabaseOptions Options { get; private set; } = null!;
    public SqlConnectionFactory ConnectionFactory { get; private set; } = null!;
    public ISchemaService SchemaService { get; private set; } = null!;
    public QueryService QueryService { get; private set; } = null!;

    public string Schema { get; private set; } = "dbo";

    // Lowercase: PostgreSQL folds unquoted identifiers (both DDL and column names below) to
    // lowercase, so a mixed-case name here would mismatch what quoted lookups (e.g. from
    // StructuredQueryBuilder, which always quotes) expect back from the catalog.
    public string TableName { get; } = $"mcptest_{Guid.NewGuid():N}";
    public string ExportDirectory { get; } = Path.Combine(Path.GetTempPath(), $"database-mcp-tests-{Guid.NewGuid():N}");

    /// <summary>The schema-qualified, correctly quoted reference to the fixture's temp table.</summary>
    public string TableRef => SqlIdentifier.BracketQualified(Options.Provider, Schema, TableName);

    public async Task InitializeAsync()
    {
        if (!RequiresDatabaseFactAttribute.IsConfigured)
        {
            // No provider configured: every test in this fixture is skipped via
            // [RequiresDatabaseFact], so there is nothing to set up.
            return;
        }

        Environment.SetEnvironmentVariable("DB_EXPORT_DIR", ExportDirectory);
        Options = DatabaseOptions.FromEnvironment();
        Schema = Options.Provider == DatabaseProvider.PostgreSql ? "public" : "dbo";
        ConnectionFactory = new SqlConnectionFactory(Options);
        SchemaService = Options.Provider == DatabaseProvider.PostgreSql
            ? new PostgreSqlSchemaService(ConnectionFactory)
            : new SqlServerSchemaService(ConnectionFactory);
        QueryService = new QueryService(ConnectionFactory, Options);

        await using DbConnection connection = await ConnectionFactory.OpenAsync();
        await using DbCommand create = connection.CreateCommand();
        create.CommandText = $"""
            CREATE TABLE {TableRef} (
                id INT NOT NULL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                amount DECIMAL(10,2) NULL
            );
            """;
        await create.ExecuteNonQueryAsync();

        await using DbCommand seed = connection.CreateCommand();
        seed.CommandText = $"""
            INSERT INTO {TableRef} (id, name, amount) VALUES
                (1, 'Alice', 10.50),
                (2, 'Bob', 20.00),
                (3, 'Carol', NULL);
            """;
        await seed.ExecuteNonQueryAsync();

        if (Options.Provider == DatabaseProvider.PostgreSql)
        {
            // pg_stat_user_tables.n_live_tup (used for the approximate row count) is only
            // populated once ANALYZE/autovacuum has run; force it so the count is deterministic
            // for tests instead of racing autovacuum.
            await using DbCommand analyze = connection.CreateCommand();
            analyze.CommandText = $"ANALYZE {TableRef};";
            await analyze.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (!RequiresDatabaseFactAttribute.IsConfigured)
        {
            return;
        }

        await using DbConnection connection = await ConnectionFactory.OpenAsync();
        await using DbCommand drop = connection.CreateCommand();
        drop.CommandText = $"DROP TABLE IF EXISTS {TableRef};";
        await drop.ExecuteNonQueryAsync();

        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }
}
