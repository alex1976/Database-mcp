using System.Data.Common;
using System.Globalization;

namespace DatabaseMcp.Data;

public readonly record struct WriteResult(long RowsWritten, bool Truncated);

/// <summary>
/// Streams a <see cref="DbDataReader"/> (in practice a <c>SqlDataReader</c>) to a delimited
/// (CSV/TSV) text destination one row at a time, so large result sets never need to be
/// materialized fully in memory. Typed against the ADO.NET base class rather than
/// <c>SqlDataReader</c> so it can be exercised in unit tests with a lightweight fake reader.
/// </summary>
public static class DelimitedWriter
{
    public static async Task<WriteResult> WriteAsync(
        DbDataReader reader,
        TextWriter writer,
        char delimiter,
        long? maxRows,
        CancellationToken cancellationToken)
    {
        int fieldCount = reader.FieldCount;

        for (int i = 0; i < fieldCount; i++)
        {
            if (i > 0)
            {
                await writer.WriteAsync(delimiter).ConfigureAwait(false);
            }

            await writer.WriteAsync(Escape(reader.GetName(i), delimiter)).ConfigureAwait(false);
        }

        await writer.WriteAsync('\n').ConfigureAwait(false);

        long rowsWritten = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (maxRows.HasValue && rowsWritten >= maxRows.Value)
            {
                return new WriteResult(rowsWritten, Truncated: true);
            }

            for (int i = 0; i < fieldCount; i++)
            {
                if (i > 0)
                {
                    await writer.WriteAsync(delimiter).ConfigureAwait(false);
                }

                object value = reader.GetValue(i);
                string text = value is DBNull ? string.Empty : FormatValue(value);
                await writer.WriteAsync(Escape(text, delimiter)).ConfigureAwait(false);
            }

            await writer.WriteAsync('\n').ConfigureAwait(false);
            rowsWritten++;
        }

        return new WriteResult(rowsWritten, Truncated: false);
    }

    private static string FormatValue(object value) => value switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string field, char delimiter)
    {
        bool needsQuoting = field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        return needsQuoting ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
