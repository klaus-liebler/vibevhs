using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Nur für "dotnet ef ..." zur Entwicklungszeit (lokaler Container, deploy/docker-compose.yml).</summary>
internal sealed class DesignZeitFabrik : IDesignTimeDbContextFactory<VibeVhsDbContext>
{
    public VibeVhsDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<VibeVhsDbContext>().UseNpgsql(Registrierung.Entwicklungsverbindung, s => s.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Options);
}
