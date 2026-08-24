# PURPOSE
the purpose of this project is to develop an MCP server to expose a SQL SERVER database (azure or on-prem) to Claude.

# SPECIFICATION
The MCP server must be able to read/navigate schema, read/navigate tables and read/navigate views contained in the database and exptract data in csv or txt.
The user must be able to request data, specify filters, and express queries against the data contained in the database.

# TECHNICAL NOTES
Good reading performance and data extraction efficiency are required.
The databse will be identified by a set of paramenters specified in configuration.

Parameters:
SQLSERVER_HOST="your-sql-server-instance" (azure or on-prem)
SQLSERVER_USER="your-username"
SQLSERVER_PASSWORD="your-password"
SQLSERVER_DATABASE="your-database"
SQLSERVER_ENCRYPT="true"
SQLSERVER_TRUST_CERT="true"  #For self-signed certificates

tecnology: c# and dotnet 10