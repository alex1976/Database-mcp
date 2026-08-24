using Microsoft.Data.SqlClient;

namespace DatabaseMcp.Data;

/// <summary>
/// Read-only navigation of database metadata (schemas, tables, views, columns, keys, indexes)
/// using SQL Server catalog views. All queries here are fixed and parameterized — no user input
/// is ever concatenated into SQL text.
/// </summary>
public sealed class SchemaService(SqlConnectionFactory connectionFactory)
{
    private static readonly string[] SystemSchemas =
    [
        "sys", "INFORMATION_SCHEMA", "guest",
        "db_owner", "db_accessadmin", "db_securityadmin", "db_ddladmin",
        "db_backupoperator", "db_datareader", "db_datawriter",
        "db_denydatareader", "db_denydatawriter",
    ];

    public async Task<IReadOnlyList<SchemaInfo>> ListSchemasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.name AS SchemaName
            FROM sys.schemas s
            WHERE s.name NOT IN (SELECT value FROM STRING_SPLIT(@excluded, ','))
            ORDER BY s.name;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@excluded", string.Join(',', SystemSchemas));

        var results = new List<SchemaInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SchemaInfo(reader.GetString(0)));
        }

        return results;
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(string? schema, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.name AS SchemaName, t.name AS TableName,
                   ISNULL(SUM(p.rows), 0) AS ApproxRowCount
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
            WHERE (@schema IS NULL OR s.name = @schema)
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", (object?)schema ?? DBNull.Value);

        var results = new List<TableInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TableInfo(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ViewInfo>> ListViewsAsync(string? schema, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT s.name AS SchemaName, v.name AS ViewName
            FROM sys.views v
            JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, v.name;
            """;

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", (object?)schema ?? DBNull.Value);

        var results = new List<ViewInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ViewInfo(reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    public async Task<TableDetails?> DescribeTableAsync(string schema, string table, CancellationToken cancellationToken = default)
    {
        string fullName = $"{schema}.{table}";

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        int? objectId = await ResolveObjectIdAsync(connection, fullName, "U", cancellationToken).ConfigureAwait(false);
        if (objectId is null)
        {
            return null;
        }

        var columns = await ReadColumnsAsync(connection, fullName, cancellationToken).ConfigureAwait(false);
        var primaryKey = await ReadPrimaryKeyAsync(connection, fullName, cancellationToken).ConfigureAwait(false);
        var foreignKeys = await ReadForeignKeysAsync(connection, fullName, cancellationToken).ConfigureAwait(false);
        var indexes = await ReadIndexesAsync(connection, fullName, cancellationToken).ConfigureAwait(false);
        long approxRowCount = await ReadApproxRowCountAsync(connection, fullName, cancellationToken).ConfigureAwait(false);

        return new TableDetails(schema, table, columns, primaryKey, foreignKeys, indexes, approxRowCount);
    }

    public async Task<ViewDetails?> DescribeViewAsync(string schema, string view, CancellationToken cancellationToken = default)
    {
        string fullName = $"{schema}.{view}";

        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        int? objectId = await ResolveObjectIdAsync(connection, fullName, "V", cancellationToken).ConfigureAwait(false);
        if (objectId is null)
        {
            return null;
        }

        var columns = await ReadColumnsAsync(connection, fullName, cancellationToken).ConfigureAwait(false);

        const string definitionSql = "SELECT OBJECT_DEFINITION(OBJECT_ID(@fullName));";
        await using var definitionCommand = new SqlCommand(definitionSql, connection);
        definitionCommand.Parameters.AddWithValue("@fullName", fullName);
        object? definitionResult = await definitionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        string? definition = definitionResult as string;

        return new ViewDetails(schema, view, columns, definition);
    }

    private static async Task<int?> ResolveObjectIdAsync(SqlConnection connection, string fullName, string objectType, CancellationToken cancellationToken)
    {
        const string sql = "SELECT OBJECT_ID(@fullName, @objectType);";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);
        command.Parameters.AddWithValue("@objectType", objectType);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int id ? id : null;
    }

    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(SqlConnection connection, string fullName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.column_id, c.name, ty.name AS DataType, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.is_computed, dc.definition AS DefaultValue
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE c.object_id = OBJECT_ID(@fullName)
            ORDER BY c.column_id;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);

        var results = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ColumnInfo(
                OrdinalPosition: reader.GetInt32(0),
                Name: reader.GetString(1),
                DataType: reader.GetString(2),
                MaxLength: reader.IsDBNull(3) ? null : reader.GetInt16(3),
                Precision: reader.IsDBNull(4) ? null : reader.GetByte(4),
                Scale: reader.IsDBNull(5) ? null : reader.GetByte(5),
                IsNullable: reader.GetBoolean(6),
                IsIdentity: reader.GetBoolean(7),
                IsComputed: reader.GetBoolean(8),
                DefaultValue: reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(SqlConnection connection, string fullName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@fullName) AND i.is_primary_key = 1
            ORDER BY ic.key_ordinal;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(SqlConnection connection, string fullName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT fk.name AS ConstraintName, c1.name AS ColumnName,
                   s2.name AS RefSchema, t2.name AS RefTable, c2.name AS RefColumnName,
                   fkc.constraint_column_id
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns c1 ON c1.object_id = fkc.parent_object_id AND c1.column_id = fkc.parent_column_id
            JOIN sys.tables t2 ON t2.object_id = fk.referenced_object_id
            JOIN sys.schemas s2 ON s2.schema_id = t2.schema_id
            JOIN sys.columns c2 ON c2.object_id = fkc.referenced_object_id AND c2.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(@fullName)
            ORDER BY fk.name, fkc.constraint_column_id;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);

        var grouped = new List<(string ConstraintName, string Column, string RefSchema, string RefTable, string RefColumn)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            grouped.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return grouped
            .GroupBy(row => (row.ConstraintName, row.RefSchema, row.RefTable))
            .Select(g => new ForeignKeyInfo(
                ConstraintName: g.Key.ConstraintName,
                Columns: g.Select(r => r.Column).ToList(),
                ReferencedSchema: g.Key.RefSchema,
                ReferencedTable: g.Key.RefTable,
                ReferencedColumns: g.Select(r => r.RefColumn).ToList()))
            .ToList();
    }

    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(SqlConnection connection, string fullName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT i.name AS IndexName, i.is_unique, i.is_primary_key, c.name AS ColumnName
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@fullName) AND i.name IS NOT NULL
            ORDER BY i.name, ic.key_ordinal;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);

        var grouped = new List<(string Name, bool IsUnique, bool IsPrimaryKey, string Column)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            grouped.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3)));
        }

        return grouped
            .GroupBy(row => (row.Name, row.IsUnique, row.IsPrimaryKey))
            .Select(g => new IndexInfo(g.Key.Name, g.Key.IsUnique, g.Key.IsPrimaryKey, g.Select(r => r.Column).ToList()))
            .ToList();
    }

    private static async Task<long> ReadApproxRowCountAsync(SqlConnection connection, string fullName, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ISNULL(SUM(p.rows), 0)
            FROM sys.partitions p
            WHERE p.object_id = OBJECT_ID(@fullName) AND p.index_id IN (0, 1);
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@fullName", fullName);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value ? value : Convert.ToInt64(result);
    }
}
