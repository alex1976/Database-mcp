using DatabaseMcp.Security;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Security;

public class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM dbo.Orders")]
    [InlineData("  select Id from dbo.Orders  ")]
    [InlineData("WITH Cte AS (SELECT 1 AS N) SELECT * FROM Cte")]
    [InlineData("SELECT * FROM dbo.Orders;")]
    [InlineData("SELECT * FROM dbo.Orders -- trailing comment")]
    [InlineData("SELECT * FROM dbo.Orders /* block comment */ WHERE Id = 1")]
    public void EnsureReadOnlySelect_AllowsSelectAndCte(string sql)
    {
        SqlGuard.EnsureReadOnlySelect(sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureReadOnlySelect_RejectsEmpty(string sql)
    {
        Assert.Throws<McpException>(() => SqlGuard.EnsureReadOnlySelect(sql));
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Orders (Id) VALUES (1)")]
    [InlineData("UPDATE dbo.Orders SET Id = 1")]
    [InlineData("DELETE FROM dbo.Orders")]
    [InlineData("DROP TABLE dbo.Orders")]
    [InlineData("ALTER TABLE dbo.Orders ADD Foo INT")]
    [InlineData("CREATE TABLE dbo.Foo (Id INT)")]
    [InlineData("TRUNCATE TABLE dbo.Orders")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE sp_who")]
    [InlineData("MERGE dbo.Orders USING dbo.Staging ON 1=1 WHEN MATCHED THEN DELETE;")]
    [InlineData("SELECT * INTO dbo.Copy FROM dbo.Orders")]
    [InlineData("BACKUP DATABASE Foo TO DISK = 'x'")]
    public void EnsureReadOnlySelect_RejectsWriteAndDdlStatements(string sql)
    {
        // Statements that don't start with SELECT/WITH are rejected by that check before the
        // keyword denylist is even consulted; "SELECT * INTO ..." starts with SELECT and is
        // instead caught by the denylist. Either way, an McpException must be thrown.
        Assert.Throws<McpException>(() => SqlGuard.EnsureReadOnlySelect(sql));
    }

    [Fact]
    public void EnsureReadOnlySelect_RejectsMultipleStatements()
    {
        Assert.Throws<McpException>(() => SqlGuard.EnsureReadOnlySelect("SELECT 1; SELECT 2"));
    }

    [Fact]
    public void EnsureReadOnlySelect_RejectsNonSelectStart()
    {
        Assert.Throws<McpException>(() => SqlGuard.EnsureReadOnlySelect("sp_who"));
    }

    [Fact]
    public void EnsureReadOnlySelect_KeywordInsideCommentDoesNotTriggerRejection()
    {
        // Comments are stripped before keyword scanning. This is safe, not a bypass: SQL Server
        // itself treats "-- ..." as inert text, so a keyword inside a comment can never execute.
        SqlGuard.EnsureReadOnlySelect("SELECT 1 -- DROP TABLE Foo");
    }

    [Fact]
    public void EnsureReadOnlySelect_StatementAfterCommentIsStillValidated()
    {
        // Content outside the comment is real SQL and must still be caught.
        Assert.Throws<McpException>(() => SqlGuard.EnsureReadOnlySelect("SELECT 1 /* comment */; DROP TABLE Foo"));
    }
}
