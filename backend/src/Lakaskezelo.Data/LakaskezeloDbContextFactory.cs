using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lakaskezelo.Data;

// Used only by `dotnet ef` at design time — the real app reads its connection string from
// configuration in Program.cs instead. For `database update` against a real target (e.g. Render),
// set LAKASKEZELO_DB_CONNECTION first — accepts either the ADO.NET form or a "postgres://..." URI.
public class LakaskezeloDbContextFactory : IDesignTimeDbContextFactory<LakaskezeloDbContext>
{
    public LakaskezeloDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LAKASKEZELO_DB_CONNECTION")
            ?? "Host=localhost;Database=lakaskezelo;Username=lakaskezelo;Password=placeholder";
        connectionString = ConnectionStringHelper.NormalizeToNpgsqlFormat(connectionString);

        var optionsBuilder = new DbContextOptionsBuilder<LakaskezeloDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new LakaskezeloDbContext(optionsBuilder.Options);
    }
}
