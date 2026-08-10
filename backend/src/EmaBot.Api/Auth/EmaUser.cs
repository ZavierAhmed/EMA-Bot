using Microsoft.AspNetCore.Identity;

namespace EmaBot.Api.Auth;

public sealed class EmaUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
}
