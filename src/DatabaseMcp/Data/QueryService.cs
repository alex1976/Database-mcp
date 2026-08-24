using System.Text;
using DatabaseMcp.Configuration;
using Microsoft.Data.SqlClient;

namespace DatabaseMcp.Data;

public sealed record InlineQueryResult(string Content, long RowCount, bool Truncated, OutputFormat Format);

public sealed record ExportResult(string FilePath, long RowCount, long FileSizeBytes, OutputFormat Format);

/// <summary>
/// Executes read-only SELECT statements against SQL Server and streams the results either as an
/// inline, row-capped CSV/TXT string (for exploration) or as a file on disk (for full extraction).
/// </summary>
public sealed class QueryService(SqlConnectionFactory connectionFactory, DatabaseOptions options)
{
    public async Task<InlineQueryResult> ExecuteInlineAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        OutputFormat format,
        int? requestedMaxRows,
        CancellationToken cancellationToken)
    {
        int effectiveMax = Math.Clamp(requestedMaxRows ?? options.DefaultMaxRows, 1, options.HardMaxRows);

        await using SqlConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqlCommand command = BuildCommand(connection, sql, parameters);

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        using var stringWriter = new StringWriter();
        WriteResult result = await DelimitedWriter.WriteAsync(reader, stringWriter, format.Delimiter(), effectiveMax, cancellationToken)
            .ConfigureAwait(false);

        return new InlineQueryResult(stringWriter.ToString(), result.RowsWritten, result.Truncated, format);
    }

    public async Task<ExportResult> ExportAsync(
        string sql,
        IReadOnlyDictionary<string, object>? parameters,
        OutputFormat format,
        string? requestedFileName,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.ExportDirectory);
        string fullPath = ResolveExportPath(requestedFileName, format);

        await using SqlConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqlCommand command = BuildCommand(connection, sql, parameters);

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream fileStream = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        await using var streamWriter = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteResult result = await DelimitedWriter.WriteAsync(reader, streamWriter, format.Delimiter(), maxRows: null, cancellationToken)
            .ConfigureAwait(false);
        await streamWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

        long fileSize = new FileInfo(fullPath).Length;
        return new ExportResult(fullPath, result.RowsWritten, fileSize, format);
    }

    private SqlCommand BuildCommand(SqlConnection connection, string sql, IReadOnlyDictionary<string, object>? parameters)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = options.CommandTimeoutSeconds };
        if (parameters is not null)
        {
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }
        }

        return command;
    }

    private string ResolveExportPath(string? requestedFileName, OutputFormat format)
    {
        string baseName = string.IsNullOrWhiteSpace(requestedFileName)
            ? $"export_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}"
            : Path.GetFileNameWithoutExtension(Path.GetFileName(requestedFileName));

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        string fileName = $"{baseName}.{format.FileExtension()}";
        return Path.Combine(options.ExportDirectory, fileName);
    }
}
