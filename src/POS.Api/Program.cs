using System;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Api.Authentication;
using POS.Api.Authorization;
using POS.Api.Extensions;
using POS.Api.Middleware;
using POS.Application;
using POS.Infrastructure;
using POS.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog Structured Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("MachineName", Environment.MachineName)
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .CreateLogger();

builder.Host.UseSerilog();

// Add Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Add Controllers with JSON configuration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

// Two bearer schemes: the till's existing terminal token, and Neon Auth for the web dashboard.
// PosAuth stays the default, so nothing that authenticated before changes behaviour.
builder.Services.AddPosAndDashboardAuthentication(builder.Configuration, builder.Environment);

// Each policy names the scheme allowed to satisfy it, so an endpoint cannot be reached with the
// other client's credential even if that credential is perfectly valid.
builder.Services.AddApiAuthorization();

// Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready", "db" });

// Swagger / OpenAPI
builder.Services.AddSwaggerDocumentation();

// Configurable CORS. The browser origins allowed to call this API — the Angular dashboard, and
// nothing else. Origins are always an explicit list, never a wildcard: the policy allows
// credentials, and a wildcard combined with that is what turns a browser into a confused deputy.
//
// The dashboard authenticates with a bearer token in the Authorization header rather than a
// cookie, so no ambient credential is attached to a cross-site request and there is no CSRF
// surface here to defend. That stops being true the day anything moves to cookie authentication.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("PosWebClientPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Automatically apply database migrations on startup if relational DB is configured
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetService<AppDbContext>();

    if (context != null)
    {
        if (context.Database.IsRelational())
        {
            try
            {
                logger.LogInformation("Applying EF Core migrations to database...");
                await context.Database.MigrateAsync();
                logger.LogInformation("EF Core migrations applied successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply migrations on startup.");
            }
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }
    }
}

// HTTP Middleware Pipeline
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "POS API v1");
    c.RoutePrefix = "swagger";
});

app.UseRouting();

app.UseCors("PosWebClientPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "POS API",
    status = "Online",
    version = "1.0.0",
    docs = "/swagger",
    health = "/api/health"
}));

app.MapControllers();
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/health/live");

try
{
    Log.Information("Starting POS Web API Host...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
