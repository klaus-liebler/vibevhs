using Microsoft.EntityFrameworkCore;
using QRCoder;
using VibeVHS.Application.Dokumente;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Domain.Dokumente;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Durchfuehrung;

/// <summary>Abbildung von Anwesenheiten, Listenabschlüssen und archivierten Dokumenten.</summary>
internal static class DurchfuehrungMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Unterrichtsapp>(b =>
        {
            b.ToTable("Unterrichtsapps");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Name).HasMaxLength(200);
            b.Property(a => a.Adresse).HasMaxLength(500);
            b.Property(a => a.Beschreibung).HasMaxLength(1000);
            b.Property(a => a.Datenhinweis).HasMaxLength(500);
            b.Property(a => a.FreigegebenVon).HasMaxLength(200);
        });

        mb.Entity<Appeinsatz>(b =>
        {
            b.ToTable("Appeinsaetze");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Titel).HasMaxLength(200);
            b.Property(e => e.Auftrag).HasMaxLength(2000);
            b.HasOne<Unterrichtsapp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Domain.Programm.Kurs>().WithMany().HasForeignKey(e => e.KursId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(e => e.KursId);
        });

        mb.Entity<Anwesenheit>(b =>
        {
            b.ToTable("Anwesenheiten");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.ErfasstVon).HasMaxLength(200);
            b.Property(a => a.Bemerkung).HasMaxLength(500);
            b.Ignore(a => a.Ue);
            b.HasIndex(a => new { a.VeranstaltungId, a.BelegungId }).IsUnique();
            b.HasIndex(a => a.KursId);
            b.HasIndex(a => a.PersonId);
        });

        mb.Entity<Listenabschluss>(b =>
        {
            b.ToTable("Listenabschluesse");
            b.HasKey(a => a.VeranstaltungId);
            b.Property(a => a.VeranstaltungId).ValueGeneratedNever();
            b.Property(a => a.Von).HasMaxLength(200);
        });

        mb.Entity<Termincode>(b =>
        {
            b.ToTable("Termincodes");
            b.HasKey(t => t.Code);
            b.Property(t => t.Code).HasMaxLength(40);
            b.HasIndex(t => new { t.VeranstaltungId, t.OrtId });
        });

        mb.Entity<Zugangstoken>(b =>
        {
            b.ToTable("Zugangstokens");
            b.HasKey(z => z.Id);
            b.Property(z => z.Id).ValueGeneratedNever();
            b.Property(z => z.Hash).HasMaxLength(64);
            b.Property(z => z.WiderrufGrund).HasMaxLength(300);
            b.HasIndex(z => z.Hash).IsUnique();
            b.HasIndex(z => z.PersonId);
        });

        mb.Entity<Dokument>(b =>
        {
            b.ToTable("Dokumente");
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.Titel).HasMaxLength(300);
            b.Property(d => d.ErstelltVon).HasMaxLength(200);
            b.Property(d => d.Sha256).HasMaxLength(64);
            b.Property(d => d.UebersetzungsToken).HasMaxLength(64);
            b.Property(d => d.NeuausstellungGrund).HasMaxLength(500);
            b.HasIndex(d => d.UebersetzungsToken).IsUnique();
            b.HasIndex(d => d.PersonId);
            b.HasIndex(d => d.KursId);
            b.HasIndex(d => d.BelegungId);
        });
    }
}

internal sealed class DurchfuehrungSpeicher(VibeVhsDbContext db) : IDurchfuehrungSpeicher
{
    public async Task<IReadOnlyList<Anwesenheit>> AnwesenheitenAsync(Guid veranstaltungId, CancellationToken abbruch = default)
        => await db.Set<Anwesenheit>().Where(a => a.VeranstaltungId == veranstaltungId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDesKursesAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<Anwesenheit>().AsNoTracking().Where(a => a.KursId == kursId).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Anwesenheit>> AnwesenheitenDerPersonAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Set<Anwesenheit>().AsNoTracking().Where(a => a.PersonId == personId).ToListAsync(abbruch);

    public Task<Listenabschluss?> AbschlussAsync(Guid veranstaltungId, CancellationToken abbruch = default)
        => db.Set<Listenabschluss>().AsNoTracking().FirstOrDefaultAsync(a => a.VeranstaltungId == veranstaltungId, abbruch);

    public async Task<IReadOnlySet<Guid>> AbgeschlosseneAsync(IEnumerable<Guid> veranstaltungIds, CancellationToken abbruch = default)
    {
        var ids = veranstaltungIds.ToList();
        return (await db.Set<Listenabschluss>().AsNoTracking().Where(a => ids.Contains(a.VeranstaltungId)).Select(a => a.VeranstaltungId).ToListAsync(abbruch)).ToHashSet();
    }

    public async Task<Guid?> KursZurVeranstaltungAsync(Guid veranstaltungId, CancellationToken abbruch = default)
        => await db.Veranstaltungen.AsNoTracking().Where(v => v.Id == veranstaltungId).Select(v => (Guid?)v.KursId).FirstOrDefaultAsync(abbruch);

    public async Task<IReadOnlyList<(Guid VeranstaltungId, Guid KursId)>> OffeneListenAsync(DateTime endeVor, DateTime endeNach, CancellationToken abbruch = default)
    {
        var mitBelegung = db.Belegungen.Select(b => b.KursId);
        var zu = db.Set<Listenabschluss>().Select(a => a.VeranstaltungId);
        return (await db.Veranstaltungen.AsNoTracking()
                .Where(v => !v.Ausgefallen && v.Ende < endeVor && v.Ende > endeNach && mitBelegung.Contains(v.KursId) && !zu.Contains(v.Id))
                .OrderBy(v => v.Beginn).Select(v => new { v.Id, v.KursId }).ToListAsync(abbruch))
            .Select(x => (x.Id, x.KursId)).ToList();
    }

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

internal sealed class ZugangstokenSpeicher(VibeVhsDbContext db) : IZugangstokenSpeicher
{
    public Task<Zugangstoken?> ZumHashAsync(string hash, CancellationToken abbruch = default)
        => db.Set<Zugangstoken>().FirstOrDefaultAsync(z => z.Hash == hash, abbruch);

    public Task<Zugangstoken?> LadenAsync(Guid id, CancellationToken abbruch = default) => db.Set<Zugangstoken>().FirstOrDefaultAsync(z => z.Id == id, abbruch);

    public async Task<IReadOnlyList<Zugangstoken>> DerPersonAsync(Guid personId, CancellationToken abbruch = default)
        => (await db.Set<Zugangstoken>().Where(z => z.PersonId == personId).ToListAsync(abbruch)).OrderByDescending(z => z.ErstelltAm).ToList();

    public void Hinzufuegen(Zugangstoken token) => db.Add(token);

    public Task<Termincode?> TermincodeAsync(string code, CancellationToken abbruch = default) => db.Set<Termincode>().FirstOrDefaultAsync(t => t.Code == code, abbruch);

    public async Task<Termincode?> TermincodeFuerAsync(Guid veranstaltungId, Guid ortId, CancellationToken abbruch = default)
        => (await db.Set<Termincode>().Where(t => t.VeranstaltungId == veranstaltungId && t.OrtId == ortId).ToListAsync(abbruch)).OrderByDescending(t => t.ErstelltAm).FirstOrDefault();

    public void Hinzufuegen(Termincode code) => db.Add(code);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

internal sealed class DokumentSpeicher(VibeVhsDbContext db) : IDokumentSpeicher
{
    public Task<Dokument?> LadenAsync(Guid id, CancellationToken abbruch = default) => db.Set<Dokument>().FirstOrDefaultAsync(d => d.Id == id, abbruch);

    public Task<Dokument?> ZumUebersetzungsTokenAsync(string token, CancellationToken abbruch = default)
        => db.Set<Dokument>().AsNoTracking().FirstOrDefaultAsync(d => d.UebersetzungsToken == token, abbruch);

    public async Task<IReadOnlyList<Dokument>> DerPersonAsync(Guid personId, CancellationToken abbruch = default)
        => await db.Set<Dokument>().AsNoTracking().Where(d => d.PersonId == personId).OrderByDescending(d => d.ErstelltAm).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Dokument>> DesKursesAsync(Guid kursId, CancellationToken abbruch = default)
        => await db.Set<Dokument>().AsNoTracking().Where(d => d.KursId == kursId).OrderByDescending(d => d.ErstelltAm).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Dokument>> ZurBelegungAsync(Guid belegungId, CancellationToken abbruch = default)
        => await db.Set<Dokument>().Where(d => d.BelegungId == belegungId).OrderByDescending(d => d.ErstelltAm).ToListAsync(abbruch);

    public void Hinzufuegen(Dokument dokument) => db.Add(dokument);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);
}

internal sealed class QrCodeErzeuger : IQrCode
{
    private static readonly byte[] Schwarz = [0, 0, 0];
    private static readonly byte[] Weiss = [255, 255, 255];

    public string Svg(string inhalt)
    {
        using var daten = QRCodeGenerator.GenerateQrCode(inhalt, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(daten).GetGraphic(8);
    }

    public byte[] Png(string inhalt)
    {
        using var daten = QRCodeGenerator.GenerateQrCode(inhalt, QRCodeGenerator.ECCLevel.M);
        // Explizit RGB-Farben: Das Standard-PNG von QRCoder (1 Bit Graustufen) erkennt PDFsharp/MigraDoc nicht
        // („Image has no valid type.“ statt QR-Code in Briefen, Ausweisen und dem Kurs-QR-Bogen).
        return new PngByteQRCode(daten).GetGraphic(10, Schwarz, Weiss, drawQuietZones: true);
    }
}
