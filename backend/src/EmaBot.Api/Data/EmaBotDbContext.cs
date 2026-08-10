using EmaBot.Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Data;

public sealed class EmaBotDbContext(DbContextOptions<EmaBotDbContext> options)
    : IdentityDbContext<EmaUser, IdentityRole, string>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmaUser>(entity =>
        {
            entity.Property(user => user.IsActive).HasDefaultValue(true);
        });
    }
}
