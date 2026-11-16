using VibeVHS.Infrastructure.Personen;
using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Infrastructure.Datenbank;

internal sealed class StammdatenSpeicher(VibeVhsDbContext db) : IStammdaten, IBenutzerkonten, IEinrichtungAendern, IBenutzerkontenPflege
{
    public async Task<IReadOnlyList<Benutzerkonto>> AlleMitInaktivenAsync(CancellationToken abbruch = default)
        => await MitNamenAsync(await db.Benutzerkonten.ToListAsync(abbruch), abbruch);

    public async Task<Benutzerkonto?> ZumAendernAsync(Guid id, CancellationToken abbruch = default)
        => (await MitNamenAsync(await db.Benutzerkonten.Where(k => k.Id == id).ToListAsync(abbruch), abbruch)).FirstOrDefault();

    public Task<bool> LoginVergebenAsync(string login, CancellationToken abbruch = default) => db.Benutzerkonten.AnyAsync(k => k.Login == login, abbruch);

    public void Hinzufuegen(Benutzerkonto konto) => db.Benutzerkonten.Add(konto);

    public Task<Einrichtung?> EinrichtungZumAendernAsync(CancellationToken abbruch = default)
        => db.Einrichtungen.FirstOrDefaultAsync(e => e.Id == Einrichtung.EinzigeId, abbruch);

    public Task SpeichernAsync(CancellationToken abbruch = default) => db.SaveChangesAsync(abbruch);

    public Task<Einrichtung?> EinrichtungAsync(CancellationToken abbruch = default)
        => db.Einrichtungen.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Einrichtung.EinzigeId, abbruch);

    public async Task<IReadOnlyList<Fachbereich>> FachbereicheAsync(CancellationToken abbruch = default)
        => await db.Fachbereiche.AsNoTracking().OrderBy(f => f.Nr).ToListAsync(abbruch);

    public async Task<IReadOnlyList<Benutzerkonto>> AlleAsync(CancellationToken abbruch = default)
        => await MitNamenAsync(await db.Benutzerkonten.AsNoTracking().Where(k => k.Aktiv).ToListAsync(abbruch), abbruch);

    public async Task<Benutzerkonto?> FindenAsync(string login, CancellationToken abbruch = default)
    {
        var l = login.Trim().ToLowerInvariant();
        return (await MitNamenAsync(await db.Benutzerkonten.AsNoTracking().Where(k => k.Login == l && k.Aktiv).ToListAsync(abbruch), abbruch)).FirstOrDefault();
    }

    /// <summary>Konten mit Person bekommen deren heutigen Namen (aus den Versionen, keine Kopie); sortiert nach Name.</summary>
    private async Task<IReadOnlyList<Benutzerkonto>> MitNamenAsync(List<Benutzerkonto> konten, CancellationToken abbruch)
    {
        var ids = konten.Where(k => k.PersonId is not null).Select(k => k.PersonId!.Value).Distinct().ToList();
        if (ids.Count > 0)
        {
            var namen = await db.AnzeigenamenAsync(ids, DateOnly.FromDateTime(db.Uhr.GetLocalNow().Date), abbruch);
            foreach (var k in konten.Where(k => k.PersonId is not null))
            {
                k.PersonennameSetzen(namen.GetValueOrDefault(k.PersonId!.Value));
            }
        }

        return konten.OrderBy(k => k.Name, StringComparer.CurrentCulture).ThenBy(k => k.Id).ToList();
    }
}
