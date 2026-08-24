using System.Text;
using DatabaseMcp.Security;
using ModelContextProtocol;

namespace DatabaseMcp.Data;

public sealed record FilterCondition(string Column, string Operator, string? Value);

public sealed record OrderByColumn(string Column, bool Descending);

/// <summary>
/// Builds a parameterized, read-only SELECT from structured inputs (table/columns/filters/order),
/// so callers who don't want to write raw SQL still get safe, efficient queries. All identifiers
/// are validated and bracketed via <see cref="SqlIdentifier"/>; all values are bound as parameters.
/// </summary>
public static class StructuredQueryBuilder
{
    private static readonly Dictionary<string, string> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = "=",
        ["ne"] = "<>",
        ["gt"] = ">",
        ["ge"] = ">=",
        ["lt"] = "<",
        ["le"] = "<=",
        ["like"] = "LIKE",
        ["in"] = "IN",
        ["isnull"] = "IS NULL",
        ["isnotnull"] = "IS NOT NULL",
    };

    public static (string Sql, Dictionary<string, object> Parameters) Build(
        string schema,
        string table,
        IReadOnlyList<string>? columns,
        IReadOnlyList<FilterCondition>? filters,
        IReadOnlyList<OrderByColumn>? orderBy,
        int top)
    {
        string tableRef = SqlIdentifier.BracketQualified(schema, table);
        string columnList = columns is { Count: > 0 }
            ? string.Join(", ", columns.Select(c => SqlIdentifier.Bracket(c, "column")))
            : "*";

        var parameters = new Dictionary<string, object>();
        var sql = new StringBuilder()
            .Append("SELECT TOP (").Append(top).Append(") ").Append(columnList)
            .Append(" FROM ").Append(tableRef);

        if (filters is { Count: > 0 })
        {
            var clauses = new List<string>();
            int index = 0;

            foreach (FilterCondition filter in filters)
            {
                string columnRef = SqlIdentifier.Bracket(filter.Column, "column");
                if (!Operators.TryGetValue(filter.Operator, out string? sqlOperator))
                {
                    throw new McpException(
                        $"Unsupported filter operator '{filter.Operator}'. Valid operators: {string.Join(", ", Operators.Keys)}.");
                }

                switch (filter.Operator.ToLowerInvariant())
                {
                    case "isnull":
                    case "isnotnull":
                        clauses.Add($"{columnRef} {sqlOperator}");
                        break;

                    case "in":
                        string[] values = (filter.Value ?? string.Empty)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        if (values.Length == 0)
                        {
                            throw new McpException($"Filter on '{filter.Column}' with operator 'in' requires a comma-separated value list.");
                        }

                        var placeholders = new List<string>(values.Length);
                        foreach (string value in values)
                        {
                            string paramName = $"@p{index++}";
                            parameters[paramName] = value;
                            placeholders.Add(paramName);
                        }

                        clauses.Add($"{columnRef} IN ({string.Join(", ", placeholders)})");
                        break;

                    default:
                        string singleParam = $"@p{index++}";
                        parameters[singleParam] = (object?)filter.Value ?? DBNull.Value;
                        clauses.Add($"{columnRef} {sqlOperator} {singleParam}");
                        break;
                }
            }

            sql.Append(" WHERE ").Append(string.Join(" AND ", clauses));
        }

        if (orderBy is { Count: > 0 })
        {
            IEnumerable<string> orderClauses = orderBy.Select(o =>
                $"{SqlIdentifier.Bracket(o.Column, "column")} {(o.Descending ? "DESC" : "ASC")}");
            sql.Append(" ORDER BY ").Append(string.Join(", ", orderClauses));
        }

        sql.Append(';');
        return (sql.ToString(), parameters);
    }
}
