using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EmaBot.Api.Data;

public sealed class EmaBotDbContextFactory : IDesignTimeDbContextFactory<EmaBotDbContext>
{
    public EmaBotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>();
        options.UseMySql(
            "Server=localhost;Database=emabot;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new EmaBotDbContext(options.Options);
    }
}
