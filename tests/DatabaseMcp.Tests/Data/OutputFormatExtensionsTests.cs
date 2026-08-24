using DatabaseMcp.Data;
using ModelContextProtocol;

namespace DatabaseMcp.Tests.Data;

public class OutputFormatExtensionsTests
{
    [Theory]
    [InlineData(null, OutputFormat.Csv)]
    [InlineData("", OutputFormat.Csv)]
    [InlineData("csv", OutputFormat.Csv)]
    [InlineData("CSV", OutputFormat.Csv)]
    [InlineData("txt", OutputFormat.Txt)]
    [InlineData("TXT", OutputFormat.Txt)]
    [InlineData("tsv", OutputFormat.Txt)]
    [InlineData("text", OutputFormat.Txt)]
    [InlineData(" csv ", OutputFormat.Csv)]
    public void Parse_RecognizesSupportedValues(string? input, OutputFormat expected)
    {
        Assert.Equal(expected, OutputFormatExtensions.Parse(input));
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("json")]
    [InlineData("xlsx")]
    public void Parse_RejectsUnsupportedValues(string input)
    {
        Assert.Throws<McpException>(() => OutputFormatExtensions.Parse(input));
    }

    [Fact]
    public void Delimiter_MatchesFormat()
    {
        Assert.Equal(',', OutputFormat.Csv.Delimiter());
        Assert.Equal('\t', OutputFormat.Txt.Delimiter());
    }

    [Fact]
    public void FileExtension_MatchesFormat()
    {
        Assert.Equal("csv", OutputFormat.Csv.FileExtension());
        Assert.Equal("txt", OutputFormat.Txt.FileExtension());
    }
}
