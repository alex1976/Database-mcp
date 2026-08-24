using DatabaseMcp.Configuration;
using Microsoft.Data.SqlClient;

namespace DatabaseMcp.Data;

public sealed class SqlConnectionFactory(DatabaseOptions options)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(options.BuildConnectionString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
