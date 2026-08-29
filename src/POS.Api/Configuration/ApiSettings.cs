using System;

namespace POS.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "POS-API";
    public string Audience { get; set; } = "POS-Clients";
    public int ExpiryMinutes { get; set; } = 120;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

public class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] AllowedMethods { get; set; } = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };
    public string[] AllowedHeaders { get; set; } = new[] { "Authorization", "Content-Type", "X-Correlation-ID", "X-Idempotency-Key" };
    public bool AllowCredentials { get; set; } = true;
}
