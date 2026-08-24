using DatabaseMcp.Configuration;
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

        // PostgreSQL's information_schema.columns reports VARCHAR as "character varying";
        // SQL Server's sys.types reports it as "varchar".
        string expectedNameType = fixture.Options.Provider == DatabaseProvider.PostgreSql ? "character varying" : "varchar";

        Assert.NotNull(details);
        Assert.Equal(3, details.ApproxRowCount);
        Assert.Equal(["id"], details.PrimaryKeyColumns);

        Assert.Collection(details.Columns.OrderBy(c => c.OrdinalPosition),
            id =>
            {
                Assert.Equal("id", id.Name);
                Assert.False(id.IsNullable);
            },
            name =>
            {
                Assert.Equal("name", name.Name);
                Assert.Equal(expectedNameType, name.DataType);
                Assert.False(name.IsNullable);
            },
            amount =>
            {
                Assert.Equal("amount", amount.Name);
                Assert.True(amount.IsNullable);
            });
    }

    [RequiresDatabaseFact]
    public async Task DescribeTableAsync_UnknownTable_ReturnsNull()
    {
        TableDetails? details = await fixture.SchemaService.DescribeTableAsync(fixture.Schema, "nosuchtable_xyz123");

        Assert.Null(details);
    }
}
