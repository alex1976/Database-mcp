using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using Microsoft.Data.SqlClient;

namespace DatabaseMcp.Tests.Integration;

/// <summary>
/// Creates a uniquely named temp table with known data in the configured database before the
/// integration tests run, and drops it afterwards, so the tests don't depend on any particular
/// pre-existing schema (e.g. a sample database) being present.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public DatabaseOptions Options { get; private set; } = null!;
    public SqlConnectionFactory ConnectionFactory { get; private set; } = null!;
    public SchemaService SchemaService { get; private set; } = null!;
    public QueryService QueryService { get; private set; } = null!;

    public string Schema => "dbo";
    public string TableName { get; } = $"McpTest_{Guid.NewGuid():N}";
    public string ExportDirectory { get; } = Path.Combine(Path.GetTempPath(), $"database-mcp-tests-{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        if (!RequiresDatabaseFactAttribute.IsConfigured)
        {
            // No SQLSERVER_HOST/SQLSERVER_DATABASE configured: every test in this fixture is
            // skipped via [RequiresDatabaseFact], so there is nothing to set up.
            return;
        }

        Environment.SetEnvironmentVariable("SQLSERVER_EXPORT_DIR", ExportDirectory);
        Options = DatabaseOptions.FromEnvironment();
        ConnectionFactory = new SqlConnectionFactory(Options);
        SchemaService = new SchemaService(ConnectionFactory);
        QueryService = new QueryService(ConnectionFactory, Options);

        await using SqlConnection connection = await ConnectionFactory.OpenAsync();
        await using var create = new SqlCommand($"""
            CREATE TABLE [{Schema}].[{TableName}] (
                Id INT NOT NULL PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Amount DECIMAL(10,2) NULL
            );
            """, connection);
        await create.ExecuteNonQueryAsync();

        await using var seed = new SqlCommand($"""
            INSERT INTO [{Schema}].[{TableName}] (Id, Name, Amount) VALUES
                (1, 'Alice', 10.50),
                (2, 'Bob', 20.00),
                (3, 'Carol', NULL);
            """, connection);
        await seed.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (!RequiresDatabaseFactAttribute.IsConfigured)
        {
            return;
        }

        await using SqlConnection connection = await ConnectionFactory.OpenAsync();
        await using var drop = new SqlCommand($"DROP TABLE IF EXISTS [{Schema}].[{TableName}];", connection);
        await drop.ExecuteNonQueryAsync();

        if (Directory.Exists(ExportDirectory))
        {
            Directory.Delete(ExportDirectory, recursive: true);
        }
    }
}
