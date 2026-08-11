using EmaBot.Api.Auth;
using EmaBot.Api.Configuration;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EmaBot.Api.Services;

public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapAdminOptions> bootstrapOptions,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            await database.Database.MigrateAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<TradingSettingsService>().GetAsync(cancellationToken);
            var interruptedAt = DateTimeOffset.UtcNow;
            var runningSessions = await database.PaperSessions.Where(session => session.Status == PaperSessionStatus.Running).ToListAsync(cancellationToken);
            foreach (var session in runningSessions)
            {
                session.Status = PaperSessionStatus.Interrupted;
                session.InterruptedAtUtc = interruptedAt;
                session.FailureMessage = "The application restarted; resume to reconnect public market data.";
            }
            if (runningSessions.Count > 0) await database.SaveChangesAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<StrategyOptimizationService>().MarkRunningAsInterruptedAsync(cancellationToken);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<EmaUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            if (!await roleManager.RoleExistsAsync(AppRoles.Admin))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(AppRoles.Admin));
                if (!roleResult.Succeeded)
                {
                    logger.LogError("The Admin role could not be created during database initialization.");
                    return;
                }
            }

            if (await userManager.Users.AnyAsync(cancellationToken))
            {
                return;
            }

            var bootstrap = bootstrapOptions.Value;
            if (string.IsNullOrWhiteSpace(bootstrap.UserName)
                || string.IsNullOrWhiteSpace(bootstrap.Email)
                || string.IsNullOrWhiteSpace(bootstrap.Password))
            {
                logger.LogWarning("No users exist. Configure BootstrapAdmin credentials with user-secrets or environment variables to create the initial Admin.");
                return;
            }

            var admin = new EmaUser
            {
                UserName = bootstrap.UserName,
                Email = bootstrap.Email,
                EmailConfirmed = true,
                IsActive = true
            };
            var createResult = await userManager.CreateAsync(admin, bootstrap.Password);
            if (!createResult.Succeeded)
            {
                logger.LogError("The initial Admin could not be created. Review the configured bootstrap values and password policy.");
                return;
            }

            var roleAssignment = await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            if (!roleAssignment.Succeeded)
            {
                logger.LogError("The initial Admin was created but could not be assigned the Admin role.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Database initialization failed. The API will remain available so the health endpoint can report database status.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
