using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace DatabaseMcp.Security;

/// <summary>
/// Best-effort static validation that a user-supplied SQL string is a single, read-only
/// SELECT/CTE statement. This is not a full SQL parser: it is a defense-in-depth guard, shared
/// across both supported engines (SQL Server and PostgreSQL) since it works on raw SQL text.
/// Pair it with a database login that only has SELECT permissions for real enforcement.
/// </summary>
public static partial class SqlGuard
{
    private static readonly string[] ForbiddenKeywords =
    [
        // Shared / ANSI
        "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "GRANT", "REVOKE", "DENY", "INTO", "CALL",
        // SQL Server specific
        "EXEC", "EXECUTE", "sp_executesql", "OPENROWSET", "OPENQUERY", "OPENDATASOURCE",
        "BULK", "BACKUP", "RESTORE", "SHUTDOWN", "DBCC", "xp_cmdshell",
        // PostgreSQL specific
        "COPY", "DBLINK", "LO_IMPORT", "LO_EXPORT", "PG_READ_FILE", "PG_READ_BINARY_FILE",
        "PG_LS_DIR", "PG_TERMINATE_BACKEND", "PG_CANCEL_BACKEND", "VACUUM", "REINDEX",
        "CLUSTER", "LISTEN", "NOTIFY", "UNLISTEN", "DO",
    ];

    [GeneratedRegex(@"--.*?$|/\*.*?\*/", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    public static void EnsureReadOnlySelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new McpException("The SQL statement is empty.");
        }

        string withoutComments = CommentPattern().Replace(sql, " ");
        string trimmed = withoutComments.Trim();

        // Allow exactly one trailing semicolon; reject anything that looks like multiple statements.
        string body = trimmed.EndsWith(';') ? trimmed[..^1] : trimmed;
        if (body.Contains(';'))
        {
            throw new McpException("Only a single SQL statement is allowed. Remove the extra ';'-separated statement(s).");
        }

        if (!Regex.IsMatch(body, @"^\s*(SELECT|WITH)\b", RegexOptions.IgnoreCase))
        {
            throw new McpException("Only SELECT statements (optionally starting with a WITH/CTE clause) are allowed.");
        }

        foreach (string keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(body, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
            {
                throw new McpException($"The keyword '{keyword}' is not allowed in queries executed by this server. Only read-only SELECT queries are permitted.");
            }
        }
    }
}
