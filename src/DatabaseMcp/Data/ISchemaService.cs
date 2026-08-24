namespace DatabaseMcp.Data;

/// <summary>
/// Read-only navigation of database metadata (schemas, tables, views, columns, keys, indexes).
/// Implemented once per supported engine (see <see cref="SqlServerSchemaService"/> and
/// <see cref="PostgreSqlSchemaService"/>) because the system catalogs differ enough between SQL
/// Server and PostgreSQL that a single ANSI-only query set can't recover everything (identity
/// columns, indexes, approximate row counts).
/// </summary>
public interface ISchemaService
{
    Task<IReadOnlyList<SchemaInfo>> ListSchemasAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TableInfo>> ListTablesAsync(string? schema, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ViewInfo>> ListViewsAsync(string? schema, CancellationToken cancellationToken = default);

    Task<TableDetails?> DescribeTableAsync(string schema, string table, CancellationToken cancellationToken = default);

    Task<ViewDetails?> DescribeViewAsync(string schema, string view, CancellationToken cancellationToken = default);
}
