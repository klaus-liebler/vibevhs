using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Portal;

internal static class PortalMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Datei>(b =>
        {
            b.ToTable("Dateien");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });
        mb.Entity<Kursunterlage>(b =>
        {
            b.ToTable("Kursunterlagen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.KursId);
            b.HasIndex(x => x.TokenHash).IsUnique();
        });
        mb.Entity<Portalantrag>(b =>
        {
            b.ToTable("Portalanliegen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.PersonId);
            b.HasIndex(x => x.Status);
            b.Property(x => x.Daten).AlsJson();
        });
    }
}

internal sealed class PortalSpeicher(VibeVhsDbContext db) : IPortalSpeicher
{
    public Task<Datei?> DateiAsync(Guid id, CancellationToken abbruch = default) => db.Set<Datei>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, abbruch);

    public async Task<IReadOnlyList<Kursunterlage>> UnterlagenAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<Kursunterlage>().Where(u => u.KursId == kursId).ToListAsync(abbruch);

    public Task<Kursunterlage?> UnterlageAsync(Guid id, CancellationToken abbruch = default) => db.Set<Kursunterlage>().FirstOrDefaultAsync(u => u.Id == id, abbruch);

    public Task<Kursunterlage?> UnterlageMitTokenAsync(string tokenHash, CancellationToken abbruch = default)
        => db.Set<Kursunterlage>().FirstOrDefaultAsync(u => u.TokenHash == tokenHash, abbruch);

    public async Task<IReadOnlyList<Portalantrag>> AntraegeAsync(Guid? personId = null, bool nurOffene = false, CancellationToken abbruch = default)
        => (await db.Set<Portalantrag>().Where(a => (personId == null || a.PersonId == personId) && (!nurOffene || a.Status == Antragsstatus.Offen)).ToListAsync(abbruch))
            .OrderBy(a => a.EingereichtAm).ToList();

    public Task<Portalantrag?> AntragAsync(Guid id, CancellationToken abbruch = default) => db.Set<Portalantrag>().FirstOrDefaultAsync(a => a.Id == id, abbruch);

    /// <summary>Kurse mit der Person im Team (JSON-Spalte, Suche per LIKE auf die ID) oder als Leitung eines Termins.</summary>
    public async Task<IReadOnlyList<Kurs>> KurseDerPersonAsync(Guid personId, CancellationToken abbruch = default)
    {
        var muster = $"%{personId:D}%";
        var imTeam = await db.Database.SqlQuery<Guid>($"SELECT Id AS Value FROM Kurse WHERE Team LIKE {muster} AND ArchiviertAm IS NULL").ToListAsync(abbruch);
        var leitet = await db.Veranstaltungen.Where(v => v.DozentId == personId).Select(v => v.KursId).Distinct().ToListAsync(abbruch);
        var ids = imTeam.Concat(leitet).Distinct().ToList();
        return await db.Kurse.Include(k => k.Veranstaltungen).Where(k => ids.Contains(k.Id) && k.ArchiviertAm == null).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Person>> PersonenMitEMailAsync(string eMail, CancellationToken abbruch = default)
    {
        var mail = eMail.Trim().ToLower(System.Globalization.CultureInfo.InvariantCulture);
        return await db.Personen.Where(p => p.EMail != null && p.EMail.ToLower() == mail).ToListAsync(abbruch);
    }

    public Task<Benutzerkonto?> KontoZurPersonAsync(Guid personId, CancellationToken abbruch = default)
        => db.Benutzerkonten.FirstOrDefaultAsync(k => k.PersonId == personId && k.Aktiv, abbruch);

    public async Task<IReadOnlyList<Person>> PersonenMitVollmachtFuerAsync(Guid bevollmaechtigterId, CancellationToken abbruch = default)
        => await db.Personen.Where(p => p.Vollmachten.Any(v => v.BevollmaechtigterId == bevollmaechtigterId)).ToListAsync(abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
