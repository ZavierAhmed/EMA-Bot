using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EmaBot.Api.Data;

public sealed class EmaBotDbContextFactory : IDesignTimeDbContextFactory<EmaBotDbContext>
{
    public EmaBotDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<EmaBotDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("EmaBotDatabase")
            ?? "Server=localhost;Database=emabot;";
        var options = new DbContextOptionsBuilder<EmaBotDbContext>();
        options.UseMySql(
            connectionString,
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new EmaBotDbContext(options.Options);
    }
}
