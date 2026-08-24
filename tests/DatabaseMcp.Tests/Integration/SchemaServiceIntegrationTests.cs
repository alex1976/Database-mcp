using DatabaseMcp.Data;

namespace DatabaseMcp.Tests.Integration;

public class SchemaServiceIntegrationTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [RequiresDatabaseFact]
    public async Task ListSchemasAsync_IncludesTheTestSchema()
    {
        IReadOnlyList<SchemaInfo> schemas = await fixture.SchemaService.ListSchemasAsync();

        Assert.Contains(schemas, s => s.Name == fixture.Schema);
    }

    [RequiresDatabaseFact]
    public async Task ListTablesAsync_FilteredBySchema_IncludesTheTestTable()
    {
        IReadOnlyList<TableInfo> tables = await fixture.SchemaService.ListTablesAsync(fixture.Schema);

        Assert.Contains(tables, t => t.Schema == fixture.Schema && t.Name == fixture.TableName);
    }

    [RequiresDatabaseFact]
    public async Task DescribeTableAsync_ReturnsColumnsPrimaryKeyAndRowCount()
    {
        TableDetails? details = await fixture.SchemaService.DescribeTableAsync(fixture.Schema, fixture.TableName);

        Assert.NotNull(details);
        Assert.Equal(3, details.ApproxRowCount);
        Assert.Equal(["Id"], details.PrimaryKeyColumns);

        Assert.Collection(details.Columns.OrderBy(c => c.OrdinalPosition),
            id =>
            {
                Assert.Equal("Id", id.Name);
                Assert.False(id.IsNullable);
            },
            name =>
            {
                Assert.Equal("Name", name.Name);
                Assert.Equal("nvarchar", name.DataType);
                Assert.False(name.IsNullable);
            },
            amount =>
            {
                Assert.Equal("Amount", amount.Name);
                Assert.True(amount.IsNullable);
            });
    }

    [RequiresDatabaseFact]
    public async Task DescribeTableAsync_UnknownTable_ReturnsNull()
    {
        TableDetails? details = await fixture.SchemaService.DescribeTableAsync(fixture.Schema, "NoSuchTable_Xyz123");

        Assert.Null(details);
    }
}
