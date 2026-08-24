using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace DatabaseMcp.Security;

/// <summary>
/// Validates and safely brackets SQL Server identifiers (schema/table/column names) that are
/// interpolated into dynamically built SQL. Values (filters) must never go through this class —
/// they belong in parameters instead.
/// </summary>
public static partial class SqlIdentifier
{
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*$")]
    private static partial Regex ValidIdentifier();

    public static string Validate(string identifier, string what)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !ValidIdentifier().IsMatch(identifier))
        {
            throw new McpException($"Invalid {what} name: '{identifier}'. Identifiers must start with a letter or underscore and contain only letters, digits, and underscores.");
        }

        return identifier;
    }

    /// <summary>Validates and wraps an identifier in brackets, e.g. Name -> [Name].</summary>
    public static string Bracket(string identifier, string what) =>
        $"[{Validate(identifier, what).Replace("]", "]]")}]";

    /// <summary>Validates and brackets a schema.table pair, e.g. dbo, Orders -> [dbo].[Orders].</summary>
    public static string BracketQualified(string schema, string table) =>
        $"{Bracket(schema, "schema")}.{Bracket(table, "table")}";
}
