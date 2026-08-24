using DatabaseMcp.Data;

namespace DatabaseMcp.Tests.Integration;

public class QueryServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_ReturnsAllRowsAsCsv_WhenUnderMaxRows()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT id, name FROM {fixture.TableRef} ORDER BY id",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal(3, result.RowCount);
        Assert.False(result.Truncated);
        Assert.Equal("id,name\n1,Alice\n2,Bob\n3,Carol\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_TruncatesAndReportsFlag_WhenOverMaxRows()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT id FROM {fixture.TableRef} ORDER BY id",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 2, CancellationToken.None);

        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_RendersNullAsEmptyField()
    {
        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT amount FROM {fixture.TableRef} WHERE id = 3",
            parameters: null, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal("amount\n\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExecuteInlineAsync_WithParameters_BindsValuesSafely()
    {
        var parameters = new Dictionary<string, object> { ["@name"] = "Bob" };

        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            $"SELECT id FROM {fixture.TableRef} WHERE name = @name",
            parameters, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        Assert.Equal("id\n2\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task StructuredQueryBuilder_EndToEnd_FiltersAndOrdersCorrectly()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            fixture.Options.Provider, fixture.Schema, fixture.TableName,
            columns: ["id", "name"],
            filters: [new FilterCondition("amount", "gt", "5")],
            orderBy: [new OrderByColumn("id", Descending: true)],
            top: 10);

        InlineQueryResult result = await fixture.QueryService.ExecuteInlineAsync(
            sql, parameters, OutputFormat.Csv, requestedMaxRows: 10, CancellationToken.None);

        // Carol (amount = NULL) is excluded by "amount > 5"; Bob then Alice remain, in id DESC order.
        Assert.Equal("id,name\n2,Bob\n1,Alice\n", result.Content);
    }

    [RequiresDatabaseFact]
    public async Task ExportAsync_StreamsFullResultToFile_WithoutRowCap()
    {
        ExportResult result = await fixture.QueryService.ExportAsync(
            $"SELECT id, name FROM {fixture.TableRef} ORDER BY id",
            parameters: null, OutputFormat.Csv, requestedFileName: "export-test", CancellationToken.None);

        Assert.Equal(3, result.RowCount);
        Assert.True(File.Exists(result.FilePath));

        string content = await File.ReadAllTextAsync(result.FilePath);
        Assert.Equal("id,name\n1,Alice\n2,Bob\n3,Carol\n", content);
    }
}
