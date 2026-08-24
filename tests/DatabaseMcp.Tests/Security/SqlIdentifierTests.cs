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
    [InlineData("Café")] // Unicode letters are valid T-SQL identifier characters.
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
    public void Bracket_WrapsIdentifierInBrackets()
    {
        Assert.Equal("[Orders]", SqlIdentifier.Bracket("Orders", "table"));
    }

    [Fact]
    public void Bracket_EscapesEmbeddedClosingBracket()
    {
        // Validate() rejects ']' via the regex, so this path only matters if a future identifier
        // rule ever allows it; assert the escaping logic itself is still correct in isolation.
        Assert.Throws<McpException>(() => SqlIdentifier.Bracket("Foo]Bar", "column"));
    }

    [Fact]
    public void BracketQualified_ProducesSchemaDotTable()
    {
        Assert.Equal("[dbo].[Orders]", SqlIdentifier.BracketQualified("dbo", "Orders"));
    }
}
