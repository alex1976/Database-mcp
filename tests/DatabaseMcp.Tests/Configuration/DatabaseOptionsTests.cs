using DatabaseMcp.Configuration;

namespace DatabaseMcp.Tests.Configuration;

public class DatabaseOptionsTests
{
    [Fact]
    public void ParsePostgresSslMode_DefaultsToPrefer_WhenUnset()
    {
        Assert.Equal("Prefer", DatabaseOptions.ParsePostgresSslMode(null));
        Assert.Equal("Prefer", DatabaseOptions.ParsePostgresSslMode("   "));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Require")]
    [InlineData("verifyfull")]
    [InlineData(" VerifyCA ")]
    public void ParsePostgresSslMode_AcceptsKnownModes(string raw)
    {
        Assert.Equal(raw.Trim(), DatabaseOptions.ParsePostgresSslMode(raw));
    }

    [Theory]
    [InlineData("Require2")]
    [InlineData("ssl")]
    [InlineData("3")]
    public void ParsePostgresSslMode_RejectsUnknownModes_AtStartup(string raw)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseOptions.ParsePostgresSslMode(raw));

        Assert.Contains("POSTGRES_SSL_MODE", ex.Message);
        Assert.Contains("VerifyFull", ex.Message);
    }
}
