using System.Net;
using System.Net.Http.Json;
using EmaBot.Api.Auth;
using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmaBot.Api.Tests;

public sealed class AuthEndpointTests : IClassFixture<EmaBotApiFactory>
{
    private readonly EmaBotApiFactory _factory;

    public AuthEndpointTests(EmaBotApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CurrentUser_RejectsUnauthenticatedRequests()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_RejectsInvalidPassword()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);

        var response = await PostLogin(client, token, "admin", "incorrect-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanLogInAndLoadCurrentUser()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var token = await GetAntiforgeryToken(client);

        var login = await PostLogin(client, token, "admin", "A-strong-password-123!");
        var currentUser = await client.GetAsync("/api/auth/me");

        Assert.True(login.StatusCode == HttpStatusCode.NoContent, await login.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, currentUser.StatusCode);
        var payload = await currentUser.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(payload);
        Assert.Equal("admin", payload.UserName);
        Assert.Equal(AppRoles.Admin, payload.Role);
    }

    private static async Task<string> GetAntiforgeryToken(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var token = System.Text.Json.JsonSerializer.Deserialize<AntiforgeryResponse>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        return Assert.IsType<string>(token?.Token);
    }

    private static async Task<HttpResponseMessage> PostLogin(HttpClient client, string token, string userName, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(userName, password))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}

public sealed class EmaBotApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll(typeof(DbContextOptions<EmaBotDbContext>));
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<EmaBotDbContext>));
            services.RemoveAll<IHostedService>();
            services.AddDbContext<EmaBotDbContext>(options => options.UseInMemoryDatabase("EmaBotApiTests"));

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<EmaBotDbContext>();
            database.Database.EnsureDeleted();
            database.Database.EnsureCreated();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            roleManager.CreateAsync(new IdentityRole(AppRoles.Admin)).GetAwaiter().GetResult();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<EmaUser>>();
            var admin = new EmaUser
            {
                UserName = "admin",
                Email = "admin@example.test",
                EmailConfirmed = true,
                IsActive = true
            };
            userManager.CreateAsync(admin, "A-strong-password-123!").GetAwaiter().GetResult();
            userManager.AddToRoleAsync(admin, AppRoles.Admin).GetAwaiter().GetResult();
        });
    }

}
