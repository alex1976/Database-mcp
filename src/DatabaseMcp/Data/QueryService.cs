using System.Data.Common;
using System.Text;
using DatabaseMcp.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace DatabaseMcp.Data;

public sealed record InlineQueryResult(string Content, long RowCount, bool Truncated, OutputFormat Format);

public sealed record ExportResult(string FilePath, long RowCount, long FileSizeBytes, OutputFormat Format);

/// <summary>
/// Executes read-only SELECT statements against the configured database (SQL Server or
/// PostgreSQL) and streams the results either as an inline, row-capped CSV/TXT string (for
/// exploration) or as a file on disk (for full extraction). Written against the ADO.NET base
/// types so it works identically regardless of provider.
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

        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = BuildCommand(connection, sql, parameters);

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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

        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = BuildCommand(connection, sql, parameters);

        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream fileStream = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
        await using var streamWriter = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        WriteResult result = await DelimitedWriter.WriteAsync(reader, streamWriter, format.Delimiter(), maxRows: null, cancellationToken)
            .ConfigureAwait(false);
        await streamWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

        long fileSize = new FileInfo(fullPath).Length;
        return new ExportResult(fullPath, result.RowsWritten, fileSize, format);
    }

    private DbCommand BuildCommand(DbConnection connection, string sql, IReadOnlyDictionary<string, object>? parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        if (parameters is not null)
        {
            foreach ((string name, object value) in parameters)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;

                // StructuredQueryBuilder only ever produces string-valued filter parameters (the
                // MCP tool arguments are all strings). Npgsql normally binds a string parameter as
                // "text", which blocks PostgreSQL's usual implicit cast (e.g. comparing it to a
                // numeric/date column fails with "operator does not exist: numeric > text") even
                // though the equivalent literal ('5' > amount) would work fine. Declaring it
                // "Unknown" instead makes Npgsql send it the same way a literal is sent, letting
                // PostgreSQL infer the type from context — matching SQL Server's implicit
                // conversion behavior for the same query.
                if (parameter is NpgsqlParameter npgsqlParameter && value is string)
                {
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Unknown;
                }

                command.Parameters.Add(parameter);
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
