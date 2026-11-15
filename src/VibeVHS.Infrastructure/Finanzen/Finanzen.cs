using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Finanzen;

/// <summary>Abbildung der Finanzdaten (Belege, Nummernkreise, Hash-Kette, SEPA, Kontoauszüge, Mahnläufe, Honorarverträge).</summary>
internal static class FinanzMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Beleg>(b =>
        {
            b.ToTable("Belege");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Nummer).HasMaxLength(30);
            b.HasIndex(x => x.Nummer).IsUnique();
            b.HasIndex(x => x.PersonId);
            b.HasIndex(x => x.OrganisationId);
            b.HasIndex(x => x.BezugBelegId);
            b.Property(x => x.ErfasstVon).HasMaxLength(200);
            b.Property(x => x.FestgeschriebenVon).HasMaxLength(200);
            b.Property(x => x.EmpfaengerName).HasMaxLength(300);
            b.Property(x => x.EmpfaengerAnschrift).HasMaxLength(1000);
            b.Property(x => x.Text).HasMaxLength(2000);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.OwnsMany(x => x.Positionen, p =>
            {
                p.ToTable("Belegpositionen");
                p.WithOwner().HasForeignKey("BelegId");
                p.HasKey("BelegId", nameof(Belegposition.Nr));
                p.Property(x => x.Nr).ValueGeneratedNever();
                p.Property(x => x.Text).HasMaxLength(500);
                p.HasIndex(x => x.BelegungId);
                p.HasIndex(x => x.VeranstaltungId);
            });
            b.Navigation(x => x.Positionen).HasField("_positionen").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        mb.Entity<Nummernkreis>(b =>
        {
            b.ToTable("Nummernkreise");
            b.HasKey(x => new { x.Art, x.Jahr });
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        mb.Entity<Finanzprotokolleintrag>(b =>
        {
            b.ToTable("Finanzprotokoll");
            b.HasKey(x => x.Nr);
            b.Property(x => x.Nr).ValueGeneratedNever();
            b.Property(x => x.BenutzerKennung).HasMaxLength(100);
            b.Property(x => x.Aktion).HasMaxLength(60);
            b.Property(x => x.ObjektTyp).HasMaxLength(60);
            b.Property(x => x.VorigerHash).HasMaxLength(64);
            b.Property(x => x.Hash).HasMaxLength(64);
        });

        mb.Entity<Finanzeinstellungen>(b =>
        {
            b.ToTable("Finanzeinstellungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Mahnstufen).AlsJson();
        });

        mb.Entity<SepaMandat>(b =>
        {
            b.ToTable("SepaMandate");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Referenz).HasMaxLength(35);
            b.HasIndex(x => x.Referenz).IsUnique();
            b.HasIndex(x => x.PersonId);
            b.Property(x => x.Iban).HasMaxLength(34);
            b.Property(x => x.Bic).HasMaxLength(11);
            b.Property(x => x.Kontoinhaber).HasMaxLength(200);
        });

        mb.Entity<Bankverbindung>(b =>
        {
            b.ToTable("Bankverbindungen");
            b.HasKey(x => x.PersonId);
            b.Property(x => x.Iban).HasMaxLength(34);
            b.Property(x => x.Bic).HasMaxLength(11);
            b.Property(x => x.Kontoinhaber).HasMaxLength(200);
        });

        mb.Entity<Zahlungslauf>(b =>
        {
            b.ToTable("Zahlungslaeufe");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.NachrichtId).HasMaxLength(35);
            b.Property(x => x.Posten).AlsJson();
            b.Ignore(x => x.Aktiv);
        });

        mb.Entity<Kontoauszug>(b =>
        {
            b.ToTable("Kontoauszuege");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.Sha256).IsUnique();
            b.HasIndex(x => x.AuszugId);
        });

        mb.Entity<Umsatz>(b =>
        {
            b.ToTable("Umsaetze");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.KontoauszugId);
            b.HasIndex(x => x.EndToEndId);
            b.Property(x => x.Verwendungszweck).HasMaxLength(500);
        });

        mb.Entity<Mahnlauf>(b =>
        {
            b.ToTable("Mahnlaeufe");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Posten).AlsJson();
            b.Ignore(x => x.Aktiv);
        });

        mb.Entity<Honorarvertrag>(b =>
        {
            b.ToTable("Honorarvertraege");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.PersonId);
            b.Property(x => x.Modell).HasMaxLength(60);
            b.Property(x => x.Parameter).AlsJson();
        });

        mb.Entity<Uebergabe>(b =>
        {
            b.ToTable("Uebergaben");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });
    }
}

internal sealed class FinanzSpeicher(VibeVhsDbContext db) : IFinanzSpeicher
{
    public Task<Finanzeinstellungen?> EinstellungenAsync(CancellationToken abbruch = default) => db.Set<Finanzeinstellungen>().FirstOrDefaultAsync(abbruch);

    public async Task<Nummernkreis> NummernkreisAsync(Belegart art, int jahr, CancellationToken abbruch = default)
    {
        var kreis = db.Set<Nummernkreis>().Local.FirstOrDefault(k => k.Art == art && k.Jahr == jahr)
                    ?? await db.Set<Nummernkreis>().FirstOrDefaultAsync(k => k.Art == art && k.Jahr == jahr, abbruch);
        if (kreis is null)
        {
            kreis = new Nummernkreis(art, jahr);
            db.Add(kreis);
        }

        return kreis;
    }

    public async Task<IReadOnlyList<Nummernkreis>> NummernkreiseAsync(CancellationToken abbruch = default)
        => await db.Set<Nummernkreis>().AsNoTracking().ToListAsync(abbruch);

    public Task<Beleg?> BelegAsync(Guid id, CancellationToken abbruch = default) => db.Set<Beleg>().FirstOrDefaultAsync(b => b.Id == id, abbruch);

    public Task<Beleg?> BelegNachNummerAsync(string nummer, CancellationToken abbruch = default)
        => db.Set<Beleg>().AsNoTracking().FirstOrDefaultAsync(b => b.Nummer == nummer, abbruch);

    public async Task<IReadOnlyList<Beleg>> BelegeAsync(Guid? partei = null, CancellationToken abbruch = default)
    {
        return await db.Set<Beleg>().AsNoTracking()
            .Where(b => partei == null || b.PersonId == partei || b.OrganisationId == partei).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Beleg>> BelegeZumAendernAsync(IEnumerable<Guid> ids, CancellationToken abbruch = default)
    {
        var liste = ids.ToList();
        return await db.Set<Beleg>().Where(b => liste.Contains(b.Id)).ToListAsync(abbruch);
    }

    public async Task<IReadOnlyList<Belegung>> AbrechenbareBelegungenAsync(CancellationToken abbruch = default)
        => await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => (b.EntgeltCent > 0 || b.GebuehrCent > 0)
                        && (b.Status == Belegungsstatus.Angemeldet || b.Status == Belegungsstatus.Storniert || b.Status == Belegungsstatus.Umgebucht))
            .ToListAsync(abbruch);

    public async Task<IReadOnlyList<Dozententermin>> DozententermineAsync(DateTime von, DateTime bis, Guid? dozentId = null, CancellationToken abbruch = default)
    {
        var termine = await db.Veranstaltungen.AsNoTracking()
            .Where(v => v.DozentId != null && v.Beginn >= von && v.Beginn < bis && (dozentId == null || v.DozentId == dozentId))
            .ToListAsync(abbruch);
        if (termine.Count == 0)
        {
            return [];
        }

        var kursIds = termine.Select(v => v.KursId).Distinct().ToList();
        var kurse = await db.Kurse.IgnoreAutoIncludes().AsNoTracking().Where(k => kursIds.Contains(k.Id))
            .Select(k => new { k.Id, k.Kursnummer, k.KursartId, k.Fachbereich }).ToDictionaryAsync(k => k.Id, abbruch);
        var arten = await db.Kursarten.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, abbruch);
        var belegungen = await db.Belegungen.IgnoreAutoIncludes().AsNoTracking()
            .Where(b => kursIds.Contains(b.KursId) && b.Status == Belegungsstatus.Angemeldet)
            .Select(b => new { b.KursId, b.Ermaessigt }).ToListAsync(abbruch);
        var ids = termine.Select(v => v.Id).ToList();
        var anwesend = await db.Set<Anwesenheit>().AsNoTracking().Where(a => ids.Contains(a.VeranstaltungId))
            .GroupBy(a => a.VeranstaltungId)
            .Select(g => new { g.Key, Anwesend = g.Count(a => a.Status == Anwesenheitsstatus.Anwesend) })
            .ToDictionaryAsync(x => x.Key, x => x.Anwesend, abbruch);
        var abgeschlossen = await db.Set<Listenabschluss>().AsNoTracking().Where(l => ids.Contains(l.VeranstaltungId)).Select(l => l.VeranstaltungId).ToListAsync(abbruch);
        var zu = abgeschlossen.ToHashSet();
        return termine.Where(v => kurse.ContainsKey(v.KursId)).Select(v =>
        {
            var k = kurse[v.KursId];
            return new Dozententermin(v.DozentId!.Value, v.Id, v.KursId, k.Kursnummer, arten.GetValueOrDefault(k.KursartId, "?"), k.Fachbereich, v.Beginn, v.Ende,
                v.Ausgefallen, v.AusgefallenAm, v.Bemerkung, string.Join(" + ", v.Orte.Select(o => o.OrtName)), belegungen.Count(b => b.KursId == v.KursId),
                anwesend.TryGetValue(v.Id, out var n) ? n : zu.Contains(v.Id) ? 0 : null, belegungen.Count(b => b.KursId == v.KursId && b.Ermaessigt));
        }).ToList();
    }

    public async Task<IReadOnlyList<SepaMandat>> MandateAsync(Guid? personId = null, CancellationToken abbruch = default)
        => await db.Set<SepaMandat>().AsNoTracking().Where(m => personId == null || m.PersonId == personId).OrderByDescending(m => m.UnterschriebenAm).ToListAsync(abbruch);

    public Task<SepaMandat?> MandatAsync(Guid id, CancellationToken abbruch = default) => db.Set<SepaMandat>().FirstOrDefaultAsync(m => m.Id == id, abbruch);

    public Task<int> MandateAnzahlAsync(CancellationToken abbruch = default) => db.Set<SepaMandat>().CountAsync(abbruch);

    public Task<Bankverbindung?> BankverbindungAsync(Guid personId, CancellationToken abbruch = default)
        => db.Set<Bankverbindung>().FirstOrDefaultAsync(b => b.PersonId == personId, abbruch);

    public async Task<IReadOnlyList<Bankverbindung>> BankverbindungenAsync(CancellationToken abbruch = default)
        => await db.Set<Bankverbindung>().AsNoTracking().Where(b => b.Iban != "anonymisiert").ToListAsync(abbruch);

    public async Task<IReadOnlyList<Zahlungslauf>> ZahlungslaeufeAsync(CancellationToken abbruch = default)
    {
        var liste = await db.Set<Zahlungslauf>().AsNoTracking().ToListAsync(abbruch);
        return liste.OrderByDescending(l => l.ErstelltAm).ToList();
    }

    public Task<Zahlungslauf?> ZahlungslaufAsync(Guid id, CancellationToken abbruch = default) => db.Set<Zahlungslauf>().FirstOrDefaultAsync(l => l.Id == id, abbruch);

    public Task<bool> KontoauszugVorhandenAsync(string sha256, string auszugId, CancellationToken abbruch = default)
        => db.Set<Kontoauszug>().AnyAsync(k => k.Sha256 == sha256 || k.AuszugId == auszugId, abbruch);

    public async Task<IReadOnlyList<Kontoauszug>> KontoauszuegeAsync(CancellationToken abbruch = default)
        => (await db.Set<Kontoauszug>().AsNoTracking().ToListAsync(abbruch)).OrderByDescending(k => k.EingelesenAm).ToList();

    public async Task<IReadOnlyList<Umsatz>> UmsaetzeAsync(Guid? kontoauszugId = null, bool nurOffene = false, CancellationToken abbruch = default)
        => await db.Set<Umsatz>().AsNoTracking()
            .Where(u => (kontoauszugId == null || u.KontoauszugId == kontoauszugId) && (!nurOffene || u.Status == Umsatzstatus.Offen))
            .OrderBy(u => u.Nr).ToListAsync(abbruch);

    public Task<Umsatz?> UmsatzAsync(Guid id, CancellationToken abbruch = default) => db.Set<Umsatz>().FirstOrDefaultAsync(u => u.Id == id, abbruch);

    public async Task<IReadOnlyList<Mahnlauf>> MahnlaeufeAsync(CancellationToken abbruch = default)
        => (await db.Set<Mahnlauf>().AsNoTracking().ToListAsync(abbruch)).OrderByDescending(l => l.ErstelltAm).ToList();

    public Task<Mahnlauf?> MahnlaufAsync(Guid id, CancellationToken abbruch = default) => db.Set<Mahnlauf>().FirstOrDefaultAsync(l => l.Id == id, abbruch);

    public async Task<IReadOnlyList<Honorarvertrag>> HonorarvertraegeAsync(Guid? personId = null, CancellationToken abbruch = default)
        => await db.Set<Honorarvertrag>().AsNoTracking().Where(v => personId == null || v.PersonId == personId).OrderBy(v => v.GueltigAb).ToListAsync(abbruch);

    public Task<Honorarvertrag?> HonorarvertragAsync(Guid id, CancellationToken abbruch = default) => db.Set<Honorarvertrag>().FirstOrDefaultAsync(v => v.Id == id, abbruch);

    public async Task<IReadOnlyList<Uebergabe>> UebergabenAsync(CancellationToken abbruch = default)
        => (await db.Set<Uebergabe>().AsNoTracking().ToListAsync(abbruch)).OrderByDescending(u => u.Am).ToList();

    public Task<Uebergabe?> UebergabeAsync(Guid id, CancellationToken abbruch = default) => db.Set<Uebergabe>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, abbruch);

    public Task<Organisation?> OrganisationAsync(Guid id, CancellationToken abbruch = default) => db.Set<Organisation>().FirstOrDefaultAsync(o => o.Id == id, abbruch);

    public async Task<IReadOnlyList<Organisation>> AuftraggeberAsync(CancellationToken abbruch = default)
    {
        var ids = await db.Kurse.IgnoreAutoIncludes().Where(k => k.AuftraggeberId != null).Select(k => k.AuftraggeberId!.Value).Distinct().ToListAsync(abbruch);
        return await db.Set<Organisation>().AsNoTracking().Where(o => ids.Contains(o.Id)).OrderBy(o => o.Name).ToListAsync(abbruch);
    }

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public void Entfernen(object eintrag) => db.Remove(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

/// <summary>Hash-Kette: Der Vorgänger kommt aus der Datenbank bzw. aus den in diesem Arbeitsbereich angefügten Einträgen.</summary>
internal sealed class Finanzprotokoll(VibeVhsDbContext db, IAktuellerBenutzer benutzer, TimeProvider uhr) : IFinanzprotokoll
{
    public async Task SchreibenAsync(string aktion, string objektTyp, Guid? objektId, string? vorher, string? nachher, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var letzter = db.Set<Finanzprotokolleintrag>().Local.OrderByDescending(e => e.Nr).FirstOrDefault()
                      ?? await db.Set<Finanzprotokolleintrag>().AsNoTracking().OrderByDescending(e => e.Nr).FirstOrDefaultAsync(abbruch);
        var eintrag = new Finanzprotokolleintrag((letzter?.Nr ?? 0) + 1, uhr.GetUtcNow(), wer.Kennung, aktion, objektTyp, objektId, vorher, nachher,
            letzter?.Hash ?? Finanzprotokolleintrag.Anfang);
        db.Add(eintrag);
    }

    public async Task<IReadOnlyList<Finanzprotokolleintrag>> AlleAsync(CancellationToken abbruch = default)
        => await db.Set<Finanzprotokolleintrag>().AsNoTracking().OrderBy(e => e.Nr).ToListAsync(abbruch);
}
