using System.Text.RegularExpressions;
using DatabaseMcp.Configuration;
using ModelContextProtocol;

namespace DatabaseMcp.Security;

/// <summary>
/// Validates and safely quotes schema/table/column identifiers that are interpolated into
/// dynamically built SQL, using the correct quoting style for the target engine (SQL Server
/// brackets vs. PostgreSQL double quotes). Values (filters) must never go through this class —
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

    /// <summary>Validates and quotes an identifier, e.g. Name -> [Name] (SQL Server) or "Name" (PostgreSQL).</summary>
    public static string Bracket(DatabaseProvider provider, string identifier, string what)
    {
        string validated = Validate(identifier, what);
        return provider == DatabaseProvider.PostgreSql
            ? $"\"{validated.Replace("\"", "\"\"")}\""
            : $"[{validated.Replace("]", "]]")}]";
    }

    /// <summary>Validates and quotes a schema.table pair, e.g. dbo, Orders -> [dbo].[Orders] or "dbo"."Orders".</summary>
    public static string BracketQualified(DatabaseProvider provider, string schema, string table) =>
        $"{Bracket(provider, schema, "schema")}.{Bracket(provider, table, "table")}";
}
