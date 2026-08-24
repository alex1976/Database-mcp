namespace DatabaseMcp.Tests.Integration;

/// <summary>
/// Marks a test as requiring a real, reachable database. The test is skipped unless the
/// connection variables for the selected provider are set in the environment, exactly like the
/// server itself requires (see <c>DatabaseMcp.Configuration.DatabaseOptions.FromEnvironment</c>):
/// SQLSERVER_HOST/SQLSERVER_DATABASE by default, or POSTGRES_HOST/POSTGRES_DATABASE when
/// DB_PROVIDER=postgresql.
/// </summary>
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public static bool IsConfigured
    {
        get
        {
            bool isPostgres = (Environment.GetEnvironmentVariable("DB_PROVIDER") ?? string.Empty)
                .Trim().ToLowerInvariant() is "postgresql" or "postgres" or "pg" or "npgsql";

            (string hostVar, string databaseVar) = isPostgres
                ? ("POSTGRES_HOST", "POSTGRES_DATABASE")
                : ("SQLSERVER_HOST", "SQLSERVER_DATABASE");

            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(hostVar)) &&
                   !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(databaseVar));
        }
    }

    public RequiresDatabaseFactAttribute()
    {
        if (!IsConfigured)
        {
            Skip = "Set SQLSERVER_HOST/SQLSERVER_DATABASE (default provider), or DB_PROVIDER=postgresql plus " +
                   "POSTGRES_HOST/POSTGRES_DATABASE, to run integration tests against a real database instance.";
        }
    }
}
