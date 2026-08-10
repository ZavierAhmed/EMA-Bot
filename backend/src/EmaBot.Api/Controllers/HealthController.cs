using EmaBot.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(EmaBotDbContext database) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var databaseIsHealthy = false;
        try
        {
            databaseIsHealthy = await database.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Health checks intentionally report only the status, never connection details.
        }
        var response = new HealthResponse("healthy", databaseIsHealthy ? "healthy" : "unhealthy");

        return databaseIsHealthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}

public sealed record HealthResponse(string Api, string Database);
