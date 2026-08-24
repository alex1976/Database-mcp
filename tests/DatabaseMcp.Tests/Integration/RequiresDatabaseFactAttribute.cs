namespace DatabaseMcp.Tests.Integration;

/// <summary>
/// Marks a test as requiring a real, reachable SQL Server instance. The test is skipped unless
/// SQLSERVER_HOST and SQLSERVER_DATABASE are set in the environment, exactly like the server
/// itself requires (see <c>DatabaseMcp.Configuration.DatabaseOptions.FromEnvironment</c>).
/// </summary>
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLSERVER_HOST")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLSERVER_DATABASE"));

    public RequiresDatabaseFactAttribute()
    {
        if (!IsConfigured)
        {
            Skip = "Set SQLSERVER_HOST and SQLSERVER_DATABASE (and optionally SQLSERVER_USER/SQLSERVER_PASSWORD, " +
                   "SQLSERVER_ENCRYPT, SQLSERVER_TRUST_CERT) to run integration tests against a real SQL Server instance.";
        }
    }
}
