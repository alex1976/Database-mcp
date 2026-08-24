using System.ComponentModel;
using DatabaseMcp.Data;
using ModelContextProtocol.Server;

namespace DatabaseMcp.Tools;

[McpServerToolType]
public sealed class SchemaTools(ISchemaService schemaService)
{
    [McpServerTool(Name = "list_schemas")]
    [Description("Lists user database schemas (excludes built-in system schemas like sys, INFORMATION_SCHEMA, guest).")]
    public async Task<IReadOnlyList<SchemaInfo>> ListSchemasAsync(CancellationToken cancellationToken = default)
        => await ToolExecution.RunAsync(() => schemaService.ListSchemasAsync(cancellationToken)).ConfigureAwait(false);

    [McpServerTool(Name = "list_tables")]
    [Description("Lists tables in the database, optionally filtered by schema, with an approximate row count for each.")]
    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(
        [Description("Schema name to filter by (e.g. 'dbo'). Omit to list tables from all schemas.")] string? schema = null,
        CancellationToken cancellationToken = default)
        => await ToolExecution.RunAsync(() => schemaService.ListTablesAsync(schema, cancellationToken)).ConfigureAwait(false);

    [McpServerTool(Name = "list_views")]
    [Description("Lists views in the database, optionally filtered by schema.")]
    public async Task<IReadOnlyList<ViewInfo>> ListViewsAsync(
        [Description("Schema name to filter by (e.g. 'dbo'). Omit to list views from all schemas.")] string? schema = null,
        CancellationToken cancellationToken = default)
        => await ToolExecution.RunAsync(() => schemaService.ListViewsAsync(schema, cancellationToken)).ConfigureAwait(false);

    [McpServerTool(Name = "describe_table")]
    [Description("Describes a table: columns (type, nullability, identity), primary key, foreign keys, indexes, and approximate row count.")]
    public async Task<object> DescribeTableAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("Table name.")] string table,
        CancellationToken cancellationToken = default)
    {
        TableDetails? details = await ToolExecution.RunAsync(() => schemaService.DescribeTableAsync(schema, table, cancellationToken))
            .ConfigureAwait(false);
        return details is null
            ? new { error = $"Table '{schema}.{table}' was not found." }
            : details;
    }

    [McpServerTool(Name = "describe_view")]
    [Description("Describes a view: its columns and, when available, its SQL definition.")]
    public async Task<object> DescribeViewAsync(
        [Description("Schema name, e.g. 'dbo'.")] string schema,
        [Description("View name.")] string view,
        CancellationToken cancellationToken = default)
    {
        ViewDetails? details = await ToolExecution.RunAsync(() => schemaService.DescribeViewAsync(schema, view, cancellationToken))
            .ConfigureAwait(false);
        return details is null
            ? new { error = $"View '{schema}.{view}' was not found." }
            : details;
    }
}
