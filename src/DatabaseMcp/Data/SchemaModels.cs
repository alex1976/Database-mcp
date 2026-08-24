namespace DatabaseMcp.Data;

public sealed record SchemaInfo(string Name);

public sealed record TableInfo(string Schema, string Name, long ApproxRowCount);

public sealed record ViewInfo(string Schema, string Name);

public sealed record ColumnInfo(
    int OrdinalPosition,
    string Name,
    string DataType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    bool IsIdentity,
    bool IsComputed,
    string? DefaultValue);

public sealed record ForeignKeyInfo(
    string ConstraintName,
    IReadOnlyList<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);

public sealed record IndexInfo(
    string Name,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns);

public sealed record TableDetails(
    string Schema,
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<string> PrimaryKeyColumns,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<IndexInfo> Indexes,
    long ApproxRowCount);

public sealed record ViewDetails(
    string Schema,
    string Name,
    IReadOnlyList<ColumnInfo> Columns,
    string? Definition);
