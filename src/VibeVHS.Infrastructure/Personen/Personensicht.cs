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
        => db.Set<PersonName>().Where(n => EF.Property<Guid?>(n, "ErsetztDurchVorgangId") == null && n.GueltigVon <= tag && n.GueltigBis > tag);

    public static IQueryable<PersonMerkmal> MerkmaleAm(this VibeVhsDbContext db, MerkmalArt art, DateOnly tag)
        => db.Set<PersonMerkmal>().Where(m => m.Art == art && EF.Property<Guid?>(m, "ErsetztDurchVorgangId") == null && m.GueltigVon <= tag && m.GueltigBis > tag);

    /// <summary>Personen, die am Stichtag die Rolle(n) haben (Bitmaske im Merkmal Rollen).</summary>
    public static IQueryable<Guid> MitRolle(this VibeVhsDbContext db, Personenrolle rolle, DateOnly tag)
        => db.MerkmaleAm(MerkmalArt.Rollen, tag)
            .Where(m => (SqlFunktionen.Ganzzahl(m.Inhalt) & (long)rolle) == (long)rolle)
            .Select(m => EF.Property<Guid>(m, "PersonId"));

    /// <summary>Personen mit diesem Geburtsdatum (aktueller Wissensstand).</summary>
    public static IQueryable<Guid> MitGeburtsdatum(this VibeVhsDbContext db, DateOnly geburtsdatum)
    {
        var text = PersonMerkmal.DatumText(geburtsdatum);
        return db.Set<PersonMerkmal>().Where(m => m.Art == MerkmalArt.Geburtsdatum && EF.Property<Guid?>(m, "ErsetztDurchVorgangId") == null && m.Inhalt == text)
            .Select(m => EF.Property<Guid>(m, "PersonId"));
    }

    /// <summary>Personen, die am Stichtag diese E-Mail-Adresse haben (Groß-/Kleinschreibung egal).</summary>
    public static IQueryable<Guid> MitEMail(this VibeVhsDbContext db, string eMail, DateOnly tag)
    {
        var mail = eMail.Trim().ToLowerInvariant();
        return db.Set<EMailAdresse>()
            .Where(e => EF.Property<Guid?>(e, "ErsetztDurchVorgangId") == null && e.GueltigVon <= tag && e.GueltigBis > tag && e.Adresse.ToLower() == mail)
            .Select(e => EF.Property<Guid>(e, "PersonId"));
    }

    /// <summary>
    /// Personen mit Name zum Stichtag für die sortierte Liste: Nachname und Vorname tragen die Collation DIN5007_2 (Index
    /// IX_PersonNamen_Sortierung), die Id als Hex bricht Gleichstände. Der Namensvorsatz zählt für die Sortierung nicht.
    /// </summary>
    public static IQueryable<PersonMitSchluessel> MitSortierschluessel(this VibeVhsDbContext db, IQueryable<Person> personen, DateOnly tag)
        => from p in personen
           join n in db.NamenAm(tag) on p.Id equals EF.Property<Guid>(n, "PersonId")
           select new PersonMitSchluessel { Id = p.Id, Nachname = n.Nachname, Vorname = n.Vorname, Vorsatz = n.Vorsatz, Zusatz = n.Zusatz, IdHex = SqlFunktionen.Hex(p.Id) };

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
    /// Die Suchausdrücke tragen Trigramm-Indizes (<see cref="Zusatzschema"/>), „LIKE '% wort%'“ wird damit zum Indexzugriff.
    /// </summary>
    public static IQueryable<Guid> MitSuchwort(this VibeVhsDbContext db, string wort)
    {
        var muster = "% " + Suchmuster(wort) + "%";
        var namen = db.Set<PersonName>()
            .Where(n => EF.Functions.Like(SqlFunktionen.Namensuche(n.Vorname, n.Vorsatz, n.Nachname, n.Zusatz), muster))
            .Select(n => EF.Property<Guid>(n, "PersonId"));
        var titel = db.Set<PersonMerkmal>().Where(m => m.Art == MerkmalArt.Titel && EF.Functions.Like(SqlFunktionen.Suchwoerter(m.Inhalt), muster))
            .Select(m => EF.Property<Guid>(m, "PersonId"));
        var orte = db.Set<Postadresse>().Where(a => EF.Functions.Like(SqlFunktionen.Suchwoerter(a.Ort), muster)).Select(a => EF.Property<Guid>(a, "PersonId"));
        var mails = db.Set<EMailAdresse>().Where(e => EF.Functions.Like(SqlFunktionen.Suchwoerter(e.Adresse), muster)).Select(e => EF.Property<Guid>(e, "PersonId"));
        var treffer = namen.Union(titel).Union(orte).Union(mails);
        if (DateOnly.TryParseExact(wort, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var datum))
        {
            treffer = treffer.Union(db.MitGeburtsdatum(datum));
        }

        return treffer;
    }

    /// <summary>Suchwort für LIKE: normalisiert – enthält danach nur Buchstaben, Ziffern, „@“, „.“ und Leerzeichen, also keine Platzhalter.</summary>
    public static string Suchmuster(string wort) => Domain.Gemeinsam.Suchtext.Normalisieren(wort);

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

    public string Vorsatz { get; init; } = string.Empty;

    public string Zusatz { get; init; } = string.Empty;

    public required string IdHex { get; init; }
}

/// <summary>SQL-Funktionen für Abfragen (in <see cref="VibeVhsDbContext"/> registriert, angelegt in <see cref="Zusatzschema"/>).</summary>
public static class SqlFunktionen
{
    /// <summary>CAST(text AS bigint) – z. B. die Rollen-Bitmaske im Merkmal Rollen.</summary>
    public static long Ganzzahl(string text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>suchtext(x) – normalisierte Schreibweise (siehe <see cref="Domain.Gemeinsam.Suchtext"/>).</summary>
    public static string Suchtext(string? text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>suchwoerter(x) – „ “ + suchtext(x), für die Suche nach Wortanfängen (Trigramm-Index).</summary>
    public static string Suchwoerter(string? text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>namensuchtext(vorname, vorsatz, nachname, zusatz) – „ “ + suchtext des ganzen Namens (Trigramm-Index).</summary>
    public static string Namensuche(string? vorname, string? vorsatz, string? nachname, string? zusatz)
        => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>idhex(id) – die Id als Großbuchstaben-Hex ohne Bindestriche (gleiche Reihenfolge wie die uuid), z. B. für Seitenschlüssel.</summary>
    public static string Hex(Guid id) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    /// <summary>word_similarity(eingabe, text) aus pg_trgm: 0 bis 1, wie gut die Eingabe zu einem Wortabschnitt des Textes passt.</summary>
    public static double WortAehnlichkeit(string eingabe, string? text) => throw new InvalidOperationException("Nur in Datenbankabfragen verwendbar.");

    internal static void Registrieren(ModelBuilder modell)
    {
        var text = new Microsoft.EntityFrameworkCore.Storage.StringTypeMapping("text", System.Data.DbType.String);
        Funktion(modell, nameof(Suchtext), "suchtext", text);
        Funktion(modell, nameof(Suchwoerter), "suchwoerter", text);
        Funktion(modell, nameof(Namensuche), "namensuchtext", text);
        Funktion(modell, nameof(Hex), "idhex", text);
        Funktion(modell, nameof(WortAehnlichkeit), "word_similarity", new Microsoft.EntityFrameworkCore.Storage.DoubleTypeMapping("double precision"), schema: null);
        modell.HasDbFunction(typeof(SqlFunktionen).GetMethod(nameof(Ganzzahl))!)
            .HasTranslation(a => new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlUnaryExpression(System.Linq.Expressions.ExpressionType.Convert, a[0], typeof(long),
                new Microsoft.EntityFrameworkCore.Storage.LongTypeMapping("bigint")));
    }

    private static void Funktion(ModelBuilder modell, string methode, string name, Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping typ, string? schema = "public")
    {
        var m = typeof(SqlFunktionen).GetMethod(methode)!;
        modell.HasDbFunction(m).HasTranslation(a => new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression(schema, name, a, nullable: true,
            argumentsPropagateNullability: a.Select(_ => false), m.ReturnType, typ));
    }
}
