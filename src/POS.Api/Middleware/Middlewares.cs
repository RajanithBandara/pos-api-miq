using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Domain.Exceptions;
using Serilog.Context;

namespace POS.Api.Middleware;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var val) && !string.IsNullOrWhiteSpace(val)
            ? val.ToString()
            : Guid.NewGuid().ToString("D");

        context.Items[CorrelationIdHeader] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        try
        {
            await _next(context);
            stopwatch.Stop();

            var statusCode = context.Response.StatusCode;
            if (statusCode >= 400)
            {
                _logger.LogWarning("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    method, path, statusCode, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "HTTP {Method} {Path} failed in {ElapsedMs}ms: {Message}",
                method, path, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request execution: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path,
            Extensions = { ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }
        };

        switch (exception)
        {
            case ValidationException valEx:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Validation Error";
                problemDetails.Detail = "One or more validation rules were violated.";
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1";
                problemDetails.Extensions["errors"] = valEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                break;

            case ValidationDomainException valDomEx:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Domain Validation Error";
                problemDetails.Detail = valDomEx.Message;
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1";
                problemDetails.Extensions["errors"] = valDomEx.Errors;
                break;

            case EntityNotFoundException notFoundEx:
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Entity Not Found";
                problemDetails.Detail = notFoundEx.Message;
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.4";
                problemDetails.Extensions["entity"] = notFoundEx.EntityName;
                problemDetails.Extensions["key"] = notFoundEx.EntityKey?.ToString();
                break;

            case InsufficientStockException stockEx:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Insufficient Stock";
                problemDetails.Detail = stockEx.Message;
                problemDetails.Type = "https://pos-api.internal/errors/insufficient-stock";
                problemDetails.Extensions["productId"] = stockEx.ProductId;
                problemDetails.Extensions["requested"] = stockEx.RequestedQuantity;
                problemDetails.Extensions["available"] = stockEx.AvailableQuantity;
                break;

            case DuplicateEntityException dupEx:
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Duplicate Entity";
                problemDetails.Detail = dupEx.Message;
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8";
                break;

            case SyncConflictException conflictEx:
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Synchronization Conflict";
                problemDetails.Detail = conflictEx.Message;
                problemDetails.Type = "https://pos-api.internal/errors/sync-conflict";
                problemDetails.Extensions["entityType"] = conflictEx.EntityType;
                problemDetails.Extensions["entityId"] = conflictEx.EntityId;
                problemDetails.Extensions["serverVersion"] = conflictEx.ServerVersion;
                problemDetails.Extensions["clientVersion"] = conflictEx.ClientVersion;
                break;

            case UnauthorizedDomainException unauthEx:
                problemDetails.Status = (int)HttpStatusCode.Forbidden;
                problemDetails.Title = "Forbidden";
                problemDetails.Detail = unauthEx.Message;
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3";
                break;

            case DomainException domEx:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Domain Rule Violation";
                problemDetails.Detail = domEx.Message;
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.1";
                if (!string.IsNullOrEmpty(domEx.ErrorCode))
                    problemDetails.Extensions["errorCode"] = domEx.ErrorCode;
                break;

            default:
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Internal Server Error";
                problemDetails.Detail = _env.IsDevelopment() ? exception.ToString() : "An unexpected internal error occurred.";
                problemDetails.Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1";
                break;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = problemDetails.Status ?? (int)HttpStatusCode.InternalServerError;

        return context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOpts));
    }
}
