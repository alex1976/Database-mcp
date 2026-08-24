# Database-MCP

An MCP (Model Context Protocol) server that exposes a SQL Server (Azure SQL or on-prem) **or PostgreSQL** database to Claude: browse schemas/tables/views, inspect table and view metadata, and query or export data as CSV/TXT — all read-only. The server targets exactly one database at a time; a configuration switch (`DB_PROVIDER`) selects which engine that is.

## Architecture

The server is a single .NET console application that speaks MCP over **stdio**. Claude (or any MCP client) launches the process, and JSON-RPC messages flow over stdin/stdout; all logging goes to stderr so it never corrupts the protocol stream.

```
Claude  <--stdio(JSON-RPC)-->  DatabaseMcp.dll
                                   │
                        ModelContextProtocol SDK
                         (tool discovery/dispatch)
                                   │
                    ┌──────────────┴──────────────┐
                 Tools/                          Data/
          SchemaTools, DataTools          ISchemaService, QueryService
          (MCP-facing, arg parsing,       (DbConnection/DbCommand —
           error translation)             provider-agnostic ADO.NET)
                                                   │
                                            Security/
                                    SqlGuard (read-only SELECT guard)
                                    SqlIdentifier (provider-aware quoting)
                                                   │
                                   ┌───────────────┴───────────────┐
                            SQL Server                        PostgreSQL
                       (Azure SQL or on-prem)         (Microsoft.Data.SqlClient   Npgsql)
```

- **`Configuration/DatabaseOptions.cs`** / **`DatabaseProvider.cs`** — reads `DB_PROVIDER` plus the connection settings and safety limits for whichever engine that selects, and builds the right connection string (`SqlConnectionStringBuilder` or `NpgsqlConnectionStringBuilder`).
- **`Data/SqlConnectionFactory.cs`** — opens a `DbConnection` (the ADO.NET base type) backed by either a `SqlConnection` or an `NpgsqlConnection`, so everything downstream is written once against `DbConnection`/`DbCommand`/`DbDataReader` and works for both engines.
- **`Data/ISchemaService.cs`** — read-only metadata navigation interface (schemas, tables, views, columns, primary/foreign keys, indexes, approximate row counts), with two implementations selected in `Program.cs` based on `DB_PROVIDER`:
  - **`SqlServerSchemaService.cs`** — SQL Server catalog views (`sys.*`).
  - **`PostgreSqlSchemaService.cs`** — the ANSI-standard `information_schema` views wherever SQL Server and PostgreSQL agree on them (tables, views, columns, primary/foreign keys), falling back to `pg_catalog`/`pg_stat_user_tables` only where there's no ANSI equivalent (indexes, approximate row counts).
  
  All queries in both implementations are fixed and parameterized — no user input is ever concatenated into SQL text.
- **`Data/StructuredQuery.cs`** — builds a parameterized, read-only `SELECT` from structured inputs (table, columns, filters, order, row limit) for callers who don't want to write raw SQL. Identifiers are validated/quoted via `SqlIdentifier` (brackets for SQL Server, double quotes for PostgreSQL); row limiting differs by engine (`TOP (n)` vs. trailing `LIMIT n`); filter values are always bound as parameters.
- **`Data/QueryService.cs`** + **`Data/DelimitedWriter.cs`** — execute a `SELECT` and stream the `DbDataReader` directly to CSV/TXT, either inline (row-capped, for exploration) or to a file (uncapped, for full extraction). Streaming means large result sets are never fully buffered in memory. String-valued filter parameters sent to PostgreSQL are marked `NpgsqlDbType.Unknown` so Postgres infers their type from context (matching a literal), since Postgres — unlike SQL Server — won't implicitly cast a `text` parameter to compare against a numeric/date column.
- **`Security/SqlGuard.cs`** — static validation that a raw SQL string is a single, read-only `SELECT`/`WITH` statement (rejects DML/DDL, `EXEC`/`CALL`, `COPY`, multiple statements, etc. — the denylist covers dangerous keywords from both engines). Defense in depth, not a substitute for a low-privilege DB login.
- **`Security/SqlIdentifier.cs`** — validates and quotes schema/table/column names used when building dynamic SQL for the structured query tool, using the correct quoting style per engine.
- **`Tools/SchemaTools.cs`**, **`Tools/DataTools.cs`** — the MCP tool surface (see below). `Tools/ToolExecution.cs` re-surfaces `DbException` messages (invalid column/object names, login failures, etc. — from either `SqlException` or `NpgsqlException`) to the calling LLM as `McpException`s, since the MCP SDK otherwise replaces non-`McpException` errors with a generic message — and an LLM writing SQL needs that detail to self-correct.

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
- **Npgsql** — PostgreSQL connectivity
- **Microsoft.Extensions.Hosting** — generic host, DI, logging

No ORM: all data access is hand-written, parameterized ADO.NET for predictable performance and full control over streaming.

## Configuration

The server is configured entirely through environment variables. `DB_PROVIDER` selects the engine (`sqlserver`, the default, or `postgresql`/`postgres`); the rest of the variables depend on which one you pick.

### SQL Server (default)

| Variable | Required | Default | Description |
|---|---|---|---|
| `SQLSERVER_HOST` | yes | — | SQL Server host/instance (Azure or on-prem) |
| `SQLSERVER_DATABASE` | yes | — | Database name |
| `SQLSERVER_USER` | no | — | SQL login username |
| `SQLSERVER_PASSWORD` | no | — | SQL login password |
| `SQLSERVER_ENCRYPT` | no | `true` | Encrypt the connection |
| `SQLSERVER_TRUST_CERT` | no | `false` | Trust the server certificate (self-signed certs) |

If `SQLSERVER_USER`/`SQLSERVER_PASSWORD` are omitted, the connection falls back to Windows Integrated Security (useful for on-prem/AD environments).

### PostgreSQL (`DB_PROVIDER=postgresql`)

| Variable | Required | Default | Description |
|---|---|---|---|
| `POSTGRES_HOST` | yes | — | PostgreSQL host |
| `POSTGRES_DATABASE` | yes | — | Database name |
| `POSTGRES_PORT` | no | `5432` | Port |
| `POSTGRES_USER` | no | — | Login role |
| `POSTGRES_PASSWORD` | no | — | Login password |
| `POSTGRES_SSL_MODE` | no | `Prefer` | libpq-style SSL mode: `Disable`/`Prefer`/`Require`/`VerifyCA`/`VerifyFull`. To trust a self-signed certificate, use `Require` (encrypts without validating the chain) rather than `VerifyCA`/`VerifyFull` — Npgsql no longer has a separate "trust server certificate" switch. |

### Shared (both engines)

These accept a generic `DB_*` name; the old provider-prefixed name (`SQLSERVER_*` or `POSTGRES_*`) still works as a fallback for backward compatibility.

| Variable | Default | Description |
|---|---|---|
| `DB_DEFAULT_MAX_ROWS` | `1000` | Default row cap for inline query results |
| `DB_HARD_MAX_ROWS` | `10000` | Absolute row cap for inline results (protects the conversation context) |
| `DB_COMMAND_TIMEOUT_SECONDS` | `30` | Query command timeout |
| `DB_CONNECT_TIMEOUT_SECONDS` | `15` | Connection timeout |
| `DB_EXPORT_DIR` | `./exports` next to the executable | Directory where `export_data` writes files |

**Permissions:** point this server at a login that only has `SELECT` permission on the target database(s). The server enforces read-only access in code (see Safety Model below), but a least-privilege login is the real backstop.

## Safety model

- **`execute_sql`** and **`export_data`** accept raw SQL but reject anything that isn't a single `SELECT` (optionally with a leading `WITH` CTE) via `SqlGuard` — no `INSERT`/`UPDATE`/`DELETE`/`MERGE`/DDL/`EXEC`/`CALL`/`COPY`/multiple statements/etc. The keyword denylist covers dangerous surface from both SQL Server (`xp_cmdshell`, `OPENROWSET`, ...) and PostgreSQL (`COPY`, `dblink`, `pg_read_file`, ...).
- **`query_data`** never sees raw SQL from the caller: it builds a parameterized `SELECT` from structured arguments, with identifiers validated and quoted and all filter values bound as parameters (no injection surface).
- Inline results (`execute_sql`, `query_data`) are capped at a configurable row count (`DB_HARD_MAX_ROWS`) to protect the LLM's context window; the response reports whether the result was `truncated`.
- `export_data` has no row cap — it streams the full result set directly from the `DbDataReader` to a file on disk, so extracting millions of rows doesn't require buffering them in memory.

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

Tests live in `tests/DatabaseMcp.Tests` (xUnit) and cover the safety-critical, pure-logic pieces without needing a database — including both providers where the dialect differs:

- `SqlGuardTests` — the read-only SELECT guard rejects DML/DDL/`EXEC`/multiple statements
- `SqlIdentifierTests` — identifier validation/quoting (brackets vs. double quotes) rejects injection attempts, for both providers
- `StructuredQueryBuilderTests` — the structured `query_data` SQL builder produces correct, parameterized SQL for every filter operator and both providers (`TOP` vs. `LIMIT`, bracket vs. double-quote identifiers) and rejects unsafe identifiers
- `OutputFormatExtensionsTests` — CSV/TXT format parsing
- `DelimitedWriterTests` — CSV/TSV streaming, quoting/escaping, `NULL` handling, row-cap truncation (using an in-memory `DataTable.CreateDataReader()`, no real connection needed)

```powershell
dotnet test
```

There are also integration tests (`tests/DatabaseMcp.Tests/Integration/`) that exercise `ISchemaService`/`QueryService` end-to-end against a real database: they create a uniquely named temp table, run the same code paths the MCP tools use against it, and drop it afterwards. They work against whichever provider is configured and are skipped automatically otherwise:

- SQL Server (default): set `SQLSERVER_HOST`/`SQLSERVER_DATABASE` (and optionally `SQLSERVER_USER`/`SQLSERVER_PASSWORD`).
- PostgreSQL: set `DB_PROVIDER=postgresql` plus `POSTGRES_HOST`/`POSTGRES_DATABASE` (and optionally `POSTGRES_USER`/`POSTGRES_PASSWORD`).

Point these at a disposable/dev database — the fixture creates and drops its own table, but do not run it against a production instance.

## Using it with Claude Code / Claude Desktop

Add it as an MCP server, providing the environment variables above, and point `command`/`args` at the built DLL (`dotnet <path>\DatabaseMcp.dll`) rather than `dotnet run` — it starts faster and doesn't depend on the source tree being present at runtime. See [`claude_desktop_config.example.json`](claude_desktop_config.example.json) for ready-to-copy entries — one per provider, since each running server instance targets a single database. Keep whichever entry (or both, if you want Claude to reach a SQL Server *and* a PostgreSQL database at once, each under its own tool namespace) applies to you, fill in your real host/credentials, and merge it into `claude_desktop_config.json` (Claude Desktop) or `.mcp.json` (Claude Code).

Remember to rebuild (`dotnet build -c Release`) whenever the source changes, since the MCP client launches the compiled DLL directly.
