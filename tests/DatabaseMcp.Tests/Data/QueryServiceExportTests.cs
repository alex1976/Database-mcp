using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Data;

/// <summary>
/// Export checks that need no database: the options point at an unreachable host, so any test that
/// reaches the connection step would fail with a non-McpException. These therefore prove the check runs first.
/// </summary>
public class QueryServiceExportTests : IDisposable
{
    private readonly string _exportDirectory = Path.Combine(Path.GetTempPath(), "dbmcp-export-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_exportDirectory))
        {
            Directory.Delete(_exportDirectory, recursive: true);
        }
    }

    private QueryService CreateService()
    {
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            Host = "unreachable.invalid",
            Database = "unused",
            ExportDirectory = _exportDirectory,
        };

        return new QueryService(new SqlConnectionFactory(options), options);
    }

    [Fact]
    public async Task ExportAsync_RejectsExistingFileName_WithoutOverwriting()
    {
        Directory.CreateDirectory(_exportDirectory);
        string existing = Path.Combine(_exportDirectory, "report.csv");
        await File.WriteAllTextAsync(existing, "previous export");

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateService().ExportAsync(
            "SELECT 1", parameters: null, OutputFormat.Csv, requestedFileName: "report", CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal("previous export", await File.ReadAllTextAsync(existing));
    }
}
