using DatabaseMcp.Data;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Data;

public class StructuredQueryBuilderTests
{
    [Fact]
    public void Build_WithNoFiltersOrColumns_SelectsStarWithTop()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null, filters: null, orderBy: null, top: 100);

        Assert.Equal("SELECT TOP (100) * FROM [dbo].[Orders];", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Build_WithColumns_BracketsEachColumn()
    {
        (string sql, _) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: ["Id", "Total"], filters: null, orderBy: null, top: 10);

        Assert.Equal("SELECT TOP (10) [Id], [Total] FROM [dbo].[Orders];", sql);
    }

    [Fact]
    public void Build_WithEqFilter_BindsValueAsParameter()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "eq", "Shipped")],
            orderBy: null, top: 10);

        Assert.Equal("SELECT TOP (10) * FROM [dbo].[Orders] WHERE [Status] = @p0;", sql);
        Assert.Equal("Shipped", parameters["@p0"]);
    }

    [Fact]
    public void Build_WithMultipleFilters_CombinesWithAnd()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters:
            [
                new FilterCondition("Status", "eq", "Shipped"),
                new FilterCondition("Total", "gt", "100"),
            ],
            orderBy: null, top: 10);

        Assert.Equal("SELECT TOP (10) * FROM [dbo].[Orders] WHERE [Status] = @p0 AND [Total] > @p1;", sql);
        Assert.Equal("Shipped", parameters["@p0"]);
        Assert.Equal("100", parameters["@p1"]);
    }

    [Fact]
    public void Build_WithInFilter_ExpandsCommaSeparatedValuesToParameters()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "in", "Shipped, Pending ,Cancelled")],
            orderBy: null, top: 10);

        Assert.Equal("SELECT TOP (10) * FROM [dbo].[Orders] WHERE [Status] IN (@p0, @p1, @p2);", sql);
        Assert.Equal("Shipped", parameters["@p0"]);
        Assert.Equal("Pending", parameters["@p1"]);
        Assert.Equal("Cancelled", parameters["@p2"]);
    }

    [Fact]
    public void Build_WithInFilter_EmptyValueList_Throws()
    {
        var ex = Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "in", "")],
            orderBy: null, top: 10));

        Assert.Contains("comma-separated value list", ex.Message);
    }

    [Theory]
    [InlineData("isnull", "IS NULL")]
    [InlineData("isnotnull", "IS NOT NULL")]
    public void Build_WithNullCheckFilter_EmitsUnaryOperatorWithoutParameter(string op, string expectedSql)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("DeletedAt", op, null)],
            orderBy: null, top: 10);

        Assert.Equal($"SELECT TOP (10) * FROM [dbo].[Orders] WHERE [DeletedAt] {expectedSql};", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Build_WithUnsupportedOperator_Throws()
    {
        var ex = Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "contains", "x")],
            orderBy: null, top: 10));

        Assert.Contains("Unsupported filter operator", ex.Message);
    }

    [Fact]
    public void Build_WithOrderBy_AppendsAscAndDesc()
    {
        (string sql, _) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null, filters: null,
            orderBy: [new OrderByColumn("Total", Descending: true), new OrderByColumn("Id", Descending: false)],
            top: 10);

        Assert.Equal("SELECT TOP (10) * FROM [dbo].[Orders] ORDER BY [Total] DESC, [Id] ASC;", sql);
    }

    [Theory]
    [InlineData("Orders; DROP TABLE Orders --")]
    [InlineData("Orders]; DROP TABLE Orders --")]
    [InlineData("Orders' OR '1'='1")]
    public void Build_RejectsInjectionAttemptsInTableName(string maliciousTable)
    {
        Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            "dbo", maliciousTable, columns: null, filters: null, orderBy: null, top: 10));
    }

    [Fact]
    public void Build_RejectsInjectionAttemptsInColumnName()
    {
        Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: ["Id]; DROP TABLE Orders --"], filters: null, orderBy: null, top: 10));
    }

    [Fact]
    public void Build_FilterValueForInjectionAttempt_IsBoundAsParameterNotConcatenated()
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "eq", "'; DROP TABLE Orders --")],
            orderBy: null, top: 10);

        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("'; DROP TABLE Orders --", parameters["@p0"]);
    }
}
