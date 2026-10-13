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

    [GeneratedRegex(@"\G\$(?:[A-Za-z_][A-Za-z0-9_]*)?\$")]
    private static partial Regex DollarQuoteOpen();

    public static void EnsureReadOnlySelect(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new McpException("The SQL statement is empty.");
        }

        // Every check below runs on the masked copy, never on the raw text the database receives,
        // so a comment marker inside a literal cannot hide code from the checks.
        string code = MaskNonCode(sql);
        string trimmed = code.Trim();

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

    /// <summary>
    /// Returns a copy of <paramref name="sql"/> with the same length in which string literals,
    /// quoted identifiers, dollar-quoted bodies and comments are replaced by spaces. Only real code
    /// remains, so a <c>;</c> or keyword is detected only when the engine would also see it as code.
    /// Handles the lexical rules that matter for both engines: doubled quotes, SQL Server
    /// <c>[...]</c> identifiers, nested block comments, PostgreSQL <c>E'...'</c> backslash escapes and
    /// <c>$tag$...$tag$</c> dollar quoting.
    /// </summary>
    private static string MaskNonCode(string sql)
    {
        char[] code = sql.ToCharArray();
        int i = 0;

        while (i < sql.Length)
        {
            char c = sql[i];

            // Consume a whole identifier so that a '$' inside it (valid in PostgreSQL names, e.g. a$b)
            // is not mistaken for the start of a dollar quote, and so an E before a quote is detected.
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && IsWordChar(sql[i]))
                {
                    i++;
                }

                bool isEscapeStringPrefix = i - start == 1 && (c == 'E' || c == 'e') && i < sql.Length && sql[i] == '\'';
                if (isEscapeStringPrefix)
                {
                    i = MaskQuoted(sql, code, i, close: '\'', backslashEscapes: true);
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                    i = MaskQuoted(sql, code, i, close: '\'', backslashEscapes: false);
                    break;

                case '"':
                    i = MaskQuoted(sql, code, i, close: '"', backslashEscapes: false);
                    break;

                case '[':
                    i = MaskQuoted(sql, code, i, close: ']', backslashEscapes: false);
                    break;

                case '-' when i + 1 < sql.Length && sql[i + 1] == '-':
                    i = MaskLineComment(sql, code, i);
                    break;

                case '/' when i + 1 < sql.Length && sql[i + 1] == '*':
                    i = MaskBlockComment(sql, code, i);
                    break;

                case '$':
                    Match tag = DollarQuoteOpen().Match(sql, i);
                    i = tag.Success
                        ? MaskDollarQuoted(sql, code, i, tag.Value)
                        : i + 1;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return new string(code);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    /// <summary>
    /// Masks a quoted run starting at <paramref name="start"/> (the opening quote/bracket) and returns
    /// the index just past its closing delimiter. A doubled closing character is an escaped literal
    /// character ('it''s', "a""b", [a]]b]).
    /// </summary>
    private static int MaskQuoted(string sql, char[] code, int start, char close, bool backslashEscapes)
    {
        int j = start + 1;
        while (j < sql.Length)
        {
            char c = sql[j];

            if (backslashEscapes && c == '\\' && j + 1 < sql.Length)
            {
                j += 2;
                continue;
            }

            if (!backslashEscapes && c == '\\' && close == '\'' && j + 1 < sql.Length && sql[j + 1] == '\'')
            {
                // Without E'' a backslash is literal in PostgreSQL, so '\' ends the literal, while
                // standard_conforming_strings=off would treat it as an escape. Refuse the ambiguous form.
                throw new McpException("Backslash-escaped quotes are not supported. Write a literal quote as '' instead.");
            }

            if (c == close)
            {
                if (j + 1 < sql.Length && sql[j + 1] == close)
                {
                    j += 2;
                    continue;
                }

                Blank(code, start, j + 1);
                return j + 1;
            }

            j++;
        }

        throw new McpException("Unterminated string literal or quoted identifier in SQL statement.");
    }

    private static int MaskLineComment(string sql, char[] code, int start)
    {
        int end = start;
        while (end < sql.Length && sql[end] != '\n')
        {
            end++;
        }

        Blank(code, start, end);
        return end;
    }

    /// <summary>Block comments nest in both SQL Server and PostgreSQL.</summary>
    private static int MaskBlockComment(string sql, char[] code, int start)
    {
        int depth = 0;
        int j = start;
        while (j < sql.Length)
        {
            if (sql[j] == '/' && j + 1 < sql.Length && sql[j + 1] == '*')
            {
                depth++;
                j += 2;
            }
            else if (sql[j] == '*' && j + 1 < sql.Length && sql[j + 1] == '/')
            {
                depth--;
                j += 2;
                if (depth == 0)
                {
                    Blank(code, start, j);
                    return j;
                }
            }
            else
            {
                j++;
            }
        }

        throw new McpException("Unterminated block comment in SQL statement.");
    }

    private static int MaskDollarQuoted(string sql, char[] code, int start, string tag)
    {
        int close = sql.IndexOf(tag, start + tag.Length, StringComparison.Ordinal);
        if (close < 0)
        {
            throw new McpException("Unterminated dollar-quoted string in SQL statement.");
        }

        int end = close + tag.Length;
        Blank(code, start, end);
        return end;
    }

    private static void Blank(char[] code, int start, int end)
    {
        for (int k = start; k < end; k++)
        {
            code[k] = ' ';
        }
    }
}
