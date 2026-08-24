using System.Data;
using System.Data.Common;
using DatabaseMcp.Data;

namespace DatabaseMcp.Tests.Data;

public class DelimitedWriterTests
{
    private static DbDataReader CreateReader(DataTable table) => table.CreateDataReader();

    private static DataTable BuildTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach ((string name, Type type) in columns)
        {
            table.Columns.Add(name, type);
        }

        return table;
    }

    [Fact]
    public async Task WriteAsync_WritesHeaderAndRows_CsvDelimiter()
    {
        DataTable table = BuildTable(("Id", typeof(int)), ("Name", typeof(string)));
        table.Rows.Add(1, "Alice");
        table.Rows.Add(2, "Bob");

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        WriteResult result = await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: null, CancellationToken.None);

        Assert.Equal(2, result.RowsWritten);
        Assert.False(result.Truncated);
        Assert.Equal("Id,Name\n1,Alice\n2,Bob\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_UsesTabDelimiter_WhenRequested()
    {
        DataTable table = BuildTable(("Id", typeof(int)), ("Name", typeof(string)));
        table.Rows.Add(1, "Alice");

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        await DelimitedWriter.WriteAsync(reader, writer, '\t', maxRows: null, CancellationToken.None);

        Assert.Equal("Id\tName\n1\tAlice\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_QuotesFieldsContainingDelimiterQuotesOrNewlines()
    {
        DataTable table = BuildTable(("Text", typeof(string)));
        table.Rows.Add("has,comma");
        table.Rows.Add("has\"quote");
        table.Rows.Add("has\nnewline");

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: null, CancellationToken.None);

        Assert.Equal("Text\n\"has,comma\"\n\"has\"\"quote\"\n\"has\nnewline\"\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_RendersDbNullAsEmptyString()
    {
        DataTable table = BuildTable(("Value", typeof(string)));
        table.Rows.Add(DBNull.Value);

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: null, CancellationToken.None);

        Assert.Equal("Value\n\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_FormatsDateTimeInvariantly()
    {
        DataTable table = BuildTable(("When", typeof(DateTime)));
        table.Rows.Add(new DateTime(2026, 8, 24, 13, 5, 30, 250));

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: null, CancellationToken.None);

        Assert.Equal("When\n2026-08-24 13:05:30.250\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_StopsAtMaxRowsAndReportsTruncated()
    {
        DataTable table = BuildTable(("Id", typeof(int)));
        for (int i = 1; i <= 5; i++)
        {
            table.Rows.Add(i);
        }

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        WriteResult result = await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: 3, CancellationToken.None);

        Assert.Equal(3, result.RowsWritten);
        Assert.True(result.Truncated);
        Assert.Equal("Id\n1\n2\n3\n", writer.ToString());
    }

    [Fact]
    public async Task WriteAsync_ExactlyMaxRows_IsNotReportedAsTruncated()
    {
        DataTable table = BuildTable(("Id", typeof(int)));
        table.Rows.Add(1);
        table.Rows.Add(2);

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        WriteResult result = await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: 2, CancellationToken.None);

        Assert.Equal(2, result.RowsWritten);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task WriteAsync_EmptyResultSet_WritesOnlyHeader()
    {
        DataTable table = BuildTable(("Id", typeof(int)));

        using DbDataReader reader = CreateReader(table);
        using var writer = new StringWriter();

        WriteResult result = await DelimitedWriter.WriteAsync(reader, writer, ',', maxRows: null, CancellationToken.None);

        Assert.Equal(0, result.RowsWritten);
        Assert.False(result.Truncated);
        Assert.Equal("Id\n", writer.ToString());
    }
}
