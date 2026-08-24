using DatabaseMcp.Configuration;
using DatabaseMcp.Security;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Security;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("dbo")]
    [InlineData("Orders")]
    [InlineData("_underscore")]
    [InlineData("Column1")]
    [InlineData("Café")] // Unicode letters are valid identifier characters in both engines.
    public void Validate_AcceptsValidIdentifiers(string identifier)
    {
        Assert.Equal(identifier, SqlIdentifier.Validate(identifier, "test"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1StartsWithDigit")]
    [InlineData("has space")]
    [InlineData("has;semicolon")]
    [InlineData("has'quote")]
    [InlineData("has]bracket")]
    [InlineData("has--comment")]
    [InlineData("DROP TABLE Foo")]
    public void Validate_RejectsInvalidIdentifiers(string identifier)
    {
        Assert.Throws<McpException>(() => SqlIdentifier.Validate(identifier, "test"));
    }

    [Fact]
    public void Bracket_WrapsIdentifierInBrackets_ForSqlServer()
    {
        Assert.Equal("[Orders]", SqlIdentifier.Bracket(DatabaseProvider.SqlServer, "Orders", "table"));
    }

    [Fact]
    public void Bracket_WrapsIdentifierInDoubleQuotes_ForPostgreSql()
    {
        Assert.Equal("\"Orders\"", SqlIdentifier.Bracket(DatabaseProvider.PostgreSql, "Orders", "table"));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Bracket_RejectsInvalidIdentifierRegardlessOfProvider(DatabaseProvider provider)
    {
        // Validate() rejects ']' via the regex, so this path only matters if a future identifier
        // rule ever allows it; assert the guard still fires for both providers.
        Assert.Throws<McpException>(() => SqlIdentifier.Bracket(provider, "Foo]Bar", "column"));
    }

    [Fact]
    public void BracketQualified_ProducesSchemaDotTable_ForSqlServer()
    {
        Assert.Equal("[dbo].[Orders]", SqlIdentifier.BracketQualified(DatabaseProvider.SqlServer, "dbo", "Orders"));
    }

    [Fact]
    public void BracketQualified_ProducesSchemaDotTable_ForPostgreSql()
    {
        Assert.Equal("\"public\".\"orders\"", SqlIdentifier.BracketQualified(DatabaseProvider.PostgreSql, "public", "orders"));
    }
}
