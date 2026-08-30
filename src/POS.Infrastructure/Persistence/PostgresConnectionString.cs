using System;
using System.Collections.Generic;
using Npgsql;

namespace POS.Infrastructure.Persistence;

/// <summary>
/// Accepts a PostgreSQL connection string in either of the two shapes it arrives in.
///
/// Neon, Railway, Heroku and most managed Postgres dashboards hand out a URI
/// ("postgresql://user:pass@host/db?sslmode=require"), which is what anyone copying from the
/// provider's console will paste into configuration. Npgsql only understands the key-value
/// form, and rejects the URI with a message that names the connection string itself — so the
/// failure both looks unrelated to its cause and prints the password into the logs.
/// Converting here means either form works wherever a connection string is configured.
/// </summary>
public static class PostgresConnectionString
{
    private const int DefaultPort = 5432;

    public static string Normalise(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

        var value = connectionString.Trim();

        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : DefaultPort,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0])
        };

        if (credentials.Length > 1)
            builder.Password = Uri.UnescapeDataString(credentials[1]);

        foreach (var (key, parameter) in ParseQuery(uri.Query))
        {
            switch (key)
            {
                case "sslmode":
                    builder.SslMode = ParseSslMode(parameter);
                    break;

                case "channel_binding":
                    builder.ChannelBinding = ParseChannelBinding(parameter);
                    break;

                case "application_name":
                    builder.ApplicationName = parameter;
                    break;

                case "options":
                    builder.Options = parameter;
                    break;

                // Anything else is left out rather than guessed at. An unrecognised parameter
                // is far more likely to be a provider-specific hint Npgsql does not need than
                // a setting whose absence changes behaviour.
            }
        }

        return builder.ConnectionString;
    }

    private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            yield return (
                Uri.UnescapeDataString(parts[0]).Trim().ToLowerInvariant(),
                Uri.UnescapeDataString(parts[1]).Trim());
        }
    }

    private static SslMode ParseSslMode(string value) => value.ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "require" => SslMode.Require,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => SslMode.Require
    };

    private static ChannelBinding ParseChannelBinding(string value) => value.ToLowerInvariant() switch
    {
        "disable" => ChannelBinding.Disable,
        "prefer" => ChannelBinding.Prefer,
        "require" => ChannelBinding.Require,
        _ => ChannelBinding.Prefer
    };
}
