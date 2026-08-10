namespace EmaBot.Api.Configuration;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public string? UserName { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
}
