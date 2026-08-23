namespace Lakaskezelo.Data;

public static class ConnectionStringHelper
{
    // Render (and most other PaaS providers) hand out Postgres connection strings as a
    // "postgres://user:pass@host:port/db" URI. Npgsql only understands the ADO.NET
    // "Key=Value;Key=Value" form, so convert when we detect the URI shape; anything
    // already in ADO.NET form (e.g. local dev in appsettings.Development.json) passes through.
    public static string NormalizeToNpgsqlFormat(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port == -1 ? 5432 : uri.Port;

        return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }
}
