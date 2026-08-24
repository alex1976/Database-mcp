# PURPOSE
the purpose of this project is to develop an MCP server to expose a SQL SERVER database (azure or on-prem) or a PostgreSQL database to Claude.

# SPECIFICATION
The MCP server must be able to read/navigate schema, read/navigate tables and read/navigate views contained in the database and exptract data in csv or txt.
The user must be able to request data, specify filters, and express queries against the data contained in the database.

# TECHNICAL NOTES
Good reading performance and data extraction efficiency are required.
The databse will be identified by a set of paramenters specified in configuration.

The server targets exactly one database at a time; DB_PROVIDER selects which engine ("sqlserver", the default, or "postgresql").

Parameters (example, SQL Server):
DB_PROVIDER="sqlserver"
SQLSERVER_HOST="your-sql-server-instance" (azure or on-prem)
SQLSERVER_USER="your-username"
SQLSERVER_PASSWORD="your-password"
SQLSERVER_DATABASE="your-database"
SQLSERVER_ENCRYPT="true"
SQLSERVER_TRUST_CERT="true"  #For self-signed certificates

Parameters (example, PostgreSQL):
DB_PROVIDER="postgresql"
POSTGRES_HOST="your-postgres-host"
POSTGRES_PORT="5432"
POSTGRES_USER="your-username"
POSTGRES_PASSWORD="your-password"
POSTGRES_DATABASE="your-database"
POSTGRES_SSL_MODE="Prefer"  #Disable/Prefer/Require/VerifyCA/VerifyFull

tecnology: c# and dotnet 10