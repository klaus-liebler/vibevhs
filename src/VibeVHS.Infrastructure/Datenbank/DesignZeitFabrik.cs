using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Nur für "dotnet ef migrations add ..." zur Entwicklungszeit.</summary>
internal sealed class DesignZeitFabrik : IDesignTimeDbContextFactory<VibeVhsDbContext>
{
    public VibeVhsDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<VibeVhsDbContext>().UseSqlite("Data Source=designzeit.db").Options);
}
