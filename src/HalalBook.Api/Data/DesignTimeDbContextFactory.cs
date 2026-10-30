using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HalalBook.Api.Data;

/// <summary>
/// Used by `dotnet ef` so migrations are generated for SQL Server (production) without starting the app.
/// The connection string here is only a placeholder; pass --connection to `dotnet ef database update`.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=HalalBookDesign;Trusted_Connection=True")
            .Options);
}
