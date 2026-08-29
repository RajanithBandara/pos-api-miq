using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POS.Application.Common.Models;
using POS.Infrastructure.Persistence;

namespace POS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public HealthController(AppDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    /// <summary>
    /// Checks API and Database health status.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        bool isDbHealthy;
        string? dbError = null;

        try
        {
            isDbHealthy = await _dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            isDbHealthy = false;
            dbError = ex.Message;
        }

        stopwatch.Stop();

        var healthData = new
        {
            Status = isDbHealthy ? "Healthy" : "Degraded",
            Environment = _environment.EnvironmentName,
            TimestampUtc = DateTime.UtcNow,
            Database = new
            {
                Provider = _dbContext.Database.ProviderName,
                Connected = isDbHealthy,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Error = dbError
            }
        };

        if (!isDbHealthy)
        {
            return StatusCode(503, ApiResponse<object>.Fail("Database connectivity check failed.", "Service Degraded"));
        }

        return Ok(ApiResponse<object>.Ok(healthData, "API and Database are operational."));
    }

    /// <summary>
    /// Ping endpoint for basic liveness checking.
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(ApiResponse<object>.Ok(new { Status = "Healthy", ServerTime = DateTime.UtcNow }, "Pong"));
    }
}
