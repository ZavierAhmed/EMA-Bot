using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EmaBot.Api.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EmaBot.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAntiforgery antiforgery,
    SignInManager<EmaUser> signInManager,
    UserManager<EmaUser> userManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("antiforgery")]
    public ActionResult<AntiforgeryResponse> GetAntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryResponse(tokens.RequestToken!));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await userManager.FindByNameAsync(request.UserName);
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new ApiMessage("Invalid username or password."));
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return NoContent();
        }

        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiMessage("Too many failed attempts. Please try again later."));
        }

        return Unauthorized(new ApiMessage("Invalid username or password."));
    }

    [Authorize(Roles = AppRoles.Admin)]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            return Unauthorized();
        }

        return Ok(new CurrentUserResponse(user.UserName!, user.Email!, AppRoles.Admin));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }
}

public sealed record LoginRequest(
    [param: Required, StringLength(128)] string UserName,
    [param: Required, StringLength(256)] string Password);

public sealed record AntiforgeryResponse(string Token);
public sealed record CurrentUserResponse(string UserName, string Email, string Role);
public sealed record ApiMessage(string Message);
