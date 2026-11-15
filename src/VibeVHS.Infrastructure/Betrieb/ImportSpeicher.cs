using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Betrieb;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Betrieb;

internal static class BetriebMapping
{
    public static void Anwenden(ModelBuilder mb)
        => mb.Entity<Importlauf>(b =>
        {
            b.ToTable("Importlaeufe");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.Sha256);
            b.Property(x => x.Zuordnungen).AlsJson();
        });
}

internal sealed class ImportSpeicher(VibeVhsDbContext db) : IImportSpeicher
{
    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public async Task<IReadOnlyList<Importlauf>> LaeufeAsync(CancellationToken abbruch = default)
        => (await db.Set<Importlauf>().AsNoTracking().ToListAsync(abbruch)).OrderByDescending(l => l.Am).ToList();

    public Task<bool> DateiBereitsImportiertAsync(string sha256, CancellationToken abbruch = default) => db.Set<Importlauf>().AnyAsync(l => l.Sha256 == sha256, abbruch);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
