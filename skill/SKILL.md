---
name: database-mcp
description: Use when the user wants to explore a SQL Server database (Azure or on-prem) exposed via the Database-MCP server, or asks to browse schemas/tables/views, inspect table structure, query/filter data, or export data to CSV/TXT. Triggers on requests like "what tables are in the database", "show me the columns of X", "get me the data where...", "export this query to CSV".
---

# Database-MCP usage

This skill explains how to use the `database` MCP server (Database-MCP) to explore and extract data from a SQL Server database. The server is **read-only**: every tool only ever runs `SELECT` statements, so it can be used freely without risk of modifying data.

## Tools available

| Tool | Use for |
|---|---|
| `list_schemas` | Discover which schemas exist |
| `list_tables` | Discover tables (optionally per schema), with approximate row counts |
| `list_views` | Discover views (optionally per schema) |
| `describe_table` | Get columns, types, nullability, primary key, foreign keys, indexes for a table |
| `describe_view` | Get columns and SQL definition for a view |
| `query_data` | Fetch rows from a single table/view using structured filters/columns/order — **prefer this over `execute_sql` for simple lookups** |
| `execute_sql` | Run a raw `SELECT` (joins, aggregates, CTEs) when structured `query_data` isn't expressive enough |
| `export_data` | Stream the **full** result of a `SELECT` to a CSV/TXT file on disk — use for large exports instead of `execute_sql`/`query_data` |

## Recommended workflow

1. **Orient first.** Before writing any query, call `list_schemas` and `list_tables`/`list_views` to see what actually exists — never guess table or column names.
2. **Inspect before querying.** Call `describe_table` (or `describe_view`) on the relevant object to confirm exact column names/types and relationships (foreign keys) before building a query. This avoids trial-and-error against `execute_sql`.
3. **Prefer `query_data` for simple asks.** If the user wants rows from one table with basic filters/sorting/limit, use `query_data` with structured `columns`/`filters`/`orderBy` instead of writing SQL — it's safer and the filter/operator format is self-documenting.
   - Filter operators: `eq`, `ne`, `gt`, `ge`, `lt`, `le`, `like`, `in` (comma-separated values), `isnull`, `isnotnull`.
4. **Use `execute_sql` for anything relational.** Joins, aggregations, `GROUP BY`, window functions, or multi-table logic go through `execute_sql`. Only a single `SELECT` (optionally with a leading `WITH` CTE) is accepted — no DML/DDL, no `EXEC`, no multiple statements.
5. **Cap results, then export if needed.** `execute_sql`/`query_data` return results inline as CSV or TXT, capped at a row limit (to protect context) and report `truncated: true` if more rows exist. If the user needs the *complete* result set (large tables, full reports), switch to `export_data` with the same `SELECT` — it streams every row to a file and returns the file path, row count, and size instead of inline content.
6. **Pick the format.** Both inline and export tools accept `format: "csv"` (default) or `"txt"` (tab-delimited). Use the format the user asked for, or CSV by default.

## Error handling

If a tool call fails, the error message is specific and actionable (e.g. "Invalid column name 'Foo'", "Only SELECT statements ... are allowed", "Unsupported filter operator"). Read it, adjust the query/arguments accordingly (re-check with `describe_table` if needed), and retry — don't ask the user to fix it unless the error indicates a configuration/permission problem on the server side (e.g. login failures).

## What NOT to do

- Don't attempt `INSERT`/`UPDATE`/`DELETE`/`DROP`/`ALTER`/`EXEC` or any other data-modifying statement — the server rejects them, and this database connection should be treated as strictly read-only regardless.
- Don't paste huge raw result sets into the conversation — use `maxRows` sensibly and switch to `export_data` for bulk extraction.
- Don't guess schema/table/column names — always confirm via `list_tables`/`list_views`/`describe_table`/`describe_view` first.
