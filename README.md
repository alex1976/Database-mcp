# Database-MCP

An MCP (Model Context Protocol) server that exposes a SQL Server database (Azure SQL or on-prem) to Claude: browse schemas/tables/views, inspect table and view metadata, and query or export data as CSV/TXT — all read-only.

## Architecture

The server is a single .NET console application that speaks MCP over **stdio**. Claude (or any MCP client) launches the process, and JSON-RPC messages flow over stdin/stdout; all logging goes to stderr so it never corrupts the protocol stream.

```
Claude  <--stdio(JSON-RPC)-->  DatabaseMcp.exe
                                   │
                        ModelContextProtocol SDK
                         (tool discovery/dispatch)
                                   │
                    ┌──────────────┴──────────────┐
                 Tools/                          Data/
          SchemaTools, DataTools          SchemaService, QueryService
          (MCP-facing, arg parsing,       (SQL Server access via
           error translation)             Microsoft.Data.SqlClient)
                                                   │
                                            Security/
                                    SqlGuard (read-only SELECT guard)
                                    SqlIdentifier (safe identifier bracketing)
                                                   │
                                              SQL Server
                                          (Azure SQL or on-prem)
```

- **`Configuration/DatabaseOptions.cs`** — reads connection settings and safety limits from environment variables and builds the SQL Server connection string.
- **`Data/SqlConnectionFactory.cs`** — opens `SqlConnection`s from the configured connection string.
- **`Data/SchemaService.cs`** — read-only metadata navigation (schemas, tables, views, columns, primary/foreign keys, indexes, approximate row counts) using SQL Server catalog views (`sys.*`), with all identifiers passed as parameters — never concatenated.
- **`Data/StructuredQuery.cs`** — builds a parameterized, read-only `SELECT` from structured inputs (table, columns, filters, order, row limit) for callers who don't want to write raw SQL. Identifiers are validated/bracketed via `SqlIdentifier`; filter values are always bound as parameters.
- **`Data/QueryService.cs`** + **`Data/DelimitedWriter.cs`** — execute a `SELECT` and stream the `SqlDataReader` directly to CSV/TXT, either inline (row-capped, for exploration) or to a file (uncapped, for full extraction). Streaming means large result sets are never fully buffered in memory.
- **`Security/SqlGuard.cs`** — static validation that a raw SQL string is a single, read-only `SELECT`/`WITH` statement (rejects DML/DDL, `EXEC`, multiple statements, etc.). Defense in depth, not a substitute for a low-privilege DB login.
- **`Security/SqlIdentifier.cs`** — validates and brackets schema/table/column names used when building dynamic SQL for the structured query tool.
- **`Tools/SchemaTools.cs`**, **`Tools/DataTools.cs`** — the MCP tool surface (see below). `Tools/ToolExecution.cs` re-surfaces `SqlException` messages (invalid column/object names, login failures, etc.) to the calling LLM as `McpException`s, since the MCP SDK otherwise replaces non-`McpException` errors with a generic message — and an LLM writing SQL needs that detail to self-correct.

### Repository layout

```
Database-MCP.slnx
src/DatabaseMcp/          — the MCP server (see above)
tests/DatabaseMcp.Tests/  — xUnit tests (see Testing below)
skill/SKILL.md            — Claude skill describing how to use this server's tools
claude_desktop_config.example.json
```

## Technology

- **.NET 10** / C# (console app, `net10.0`)
- **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)** (official C# MCP SDK) — stdio server transport, tool discovery via `[McpServerToolType]`/`[McpServerTool]`
- **Microsoft.Data.SqlClient** — SQL Server connectivity (Azure SQL and on-prem)
- **Microsoft.Extensions.Hosting** — generic host, DI, logging

No ORM: all data access is hand-written, parameterized ADO.NET for predictable performance and full control over streaming.

## Configuration

The server is configured entirely through environment variables (per the project spec):

| Variable | Required | Default | Description |
|---|---|---|---|
| `SQLSERVER_HOST` | yes | — | SQL Server host/instance (Azure or on-prem) |
| `SQLSERVER_DATABASE` | yes | — | Database name |
| `SQLSERVER_USER` | no | — | SQL login username |
| `SQLSERVER_PASSWORD` | no | — | SQL login password |
| `SQLSERVER_ENCRYPT` | no | `true` | Encrypt the connection |
| `SQLSERVER_TRUST_CERT` | no | `false` | Trust the server certificate (self-signed certs) |
| `SQLSERVER_DEFAULT_MAX_ROWS` | no | `1000` | Default row cap for inline query results |
| `SQLSERVER_HARD_MAX_ROWS` | no | `10000` | Absolute row cap for inline results (protects the conversation context) |
| `SQLSERVER_COMMAND_TIMEOUT_SECONDS` | no | `30` | SQL command timeout |
| `SQLSERVER_CONNECT_TIMEOUT_SECONDS` | no | `15` | Connection timeout |
| `SQLSERVER_EXPORT_DIR` | no | `./exports` next to the executable | Directory where `export_data` writes files |

If `SQLSERVER_USER`/`SQLSERVER_PASSWORD` are omitted, the connection falls back to Windows Integrated Security (useful for on-prem/AD environments).

**Permissions:** point this server at a SQL login that only has `SELECT` permission on the target database(s). The server enforces read-only access in code (see Safety Model below), but a least-privilege login is the real backstop.

## Safety model

- **`execute_sql`** and **`export_data`** accept raw SQL but reject anything that isn't a single `SELECT` (optionally with a leading `WITH` CTE) via `SqlGuard` — no `INSERT`/`UPDATE`/`DELETE`/`MERGE`/DDL/`EXEC`/multiple statements/etc.
- **`query_data`** never sees raw SQL from the caller: it builds a parameterized `SELECT` from structured arguments, with identifiers validated and bracketed and all filter values bound as parameters (no injection surface).
- Inline results (`execute_sql`, `query_data`) are capped at a configurable row count (`SQLSERVER_HARD_MAX_ROWS`) to protect the LLM's context window; the response reports whether the result was `truncated`.
- `export_data` has no row cap — it streams the full result set directly from the `SqlDataReader` to a file on disk, so extracting millions of rows doesn't require buffering them in memory.

## MCP tools

| Tool | Purpose |
|---|---|
| `list_schemas` | List user schemas (system schemas excluded) |
| `list_tables` | List tables, optionally filtered by schema, with approximate row counts |
| `list_views` | List views, optionally filtered by schema |
| `describe_table` | Columns, primary key, foreign keys, indexes, approximate row count for a table |
| `describe_view` | Columns and SQL definition for a view |
| `execute_sql` | Run a raw read-only `SELECT`/`WITH` statement; returns CSV/TXT inline, row-capped |
| `query_data` | Query a table/view via structured columns/filters/order/limit (no raw SQL); returns CSV/TXT inline, row-capped |
| `export_data` | Run a raw read-only `SELECT`/`WITH` statement and stream the **full** result to a CSV/TXT file on disk |

Filter operators for `query_data`: `eq`, `ne`, `gt`, `ge`, `lt`, `le`, `like`, `in` (comma-separated value list), `isnull`, `isnotnull`.

## Building and running

```powershell
dotnet build -c Release
```

(run from the repo root, or from `src/DatabaseMcp` to build only the server). This produces `src/DatabaseMcp/bin/Release/net10.0/DatabaseMcp.dll`. Run it with `dotnet src/DatabaseMcp/bin/Release/net10.0/DatabaseMcp.dll`. The server communicates over stdio, so running it directly in a terminal just waits for JSON-RPC input — it's meant to be launched by an MCP client.

## Testing

Tests live in `tests/DatabaseMcp.Tests` (xUnit) and cover the safety-critical, pure-logic pieces without needing a database:

- `SqlGuardTests` — the read-only SELECT guard rejects DML/DDL/`EXEC`/multiple statements
- `SqlIdentifierTests` — identifier validation/bracketing rejects injection attempts
- `StructuredQueryBuilderTests` — the structured `query_data` SQL builder produces correct, parameterized SQL for every filter operator and rejects unsafe identifiers
- `OutputFormatExtensionsTests` — CSV/TXT format parsing
- `DelimitedWriterTests` — CSV/TSV streaming, quoting/escaping, `NULL` handling, row-cap truncation (using an in-memory `DataTable.CreateDataReader()`, no real connection needed)

```powershell
dotnet test
```

There are also integration tests (`tests/DatabaseMcp.Tests/Integration/`) that exercise `SchemaService`/`QueryService` end-to-end against a real SQL Server instance: they create a uniquely named temp table, run the same code paths the MCP tools use against it, and drop it afterwards. They're skipped automatically unless `SQLSERVER_HOST`/`SQLSERVER_DATABASE` (and optionally `SQLSERVER_USER`/`SQLSERVER_PASSWORD`) are set in the environment before running `dotnet test` — set those to point at a disposable/dev database to run them.

## Using it with Claude Code / Claude Desktop

Add it as an MCP server, providing the environment variables above, and point `command`/`args` at the built DLL (`dotnet <path>\DatabaseMcp.dll`) rather than `dotnet run` — it starts faster and doesn't depend on the source tree being present at runtime. See [`claude_desktop_config.example.json`](claude_desktop_config.example.json) for a ready-to-copy entry — fill in your real host/credentials and, for Claude Desktop, merge it into `claude_desktop_config.json`; for Claude Code, merge it into `.mcp.json`.

Remember to rebuild (`dotnet build -c Release`) whenever the source changes, since the MCP client launches the compiled DLL directly.
