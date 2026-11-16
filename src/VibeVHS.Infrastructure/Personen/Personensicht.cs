using Microsoft.EntityFrameworkCore;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Personen;

/// <summary>
/// Abfragebausteine „Stand zum Stichtag“ direkt auf den Versionstabellen (keine Kopien in der Personentabelle). Immer der aktuelle
/// Wissensstand (<c>ErsetztAm IS NULL</c>) und die am Stichtag gültige Version (<c>GueltigVon ≤ tag &lt; GueltigBis</c>).
/// Alle Bausteine sind komponierbare <see cref="IQueryable{T}"/> (eine SQL-Abfrage, kein N+1).
/// </summary>
public static class Personensicht
{
    public static IQueryable<PersonName> NamenAm(this VibeVhsDbContext db, DateOnly tag)
        => db.Set<PersonName>().Where(n => n.ErsetztAm == null && n.GueltigVon <= tag && (n.GueltigBis == null || n.GueltigBis > tag));

    public static IQueryable<PersonMerkmal> MerkmaleAm(this VibeVhsDbContext db, MerkmalArt art, DateOnly tag)
        => db.Set<PersonMerkmal>().Where(m => m.Art == art && m.ErsetztAm == null && m.GueltigVon <= tag && (m.GueltigBis == null || m.GueltigBis > tag));

    /// <summary>Personen, die am Stichtag die Rolle(n) haben (Bitmaske im Merkmal Rollen).</summary>
    public static IQueryable<Guid> MitRolle(this VibeVhsDbContext db, Personenrolle rolle, DateOnly tag)
        => db.MerkmaleAm(MerkmalArt.Rollen, tag)
            .Where(m => (SqlFunktionen.Ganzzahl(m.Inhalt) & (long)rolle) == (long)rolle)
            .Select(m => EF.Property<Guid>(m, "PersonId"));

    /// <summary>Personen mit diesem Geburtsdatum (aktueller Wissensstand).</summary>
    public static IQueryable<Guid> MitGeburtsdatum(this VibeVhsDbContext db, DateOnly geburtsdatum)
    {
        var text = PersonMerkmal.DatumText(geburtsdatum);
        return db.Set<PersonMerkmal>().Where(m => m.Art == MerkmalArt.Geburtsdatum && m.ErsetztAm == null && m.Inhalt == text)
            .Select(m => EF.Property<Guid>(m, "PersonId"));
    }

    /// <summary>Personen, die am Stichtag diese E-Mail-Adresse haben (Groß-/Kleinschreibung egal).</summary>
    public static IQueryable<Guid> MitEMail(this VibeVhsDbContext db, string eMail, DateOnly tag)
    {
        var mail = eMail.Trim().ToLowerInvariant();
        return db.Set<EMailAdresse>()
            .Where(e => e.ErsetztAm == null && e.GueltigVon <= tag && (e.GueltigBis == null || e.GueltigBis > tag) && e.Adresse.ToLower() == mail)
            .Select(e => EF.Property<Guid>(e, "PersonId"));
    }

    /// <summary>
    /// Personen mit Name zum Stichtag für die sortierte Liste: Nachname und Vorname tragen die Collation DIN5007_2 (Index
    /// IX_PersonNamen_Sortierung), die Id als Hex bricht Gleichstände. Der Namensvorsatz zählt für die Sortierung nicht.
    /// </summary>
    public static IQueryable<PersonMitSchluessel> MitSortierschluessel(this VibeVhsDbContext db, IQueryable<Person> personen, DateOnly tag)
        => from p in personen
           join n in db.NamenAm(tag) on p.Id equals EF.Property<Guid>(n, "PersonId")
           select new PersonMitSchluessel { Id = p.Id, Nachname = n.Nachname, Vorname = n.Vorname, IdHex = SqlFunktionen.Hex(p.Id) };

    /// <summary>Sortiert nach Nachname, Vorname (DIN 5007-2) und Id.</summary>
    public static IOrderedQueryable<PersonMitSchluessel> Sortiert(this IQueryable<PersonMitSchluessel> q)
        => q.OrderBy(x => x.Nachname).ThenBy(x => x.Vorname).ThenBy(x => x.IdHex);

    /// <summary>Nur Einträge nach dem Seitenschlüssel (Keyset), gleiche Reihenfolge wie <see cref="Sortiert"/>.</summary>
    public static IQueryable<PersonMitSchluessel> Nach(this IQueryable<PersonMitSchluessel> q, string nachname, string vorname, string idHex)
        => q.Where(x => string.Compare(x.Nachname, nachname) > 0
                        || (x.Nachname == nachname && (string.Compare(x.Vorname, vorname) > 0
                                                       || (x.Vorname == vorname && string.Compare(x.IdHex, idHex) > 0))));

    /// <summary>Personen sortiert nach Name zum Stichtag (DIN 5007-2), eindeutig über die Id.</summary>
    public static IQueryable<Person> NachNameSortiert(this IQueryable<Person> personen, VibeVhsDbContext db, DateOnly tag)
        => from p in personen
           join n in db.NamenAm(tag) on p.Id equals EF.Property<Guid>(n, "PersonId")
           orderby n.Nachname, n.Vorname, p.Id
           select p;

    /// <summary>Personen mit diesem Namen am Stichtag (exakt, z. B. für Demodaten).</summary>
    public static IQueryable<Guid> MitNamen(this VibeVhsDbContext db, string vorname, string nachname, DateOnly tag)
        => db.NamenAm(tag).Where(n => n.Vorname == vorname && n.Nachname == nachname).Select(n => EF.Property<Guid>(n, "PersonId"));

    /// <summary>
    /// Personen, auf die ein Suchwort passt (Wortanfang, normalisiert), direkt in den Versionstabellen – ohne eigene Suchtabelle und ohne
    /// Kopie. Durchsucht werden alle Versionen (auch frühere Namen und Adressen): Name, Titel, Geburtsdatum (TT.MM.JJJJ), Wohnort, E-Mail.
    /// </summary>
    public static IQueryable<Guid> MitSuchwort(this VibeVhsDbContext db, string wort)
    {
        var muster = " " + wort;
        var namen = db.Set<PersonName>()
            .Where(n => (" " + SqlFunktionen.Suchtext(n.Vorname + " " + n.Vorsatz + " " + n.Nachname + " " + n.Zusatz)).Contains(muster))
            .Select(n => EF.Property<Guid>(n, "PersonId"));
        var titel = db.Set<PersonMerkmal>().Where(m => m.Art == MerkmalArt.Titel && (" " + SqlFunktionen.Suchtext(m.Inhalt)).Contains(muster))
            .Select(m => EF.Property<Guid>(m, "PersonId"));
        var orte = db.Set<Postadresse>().Where(a => (" " + SqlFunktionen.Suchtext(a.Ort)).Contains(muster)).Select(a => EF.Property<Guid>(a, "PersonId"));
        var mails = db.Set<EMailAdresse>().Where(e => (" " + SqlFunktionen.Suchtext(e.Adresse)).Contains(muster)).Select(e => EF.Property<Guid>(e, "PersonId"));
        var treffer = namen.Union(titel).Union(orte).Union(mails);
        if (DateOnly.TryParseExact(wort, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var datum))
        {
            treffer = treffer.Union(db.MitGeburtsdatum(datum));
        }

        return treffer;
    }

    /// <summary>Anzeigenamen (Titel + Name) zum Stichtag für viele Personen in zwei Abfragen.</summary>
    public static async Task<Dictionary<Guid, string>> AnzeigenamenAsync(this VibeVhsDbContext db, IReadOnlyCollection<Guid> ids, DateOnly tag,
        CancellationToken abbruch = default)
    {
        var namen = await db.NamenAm(tag).Where(n => ids.Contains(EF.Property<Guid>(n, "PersonId")))
            .Select(n => new { PersonId = EF.Property<Guid>(n, "PersonId"), Name = n }).ToListAsync(abbruch);
        var titel = await db.MerkmaleAm(MerkmalArt.Titel, tag).Where(m => ids.Contains(EF.Property<Guid>(m, "PersonId")))
            .Select(m => new { PersonId = EF.Property<Guid>(m, "PersonId"), m.Inhalt }).ToDictionaryAsync(m => m.PersonId, m => m.Inhalt, abbruch);
        var anonymisiert = await db.Personen.IgnoreAutoIncludes().AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Datenschutz == Domain.Datenschutz.Datenschutzstatus.Anonymisiert).Select(p => p.Id).ToListAsync(abbruch);
        var ergebnis = namen.ToDictionary(n => n.PersonId,
            n => titel.GetValueOrDefault(n.PersonId) is { Length: > 0 } t ? $"{t} {n.Name.Anzeige}" : n.Name.Anzeige);
        foreach (var id in anonymisiert)
        {
            ergebnis[id] = $"Gelöschte Person {Domain.Datenschutz.Pseudonym.Kurz(id)}";
        }

        return ergebnis;
    }
}

/// <summary>Person-Id mit Seitenschlüssel (Klasse mit init-Eigenschaften, damit EF danach noch sortieren und filtern kann).</summary>
public sealed class PersonMitSchluessel
{
    public required Guid Id { get; init; }

    public required string Nachname { get; init; }

    public required string Vorname { get; init; }

    public required string IdHex { get; init; }
}

/// <summary>SQL-Funktionen für Abfragen auf Textspalten (in <see cref="VibeVhsDbContext"/> registriert).</summary>
public static class SqlFunktionen
{
    /// <summary>CAST(text AS INTEGER) – z. B. die Rollen-Bitmaske im Merkmal Rollen.</summary>
    public static long Ganzzahl(string text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>suchtext(x) – normalisierte Schreibweise (siehe <see cref="Domain.Gemeinsam.Suchtext"/>), als SQLite-Funktion registriert.</summary>
    public static string Suchtext(string? text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>hex(id) – die 16 Bytes der Id als Großbuchstaben-Hex (Speicherreihenfolge), z. B. für Seitenschlüssel.</summary>
    public static string Hex(Guid id) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");
}
