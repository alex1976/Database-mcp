using System.Data.Common;
using System.Globalization;
using System.Text;

namespace DatabaseMcp.Data;

public readonly record struct WriteResult(long RowsWritten, bool Truncated);

/// <summary>
/// Streams a <see cref="DbDataReader"/> (in practice a <c>SqlDataReader</c>) to a delimited
/// (CSV/TSV) text destination, so large result sets never need to be materialized fully in memory.
/// Rows are formatted into a reusable buffer and handed to the destination in chunks of roughly
/// <see cref="FlushThreshold"/> characters, rather than issuing one write per field. Values are
/// formatted straight into that buffer, so numbers and dates do not allocate intermediate strings.
/// Typed against the ADO.NET base class rather than <c>SqlDataReader</c> so it can be exercised in
/// unit tests with a lightweight fake reader.
/// </summary>
public static class DelimitedWriter
{
    private const int FlushThreshold = 64 * 1024;

    public static async Task<WriteResult> WriteAsync(
        DbDataReader reader,
        TextWriter writer,
        char delimiter,
        long? maxRows,
        CancellationToken cancellationToken)
    {
        int fieldCount = reader.FieldCount;
        var buffer = new StringBuilder(FlushThreshold + 4 * 1024);

        for (int i = 0; i < fieldCount; i++)
        {
            if (i > 0)
            {
                buffer.Append(delimiter);
            }

            AppendEscaped(buffer, reader.GetName(i), delimiter);
        }

        buffer.Append('\n');

        long rowsWritten = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (maxRows.HasValue && rowsWritten >= maxRows.Value)
            {
                await FlushAsync(writer, buffer, cancellationToken).ConfigureAwait(false);
                return new WriteResult(rowsWritten, Truncated: true);
            }

            for (int i = 0; i < fieldCount; i++)
            {
                if (i > 0)
                {
                    buffer.Append(delimiter);
                }

                AppendValue(buffer, reader.GetValue(i), delimiter);
            }

            buffer.Append('\n');
            rowsWritten++;

            if (buffer.Length >= FlushThreshold)
            {
                await FlushAsync(writer, buffer, cancellationToken).ConfigureAwait(false);
            }
        }

        await FlushAsync(writer, buffer, cancellationToken).ConfigureAwait(false);
        return new WriteResult(rowsWritten, Truncated: false);
    }

    private static async Task FlushAsync(TextWriter writer, StringBuilder buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Clear();
    }

    private static void AppendValue(StringBuilder buffer, object value, char delimiter)
    {
        switch (value)
        {
            case DBNull:
                return;

            case string text:
                AppendEscaped(buffer, text, delimiter);
                return;

            case DateTime dt:
                AppendFormatted(buffer, dt, "yyyy-MM-dd HH:mm:ss.fff", delimiter);
                return;

            case DateTimeOffset dto:
                AppendFormatted(buffer, dto, "yyyy-MM-dd HH:mm:ss.fffzzz", delimiter);
                return;

            case ISpanFormattable spanFormattable:
                AppendSpanFormatted(buffer, spanFormattable, delimiter);
                return;

            case IFormattable formattable:
                AppendEscaped(buffer, formattable.ToString(null, CultureInfo.InvariantCulture), delimiter);
                return;

            default:
                AppendEscaped(buffer, value.ToString() ?? string.Empty, delimiter);
                return;
        }
    }

    /// <summary>Formats with a custom pattern directly into a stack buffer; falls back to a string if it does not fit.</summary>
    private static void AppendFormatted<T>(StringBuilder buffer, T value, string format, char delimiter)
        where T : IFormattable, ISpanFormattable
    {
        Span<char> chars = stackalloc char[64];
        if (value.TryFormat(chars, out int written, format, CultureInfo.InvariantCulture))
        {
            AppendEscaped(buffer, chars[..written], delimiter);
        }
        else
        {
            AppendEscaped(buffer, value.ToString(format, CultureInfo.InvariantCulture), delimiter);
        }
    }

    private static void AppendSpanFormatted(StringBuilder buffer, ISpanFormattable value, char delimiter)
    {
        Span<char> chars = stackalloc char[64];
        if (value.TryFormat(chars, out int written, default, CultureInfo.InvariantCulture))
        {
            AppendEscaped(buffer, chars[..written], delimiter);
        }
        else
        {
            AppendEscaped(buffer, value.ToString(null, CultureInfo.InvariantCulture), delimiter);
        }
    }

    /// <summary>Appends a field, quoting it (and doubling embedded quotes) only when the delimiter, a quote or a line break requires it.</summary>
    private static void AppendEscaped(StringBuilder buffer, ReadOnlySpan<char> field, char delimiter)
    {
        if (field.IndexOf(delimiter) < 0 && field.IndexOfAny('"', '\n', '\r') < 0)
        {
            buffer.Append(field);
            return;
        }

        buffer.Append('"');
        foreach (char c in field)
        {
            if (c == '"')
            {
                buffer.Append('"');
            }

            buffer.Append(c);
        }

        buffer.Append('"');
    }
}
