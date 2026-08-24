using DatabaseMcp.Configuration;
using DatabaseMcp.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio communicates via stdout, so all logging must go to stderr instead.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

DatabaseOptions databaseOptions = DatabaseOptions.FromEnvironment();
builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddSingleton<ISchemaService>(sp => databaseOptions.Provider switch
{
    DatabaseProvider.PostgreSql => new PostgreSqlSchemaService(sp.GetRequiredService<SqlConnectionFactory>()),
    _ => new SqlServerSchemaService(sp.GetRequiredService<SqlConnectionFactory>()),
});
builder.Services.AddSingleton<QueryService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
