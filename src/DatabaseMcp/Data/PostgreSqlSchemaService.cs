using System.Data.Common;

namespace DatabaseMcp.Data;

/// <summary>
/// PostgreSQL implementation of <see cref="ISchemaService"/>. Uses the ANSI-standard
/// <c>information_schema</c> views wherever PostgreSQL and SQL Server agree on them (tables,
/// views, columns, primary/foreign keys), and falls back to <c>pg_catalog</c>/
/// <c>pg_stat_user_tables</c> only for the pieces that have no ANSI equivalent: indexes and
/// approximate row counts. All queries are fixed and parameterized — no user input is ever
/// concatenated into SQL text.
/// </summary>
public sealed class PostgreSqlSchemaService(SqlConnectionFactory connectionFactory) : ISchemaService
{
    public async Task<IReadOnlyList<SchemaInfo>> ListSchemasAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name NOT IN ('pg_catalog', 'information_schema')
              AND schema_name NOT LIKE 'pg\_toast%'
              AND schema_name NOT LIKE 'pg\_temp%'
            ORDER BY schema_name;
            """;

        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = CreateCommand(connection, sql);

        var results = new List<SchemaInfo>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SchemaInfo(reader.GetString(0)));
        }

        return results;
    }

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(string? schema, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT t.table_schema, t.table_name, COALESCE(s.n_live_tup, 0) AS approx_row_count
            FROM information_schema.tables t
            LEFT JOIN pg_stat_user_tables s
                ON s.schemaname = t.table_schema AND s.relname = t.table_name
            WHERE t.table_type = 'BASE TABLE'
              AND (@schema::text IS NULL OR t.table_schema = @schema::text)
            ORDER BY t.table_schema, t.table_name;
            """;

        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = CreateCommand(connection, sql, ("@schema", (object?)schema ?? DBNull.Value));

        var results = new List<TableInfo>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new TableInfo(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ViewInfo>> ListViewsAsync(string? schema, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT table_schema, table_name
            FROM information_schema.views
            WHERE (@schema::text IS NULL OR table_schema = @schema::text)
            ORDER BY table_schema, table_name;
            """;

        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbCommand command = CreateCommand(connection, sql, ("@schema", (object?)schema ?? DBNull.Value));

        var results = new List<ViewInfo>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ViewInfo(reader.GetString(0), reader.GetString(1)));
        }

        return results;
    }

    public async Task<TableDetails?> DescribeTableAsync(string schema, string table, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        bool exists = await TableExistsAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return null;
        }

        IReadOnlyList<ColumnInfo> columns = await ReadColumnsAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> primaryKey = await ReadPrimaryKeyAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ForeignKeyInfo> foreignKeys = await ReadForeignKeysAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<IndexInfo> indexes = await ReadIndexesAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);
        long approxRowCount = await ReadApproxRowCountAsync(connection, schema, table, cancellationToken).ConfigureAwait(false);

        return new TableDetails(schema, table, columns, primaryKey, foreignKeys, indexes, approxRowCount);
    }

    public async Task<ViewDetails?> DescribeViewAsync(string schema, string view, CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string definitionSql = """
            SELECT view_definition
            FROM information_schema.views
            WHERE table_schema = @schema AND table_name = @view;
            """;
        await using DbCommand definitionCommand = CreateCommand(connection, definitionSql, ("@schema", schema), ("@view", view));
        object? definitionResult = await definitionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (definitionResult is null)
        {
            return null;
        }

        IReadOnlyList<ColumnInfo> columns = await ReadColumnsAsync(connection, schema, view, cancellationToken).ConfigureAwait(false);
        string? definition = definitionResult as string;

        return new ViewDetails(schema, view, columns, definition);
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table AND table_type = 'BASE TABLE';
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ordinal_position, column_name, data_type, character_maximum_length,
                   numeric_precision, numeric_scale, is_nullable, is_identity, is_generated,
                   column_default
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position;
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));

        var results = new List<ColumnInfo>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new ColumnInfo(
                OrdinalPosition: reader.GetInt32(0),
                Name: reader.GetString(1),
                DataType: reader.GetString(2),
                MaxLength: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Precision: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Scale: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsNullable: reader.GetString(6) == "YES",
                IsIdentity: reader.GetString(7) == "YES",
                IsComputed: reader.GetString(8) != "NEVER",
                DefaultValue: reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<string>> ReadPrimaryKeyAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = @schema AND tc.table_name = @table
            ORDER BY kcu.ordinal_position;
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));

        var results = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        // Note: information_schema doesn't reliably pair up referencing/referenced columns for
        // composite foreign keys across engines. This is correct for the common case of
        // single-column foreign keys; composite FKs may report columns out of order.
        const string sql = """
            SELECT tc.constraint_name, kcu.column_name,
                   ccu.table_schema AS ref_schema, ccu.table_name AS ref_table, ccu.column_name AS ref_column,
                   kcu.ordinal_position
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
            JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name AND ccu.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = @schema AND tc.table_name = @table
            ORDER BY tc.constraint_name, kcu.ordinal_position;
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));

        var grouped = new List<(string ConstraintName, string Column, string RefSchema, string RefTable, string RefColumn)>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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

    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ix.relname AS index_name, idx.indisunique, idx.indisprimary,
                   a.attname AS column_name, array_position(idx.indkey, a.attnum) AS key_ordinal
            FROM pg_index idx
            JOIN pg_class t ON t.oid = idx.indrelid
            JOIN pg_class ix ON ix.oid = idx.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(idx.indkey)
            WHERE n.nspname = @schema AND t.relname = @table
            ORDER BY ix.relname, key_ordinal;
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));

        var grouped = new List<(string Name, bool IsUnique, bool IsPrimaryKey, string Column)>();
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            grouped.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetString(3)));
        }

        return grouped
            .GroupBy(row => (row.Name, row.IsUnique, row.IsPrimaryKey))
            .Select(g => new IndexInfo(g.Key.Name, g.Key.IsUnique, g.Key.IsPrimaryKey, g.Select(r => r.Column).ToList()))
            .ToList();
    }

    private static async Task<long> ReadApproxRowCountAsync(DbConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT n_live_tup
            FROM pg_stat_user_tables
            WHERE schemaname = @schema AND relname = @table;
            """;

        await using DbCommand command = CreateCommand(connection, sql, ("@schema", schema), ("@table", table));
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
