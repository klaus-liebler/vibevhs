using VibeVHS.Domain.Gemeinsam;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Portal;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

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
            b.FeldAlsTabelle<Portalantrag, Eintrag>("_daten", nameof(Portalantrag.Daten), "PortalanliegenAngaben", "PortalanliegenId", 1000);
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

    /// <summary>Kurse mit der Person im Team (Tabelle KursTeam, Index auf PersonId) oder als Leitung eines Termins.</summary>
    public async Task<IReadOnlyList<Kurs>> KurseDerPersonAsync(Guid personId, CancellationToken abbruch = default)
    {
        var imTeam = await db.Kurse.IgnoreAutoIncludes().Where(k => k.ArchiviertAm == null && k.Team.Any(t => t.PersonId == personId)).Select(k => k.Id).ToListAsync(abbruch);
        var leitet = await db.Veranstaltungen.Where(v => v.DozentId == personId).Select(v => v.KursId).Distinct().ToListAsync(abbruch);
        var ids = imTeam.Concat(leitet).Distinct().ToList();
        return await db.Kurse.Include(k => k.Veranstaltungen).Where(k => ids.Contains(k.Id) && k.ArchiviertAm == null).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Person>> PersonenMitEMailAsync(string eMail, CancellationToken abbruch = default)
    {
        var mitAdresse = db.MitEMail(eMail, DateOnly.FromDateTime(DateTime.Today));
        return await db.Personen.Where(p => mitAdresse.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync(abbruch);
    }

    public Task<Benutzerkonto?> KontoZurPersonAsync(Guid personId, CancellationToken abbruch = default)
        => db.Benutzerkonten.FirstOrDefaultAsync(k => k.PersonId == personId && k.Aktiv, abbruch);

    public async Task<IReadOnlyList<Person>> PersonenMitVollmachtFuerAsync(Guid bevollmaechtigterId, CancellationToken abbruch = default)
        => await db.Personen.Where(p => p.Vollmachten.Any(v => v.BevollmaechtigterId == bevollmaechtigterId)).ToListAsync(abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}
