using DatabaseMcp.Data;

namespace DatabaseMcp.Tests.Integration;

public class QueryServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_ReturnsAllRowsAsCsv_WhenUnderMaxRows()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT Id, Name FROM [{fixture.Schema}].[{fixture.TableName}] ORDER BY Id",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal(3, result.RowCount);
        Assert.False(result.Truncated);
        Assert.Equal("Id,Name\n1,Alice\n2,Bob\n3,Carol\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_TruncatesAndReportsFlag_WhenOverMaxRows()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT Id FROM [{fixture.Schema}].[{fixture.TableName}] ORDER BY Id",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 2, CancellationToken.None);

        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_RendersNullAsEmptyField()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT Amount FROM [{fixture.Schema}].[{fixture.TableName}] WHERE Id = 3",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal("Amount\n\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_WithParameters_BindsValuesSafely()
    {
        var parameters = new Dictionary<string, object> { ["@name"] = "Bob" };

        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT Id FROM [{fixture.Schema}].[{fixture.TableName}] WHERE Name = @name",
            parameters, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal("Id\n2\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task StructuredQueryBuilder_EndToEnd_FiltersAndOrdersCorrectly()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            fixture.Schema, fixture.TableName,
            columns: ["Id", "Name"],
            filters: [new FilterCondition("Amount", "gt", "5")],
            orderBy: [new OrderByColumn("Id", Descending: true)],
            top: 10);

        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            sql, parameters, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        // Carol (Amount = NULL) is excluded by "Amount > 5"; Bob then Alice remain, in Id DESC order.
        Assert.Equal("Id,Name\n2,Bob\n1,Alice\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExportAsync_StreamsFullResultToFile_WithoutRowCap()
    {
        ExportResult result = await fixture.QueryService.ExportAsync(
            $"SELECT Id, Name FROM [{fixture.Schema}].[{fixture.TableName}] ORDER BY Id",
            parameters: null, OutputFormat.Csv, requestedFileName: "export-test", CancellationToken.None);

        Assert.Equal(3, result.RowCount);
        Assert.True(File.Exists(result.FilePath));

        string content = await File.ReadAllTextAsync(result.FilePath);
        Assert.Equal("Id,Name\n1,Alice\n2,Bob\n3,Carol\n", content);
    }
}
