using System.Data.Common;
using ModelContextProtocol;

namespace DatabaseMcp.Tools;

/// <summary>
/// The MCP SDK only forwards the <see cref="Exception.Message"/> of an <see cref="McpException"/> to
/// the calling client; any other exception type is replaced with a generic message to avoid leaking
/// internals. Database error text (invalid column/object names, permission errors, connectivity
/// failures) — from either SQL Server's <c>SqlException</c> or PostgreSQL's <c>NpgsqlException</c>,
/// both of which derive from <see cref="DbException"/> — is exactly the kind of detail an LLM needs
/// to self-correct its query, so it is deliberately re-surfaced here via <see cref="McpException"/>
/// instead of being swallowed.
/// </summary>
internal static class ToolExecution
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            throw new McpException($"Database error: {ex.Message}", ex);
        }
    }
}
