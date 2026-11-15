using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Integration;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Integration;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Integration;

internal static class IntegrationMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Integrationskursdaten>(b =>
        {
            b.ToTable("Integrationskurse");
            b.HasKey(x => x.KursId);
            b.Property(x => x.BamfKursnummer).HasMaxLength(40);
            b.Property(x => x.Abschnitte).AlsJson();
            b.Property(x => x.Abrechnungen).AlsJson();
        });

        mb.Entity<Teilnahmeberechtigung>(b =>
        {
            b.ToTable("Teilnahmeberechtigungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.PersonId);
            b.Property(x => x.Aktenzeichen).HasMaxLength(60);
            b.Property(x => x.Stelle).HasMaxLength(200);
        });

        mb.Entity<Identitaetspruefung>(b =>
        {
            b.ToTable("Identitaetspruefungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.PersonId);
            b.Property(x => x.Bemerkung).HasMaxLength(500);
        });

        mb.Entity<Lichtbild>(b =>
        {
            b.ToTable("Lichtbilder");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.PersonId, x.KursId });
        });

        mb.Entity<IkFahrtkosten>(b =>
        {
            b.ToTable("IkFahrtkosten");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.KursId);
        });

        mb.Entity<Pruefungstermin>(b =>
        {
            b.ToTable("Pruefungstermine");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });

        mb.Entity<Pruefungsanmeldung>(b =>
        {
            b.ToTable("Pruefungsanmeldungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.PruefungId);
            b.HasIndex(x => x.PersonId);
        });
    }
}

internal sealed class IntegrationSpeicher(VibeVhsDbContext db) : IIntegrationSpeicher
{
    public Task<Integrationskursdaten?> KursdatenAsync(Guid kursId, CancellationToken abbruch = default)
        => db.Set<Integrationskursdaten>().FirstOrDefaultAsync(k => k.KursId == kursId, abbruch);

    public async Task<IReadOnlyList<Integrationskursdaten>> AlleKursdatenAsync(CancellationToken abbruch = default)
        => await db.Set<Integrationskursdaten>().AsNoTracking().ToListAsync(abbruch);

    public async Task<IReadOnlyList<Teilnahmeberechtigung>> BerechtigungenAsync(IEnumerable<Guid> personIds, CancellationToken abbruch = default)
    {
        var ids = personIds.ToList();
        return await db.Set<Teilnahmeberechtigung>().AsNoTracking().Where(b => ids.Contains(b.PersonId)).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Identitaetspruefung>> IdentitaetspruefungenAsync(IEnumerable<Guid> personIds, CancellationToken abbruch = default)
    {
        var ids = personIds.ToList();
        var lokal = db.Set<Identitaetspruefung>().Local.Where(p => ids.Contains(p.PersonId)).ToList();
        var gespeichert = await db.Set<Identitaetspruefung>().AsNoTracking().Where(p => ids.Contains(p.PersonId)).ToListAsync(abbruch);
        return [.. gespeichert.Where(g => lokal.All(l => l.Id != g.Id)), .. lokal];
    }

    public Task<Lichtbild?> LichtbildAsync(Guid personId, Guid kursId, CancellationToken abbruch = default)
        => db.Set<Lichtbild>().FirstOrDefaultAsync(b => b.PersonId == personId && b.KursId == kursId && b.GeloeschtAm == null, abbruch);

    public async Task<IReadOnlyList<(Guid PersonId, Guid KursId)>> LichtbilderVorhandenAsync(Guid kursId, CancellationToken abbruch = default)
        => (await db.Set<Lichtbild>().AsNoTracking().Where(b => b.KursId == kursId && b.GeloeschtAm == null).Select(b => new { b.PersonId, b.KursId }).ToListAsync(abbruch))
            .Select(x => (x.PersonId, x.KursId)).ToList();

    public async Task<IReadOnlyList<Lichtbild>> LichtbilderZumLoeschenAsync(DateOnly heute, CancellationToken abbruch = default)
        => await db.Set<Lichtbild>().Where(b => b.GeloeschtAm == null && b.LoeschenAb <= heute).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDesKursesAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<Anwesenheit>().Where(a => a.KursId == kursId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Anwesenheit>> VorlaeufigeAnwesenheitenAsync(CancellationToken abbruch = default)
        => await db.Set<Anwesenheit>().Where(a => a.Vorlaeufig).ToListAsync(abbruch);

    public async Task<IReadOnlyList<IkFahrtkosten>> FahrtkostenAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<IkFahrtkosten>().AsNoTracking().Where(f => f.KursId == kursId).OrderBy(f => f.Jahr).ThenBy(f => f.Monat).ToListAsync(abbruch);

    public Task<IkFahrtkosten?> FahrtkostenantragAsync(Guid id, CancellationToken abbruch = default) => db.Set<IkFahrtkosten>().FirstOrDefaultAsync(f => f.Id == id, abbruch);

    public async Task<IReadOnlyList<Pruefungstermin>> PruefungenAsync(CancellationToken abbruch = default)
        => await db.Set<Pruefungstermin>().AsNoTracking().OrderBy(p => p.Beginn).ToListAsync(abbruch);

    public Task<Pruefungstermin?> PruefungAsync(Guid id, CancellationToken abbruch = default) => db.Set<Pruefungstermin>().FirstOrDefaultAsync(p => p.Id == id, abbruch);

    public async Task<IReadOnlyList<Pruefungsanmeldung>> PruefungsanmeldungenAsync(Guid? pruefungId = null, CancellationToken abbruch = default)
        => await db.Set<Pruefungsanmeldung>().AsNoTracking().Where(a => pruefungId == null || a.PruefungId == pruefungId).ToListAsync(abbruch);

    public Task<Pruefungsanmeldung?> PruefungsanmeldungAsync(Guid id, CancellationToken abbruch = default) => db.Set<Pruefungsanmeldung>().FirstOrDefaultAsync(a => a.Id == id, abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
