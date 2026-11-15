using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Shop;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Shop;

internal static class ShopMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Warenkorb>(b =>
        {
            b.ToTable("Warenkoerbe");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.Status);
            b.Property(x => x.Positionen).AlsJson();
        });
        mb.Entity<Shopbuchung>(b =>
        {
            b.ToTable("Shopbuchungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.Idempotenzschluessel).IsUnique();
            b.HasIndex(x => x.WarenkorbId);
            b.Property(x => x.Einwilligungen).AlsJson();
            b.Property(x => x.Positionen).AlsJson();
        });
        mb.Entity<Shopkonto>(b =>
        {
            b.ToTable("Shopkonten");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.EMail).IsUnique();
            b.HasIndex(x => x.TokenHash);
        });
        mb.Entity<Kursstand>(b =>
        {
            b.ToTable("Kursstaende");
            b.HasKey(x => x.KursId);
            b.Property(x => x.KursId).ValueGeneratedNever();
        });
    }
}

internal sealed class ShopSpeicher(VibeVhsDbContext db, TimeProvider uhr) : IShopSpeicher, IPlatzreservierungen
{
    public async Task<IReadOnlyList<Kurs>> OeffentlicheKurseAsync(CancellationToken abbruch = default)
        => await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen).Include(k => k.Entgelte)
            .Where(k => k.ArchiviertAm == null && k.Status == Kursstatus.Veroeffentlicht && k.Sichtbarkeit == Sichtbarkeit.Oeffentlich)
            .ToListAsync(abbruch);

    public async Task<IReadOnlyList<Kursstand>> KursstaendeAsync(CancellationToken abbruch = default) => await db.Set<Kursstand>().ToListAsync(abbruch);

    public async Task<IReadOnlyList<Warenkorb>> OffeneWarenkoerbeAsync(CancellationToken abbruch = default)
        => await db.Set<Warenkorb>().Where(w => w.Status == Warenkorbstatus.Offen).ToListAsync(abbruch);

    public Task<Warenkorb?> WarenkorbAsync(Guid id, CancellationToken abbruch = default) => db.Set<Warenkorb>().FirstOrDefaultAsync(w => w.Id == id, abbruch);

    public Task<Shopbuchung?> BuchungMitSchluesselAsync(string schluessel, CancellationToken abbruch = default)
        => db.Set<Shopbuchung>().AsNoTracking().FirstOrDefaultAsync(b => b.Idempotenzschluessel == schluessel, abbruch);

    public Task<Shopkonto?> KontoAsync(Guid id, CancellationToken abbruch = default) => db.Set<Shopkonto>().FirstOrDefaultAsync(k => k.Id == id, abbruch);

    public Task<Shopkonto?> KontoMitEMailAsync(string eMail, CancellationToken abbruch = default)
        => db.Set<Shopkonto>().FirstOrDefaultAsync(k => k.EMail == eMail, abbruch);

    public Task<Shopkonto?> KontoMitTokenAsync(string tokenHash, CancellationToken abbruch = default)
        => tokenHash.Length == 0 ? Task.FromResult<Shopkonto?>(null) : db.Set<Shopkonto>().FirstOrDefaultAsync(k => k.TokenHash == tokenHash, abbruch);

    public async Task<IReadOnlyList<Shopkonto>> UnbestaetigteKontenAsync(CancellationToken abbruch = default)
        => await db.Set<Shopkonto>().Where(k => k.Status == Kontostatus.Unbestaetigt).ToListAsync(abbruch);

    public void Hinzufuegen(object eintrag) => db.Add(eintrag);

    public void Entfernen(object eintrag) => db.Remove(eintrag);

    public async Task<T> InTransaktionAsync<T>(Func<Task<T>> arbeit, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(arbeit);
        await using var transaktion = await db.Database.BeginTransactionAsync(abbruch);
        try
        {
            var ergebnis = await arbeit();
            await transaktion.CommitAsync(abbruch);
            return ergebnis;
        }
        catch
        {
            await transaktion.RollbackAsync(CancellationToken.None);
            // Verfolgte Änderungen der gescheiterten Buchung nicht später versehentlich speichern.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

    /// <summary>Reservierte Plätze offener, nicht abgelaufener Warenkörbe (Zeitvergleich im Speicher, Zeitstempel sind binär gespeichert).</summary>
    public async Task<int> ReserviertAsync(Guid kursId, Guid? ausserWarenkorb, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        return (await db.Set<Warenkorb>().AsNoTracking().Where(w => w.Status == Warenkorbstatus.Offen).ToListAsync(abbruch))
            .Where(w => w.Id != ausserWarenkorb && w.Haelt(jetzt))
            .Sum(w => w.Positionen.Count(p => p.KursId == kursId && !p.Warteliste));
    }
}

/// <summary>Jede Minute: abgelaufene Reservierungen freigeben, unbestätigte Registrierungen löschen, Veröffentlichungsstand abgleichen.</summary>
[DisallowConcurrentExecution]
internal sealed class ShopJob(ShopDienst shop, ILogger<ShopJob> log) : IJob
{
    public static readonly JobKey Schluessel = new("shop", "automatik");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var verfallen = await shop.VerfallenAsync(cancellationToken);
        var geloescht = await shop.UnbestaetigteLoeschenAsync(cancellationToken);
        await shop.AbgleichenAsync(cancellationToken);
        if (verfallen + geloescht > 0)
        {
            log.LogInformation("Webshop: {Verfallen} Warenkörbe verfallen, {Geloescht} unbestätigte Registrierungen gelöscht", verfallen, geloescht);
        }
    }
}
