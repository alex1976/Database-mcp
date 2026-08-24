using System.Data.Common;
using DatabaseMcp.Configuration;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace DatabaseMcp.Data;

/// <summary>
/// Opens a <see cref="DbConnection"/> for whichever provider <see cref="DatabaseOptions.Provider"/>
/// selects. Everything downstream (schema/query services) works against the ADO.NET base types
/// (<see cref="DbConnection"/>/<see cref="DbCommand"/>/<see cref="DbDataReader"/>), so no other code
/// needs to know which concrete driver is in play.
/// </summary>
public sealed class SqlConnectionFactory(DatabaseOptions options)
{
    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        DbConnection connection = options.Provider switch
        {
            DatabaseProvider.PostgreSql => new NpgsqlConnection(options.BuildConnectionString()),
            _ => new SqlConnection(options.BuildConnectionString()),
        };

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
