using System.ComponentModel;
using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using DatabaseMcp.Security;
using ModelContextProtocol.Server;

namespace DatabaseMcp.Tools;

[McpServerToolType]
public sealed class DataTools(QueryService queryService, DatabaseOptions options)
{
    [McpServerTool(Name = "execute_sql")]
    [Description("Executes a read-only SQL SELECT statement (optionally starting with a WITH/CTE clause) and returns the result inline as CSV or TXT text, capped at a maximum row count to protect the conversation context. INSERT/UPDATE/DELETE/DDL/EXEC and multiple statements are rejected. Use export_data instead when you need the full result set written to a file.")]
    public async Task<object> ExecuteSqlAsync(
        [Description("A single SELECT (or WITH ... SELECT) statement.")] string sql,
        [Description("Output format: 'csv' or 'txt'. Defaults to 'csv'.")] string? format = null,
        [Description("Maximum rows to return inline. Defaults to the server's configured default; a hard cap is enforced regardless of this value.")] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        SqlGuard.EnsureReadOnlySelect(sql);
        OutputFormat parsedFormat = OutputFormatExtensions.Parse(format);
        InlineQueryResult result = await ToolExecution
            .RunAsync(() => queryService.ExecuteInlineAsync(sql, null, parsedFormat, maxRows, cancellationToken))
            .ConfigureAwait(false);

        return new
        {
            rowCount = result.RowCount,
            truncated = result.Truncated,
            format = parsedFormat.ToString().ToLowerInvariant(),
            content = result.Content,
        };
    }

    [McpServerTool(Name = "query_data")]
    [Description("Queries a single table or view using structured parameters (columns, filters, ordering, row limit) instead of raw SQL. Filter operators: eq, ne, gt, ge, lt, le, like, in, isnull, isnotnull; for 'in', Value is a comma-separated list. Returns results inline as CSV or TXT, capped at a maximum row count.")]
    public async Task<object> QueryDataAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table or view name.")] string table,
        [Description("Columns to return. Omit or leave empty to return all columns.")] IReadOnlyList<string>? columns = null,
        [Description("Filter conditions, combined with AND.")] IReadOnlyList<FilterCondition>? filters = null,
        [Description("Columns to sort by, applied in the given order.")] IReadOnlyList<OrderByColumn>? orderBy = null,
        [Description("Maximum rows to return. Defaults to the server's configured default; a hard cap is enforced regardless of this value.")] int? maxRows = null,
        [Description("Output format: 'csv' or 'txt'. Defaults to 'csv'.")] string? format = null,
        CancellationToken cancellationToken = default)
    {
        OutputFormat parsedFormat = OutputFormatExtensions.Parse(format);
        int top = Math.Clamp(maxRows ?? options.DefaultMaxRows, 1, options.HardMaxRows);
        (string sql, Dictionary<string, object> parameters) = StructuredQueryBuilder.Build(options.Provider, schema, table, columns, filters, orderBy, top);

        InlineQueryResult result = await ToolExecution
            .RunAsync(() => queryService.ExecuteInlineAsync(sql, parameters, parsedFormat, top, cancellationToken))
            .ConfigureAwait(false);

        return new
        {
            rowCount = result.RowCount,
            truncated = result.Truncated,
            format = parsedFormat.ToString().ToLowerInvariant(),
            generatedSql = sql,
            content = result.Content,
        };
    }

    [McpServerTool(Name = "export_data")]
    [Description("Executes a read-only SQL SELECT statement and streams the FULL result set (no row cap) to a CSV or TXT file in the server's export directory — use this for large data extraction instead of execute_sql/query_data. Returns the file path, row count, and file size rather than inline content.")]
    public async Task<object> ExportDataAsync(
        [Description("A single SELECT (or WITH ... SELECT) statement.")] string sql,
        [Description("Output format: 'csv' or 'txt'. Defaults to 'csv'.")] string? format = null,
        [Description("Optional file name (without extension). A timestamped name is generated if omitted.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        SqlGuard.EnsureReadOnlySelect(sql);
        OutputFormat parsedFormat = OutputFormatExtensions.Parse(format);
        ExportResult result = await ToolExecution
            .RunAsync(() => queryService.ExportAsync(sql, null, parsedFormat, fileName, cancellationToken))
            .ConfigureAwait(false);

        return new
        {
            filePath = result.FilePath,
            rowCount = result.RowCount,
            fileSizeBytes = result.FileSizeBytes,
            format = parsedFormat.ToString().ToLowerInvariant(),
        };
    }
}
