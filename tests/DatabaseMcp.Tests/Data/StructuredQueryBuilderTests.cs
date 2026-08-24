using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Data;

public class StructuredQueryBuilderTests
{
    private static string Q(DatabaseProvider provider, string identifier) =>
        provider == DatabaseProvider.PostgreSql ? $"\"{identifier}\"" : $"[{identifier}]";

    private static string Top(DatabaseProvider provider, int n) =>
        provider == DatabaseProvider.SqlServer ? $"TOP ({n}) " : string.Empty;

    private static string Limit(DatabaseProvider provider, int n) =>
        provider == DatabaseProvider.PostgreSql ? $" LIMIT {n}" : string.Empty;

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithNoFiltersOrColumns_SelectsStarWithRowLimit(DatabaseProvider provider)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null, filters: null, orderBy: null, top: 100);

        Assert.Equal($"SELECT {Top(provider, 100)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")}{Limit(provider, 100)};", sql);
        Assert.Empty(parameters);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithColumns_QuotesEachColumn(DatabaseProvider provider)
    {
        (string sql, _) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: ["Id", "Total"], filters: null, orderBy: null, top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}{Q(provider, "Id")}, {Q(provider, "Total")} FROM {Q(provider, "dbo")}.{Q(provider, "Orders")}{Limit(provider, 10)};",
            sql);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithEqFilter_BindsValueAsParameter(DatabaseProvider provider)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "eq", "Shipped")],
            orderBy: null, top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")} WHERE {Q(provider, "Status")} = @p0{Limit(provider, 10)};",
            sql);
        Assert.Equal("Shipped", parameters["@p0"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithMultipleFilters_CombinesWithAnd(DatabaseProvider provider)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null,
            filters:
            [
                new FilterCondition("Status", "eq", "Shipped"),
                new FilterCondition("Total", "gt", "100"),
            ],
            orderBy: null, top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")} " +
            $"WHERE {Q(provider, "Status")} = @p0 AND {Q(provider, "Total")} > @p1{Limit(provider, 10)};",
            sql);
        Assert.Equal("Shipped", parameters["@p0"]);
        Assert.Equal("100", parameters["@p1"]);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithInFilter_ExpandsCommaSeparatedValuesToParameters(DatabaseProvider provider)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "in", "Shipped, Pending ,Cancelled")],
            orderBy: null, top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")} " +
            $"WHERE {Q(provider, "Status")} IN (@p0, @p1, @p2){Limit(provider, 10)};",
            sql);
        Assert.Equal("Shipped", parameters["@p0"]);
        Assert.Equal("Pending", parameters["@p1"]);
        Assert.Equal("Cancelled", parameters["@p2"]);
    }

    [Fact]
    public void Build_WithInFilter_EmptyValueList_Throws()
    {
        var ex = Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            DatabaseProvider.SqlServer, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "in", "")],
            orderBy: null, top: 10));

        Assert.Contains("comma-separated value list", ex.Message);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "isnull", "IS NULL")]
    [InlineData(DatabaseProvider.SqlServer, "isnotnull", "IS NOT NULL")]
    [InlineData(DatabaseProvider.PostgreSql, "isnull", "IS NULL")]
    [InlineData(DatabaseProvider.PostgreSql, "isnotnull", "IS NOT NULL")]
    public void Build_WithNullCheckFilter_EmitsUnaryOperatorWithoutParameter(DatabaseProvider provider, string op, string expectedSql)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("DeletedAt", op, null)],
            orderBy: null, top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")} WHERE {Q(provider, "DeletedAt")} {expectedSql}{Limit(provider, 10)};",
            sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Build_WithUnsupportedOperator_Throws()
    {
        var ex = Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            DatabaseProvider.SqlServer, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "contains", "x")],
            orderBy: null, top: 10));

        Assert.Contains("Unsupported filter operator", ex.Message);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_WithOrderBy_AppendsAscAndDesc(DatabaseProvider provider)
    {
        (string sql, _) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null, filters: null,
            orderBy: [new OrderByColumn("Total", Descending: true), new OrderByColumn("Id", Descending: false)],
            top: 10);

        Assert.Equal(
            $"SELECT {Top(provider, 10)}* FROM {Q(provider, "dbo")}.{Q(provider, "Orders")} " +
            $"ORDER BY {Q(provider, "Total")} DESC, {Q(provider, "Id")} ASC{Limit(provider, 10)};",
            sql);
    }

    [Fact]
    public void Build_PostgreSql_PlacesLimitAfterOrderBy()
    {
        (string sql, _) = StructuredQueryBuilder.Build(
            DatabaseProvider.PostgreSql, "public", "orders", columns: null,
            filters: [new FilterCondition("status", "eq", "shipped")],
            orderBy: [new OrderByColumn("id", Descending: true)],
            top: 5);

        Assert.Equal(
            "SELECT * FROM \"public\".\"orders\" WHERE \"status\" = @p0 ORDER BY \"id\" DESC LIMIT 5;",
            sql);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_RejectsInjectionAttemptsInTableName(DatabaseProvider provider)
    {
        Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            provider, "dbo", "Orders; DROP TABLE Orders --", columns: null, filters: null, orderBy: null, top: 10));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_RejectsInjectionAttemptsInColumnName(DatabaseProvider provider)
    {
        Assert.Throws<McpException>(() => StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: ["Id]; DROP TABLE Orders --"], filters: null, orderBy: null, top: 10));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Build_FilterValueForInjectionAttempt_IsBoundAsParameterNotConcatenated(DatabaseProvider provider)
    {
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(
            provider, "dbo", "Orders", columns: null,
            filters: [new FilterCondition("Status", "eq", "'; DROP TABLE Orders --")],
            orderBy: null, top: 10);

        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("'; DROP TABLE Orders --", parameters["@p0"]);
    }
}
