using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;

namespace VibeVHS.Application.Shop;

/// <summary>Einstellungen des Webshops (Abschnitt „Shop“ in appsettings).</summary>
public sealed class ShopOptionen
{
    public const string Abschnitt = "Shop";

    /// <summary>So lange hält ein Warenkorb seine Plätze (gleitend ab der letzten Änderung).</summary>
    public int ReservierungMinuten { get; set; } = 15;

    public string AgbVersion { get; set; } = "2026-08";

    public string AgbUrl { get; set; } = "https://www.vhs-egs.de/agb";

    public string DatenschutzVersion { get; set; } = "2026-08";

    public string DatenschutzUrl { get; set; } = "https://www.vhs-egs.de/datenschutz";

    /// <summary>Version des Mandatstextes (SEPA-Lastschrift).</summary>
    public string MandatVersion { get; set; } = "2026-08";

    /// <summary>Gültigkeit des Bestätigungslinks (Double-Opt-in); danach wird das unbestätigte Konto gelöscht.</summary>
    public int DoiStunden { get; set; } = 48;

    public string BarrierefreiMerkmal { get; set; } = "bf";

    /// <summary>Nur Merkmale dieser Gruppen werden veröffentlicht (keine Zulassungen o. Ä.).</summary>
    public string[] OeffentlicheMerkmalgruppen { get; set; } = ["Zugänglichkeit", "Ausstattung", "Organisation"];

    public int VerfuegbarkeitSekunden { get; set; } = 30;

    public TimeSpan Reservierung => TimeSpan.FromMinutes(ReservierungMinuten);
}

/// <summary>Fachlicher Fehler der Shop-API mit maschinenlesbarem Code und HTTP-Status.</summary>
public sealed class ShopFehler(string code, string meldung, int status = 409, IReadOnlyDictionary<string, object?>? details = null) : Exception(meldung)
{
    public string Code { get; } = code;

    public int Status { get; } = status;

    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? new Dictionary<string, object?>();
}

// ---------- Veröffentlichte Kursdaten (API-Vertrag v1) ----------

public sealed record KursKurz(
    string Nummer,
    string Titel,
    string Slug,
    int Fachbereich,
    string FachbereichName,
    string FachbereichSlug,
    string Semester,
    DateTime? Beginn,
    DateTime? Ende,
    int Termine,
    string? Zeiten,
    string? Ort,
    long? EntgeltCent,
    long? ErmaessigtCent,
    bool Barrierefrei,
    bool OnlineBuchbar,
    DateTimeOffset GeaendertAm);

public sealed record Kursliste(DateTimeOffset Stand, IReadOnlyList<KursKurz> Kurse, IReadOnlyList<string> Entfernt);

public sealed record TerminInfo(int Nummer, DateTime Beginn, DateTime Ende, IReadOnlyList<string> Orte, bool Ausgefallen);

public sealed record OrtInfo(string Name, string? Strasse, string? Postleitzahl, string? Stadt, string Wegbeschreibung, IReadOnlyList<string> Merkmale, bool Barrierefrei,
    double? Breitengrad, double? Laengengrad);

public sealed record KursDetail(
    KursKurz Kurs,
    string? Beschreibung,
    string? Bildungsurlaub,
    int MinTeilnehmende,
    int MaxTeilnehmende,
    int StornofristTage,
    long BearbeitungsgebuehrCent,
    bool Warteliste,
    IReadOnlyList<TerminInfo> Termine,
    IReadOnlyList<OrtInfo> Orte);

/// <summary>Freie Plätze und Buchbarkeit; <see cref="Code"/> ist der Code der Buchungsprüfung (wie in der Verwaltung).</summary>
public sealed record Verfuegbarkeit(
    string Kursnummer,
    int Plaetze,
    int Belegt,
    int Reserviert,
    int Frei,
    int Wartend,
    bool Warteliste,
    bool Buchbar,
    string Code,
    string Text,
    DateTimeOffset Stand);

// ---------- Warenkorb und Buchung ----------

public sealed record WarenkorbPositionAntwort(string Kursnummer, string Titel, bool Warteliste, long? EntgeltCent, string Text);

public sealed record WarenkorbAntwort(Guid Id, string Status, DateTimeOffset ReserviertBis, int RestSekunden, IReadOnlyList<WarenkorbPositionAntwort> Positionen, long SummeCent);

public sealed record TeilnehmerAngabe(string Vorname, string Nachname, DateOnly? Geburtsdatum, string EMail, string Strasse, string Postleitzahl, string Ort, string? Sprache);

public sealed record LastschriftAngabe(string Kontoinhaber, string Iban, string? Bic);

public sealed record EinwilligungAngabe(string Art, string Version, bool Erteilt);

/// <summary>Kasse: Teilnehmende (Gast; beim Konto aus der Person), Zahlart „lastschrift“ oder „rechnung“, Einwilligungen mit Version.</summary>
public sealed record BuchungsAnfrage(TeilnehmerAngabe? Teilnehmer, string Zahlart, LastschriftAngabe? Lastschrift, IReadOnlyList<EinwilligungAngabe> Einwilligungen);

public sealed record BuchungsAntwort(Guid BuchungId, Guid WarenkorbId, DateTimeOffset Zeitpunkt, string Zahlart, string? Mandatsreferenz,
    IReadOnlyList<Buchungsposition> Positionen, bool Wiederholt);

public sealed record KontoBuchung(Guid BelegungId, string Kursnummer, string Titel, DateTime? Beginn, string Status, long EntgeltCent, bool Stornierbar,
    long? StornogebuehrCent, string? StornoText);

public sealed record Shopeinstellungen(string AgbVersion, string AgbUrl, string DatenschutzVersion, string DatenschutzUrl, string MandatVersion, string MandatText,
    int ReservierungMinuten, string Einrichtung);

/// <summary>Kurzlebiger Zwischenspeicher (Verfügbarkeit, 30 s) und Sperre für Reservierung und Buchung (ein Prozess, SQLite).</summary>
public sealed class ShopZwischenspeicher
{
    private readonly ConcurrentDictionary<string, Verfuegbarkeit> _verfuegbarkeit = new(StringComparer.Ordinal);

    /// <summary>Reservieren und Buchen laufen nacheinander, damit parallele Anfragen nicht überbuchen.</summary>
    public SemaphoreSlim Sperre { get; } = new(1, 1);

    public DateTimeOffset LetzterAbgleich { get; set; } = DateTimeOffset.MinValue;

    public bool Versuchen(string kursnummer, DateTimeOffset jetzt, TimeSpan alter, out Verfuegbarkeit? v)
        => _verfuegbarkeit.TryGetValue(kursnummer, out v) && jetzt - v.Stand < alter;

    public void Merken(Verfuegbarkeit v) => _verfuegbarkeit[v.Kursnummer] = v;

    public void Vergessen(string kursnummer) => _verfuegbarkeit.TryRemove(kursnummer, out _);
}

public static class ShopText
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>URL-tauglicher Name: Kleinbuchstaben, Umlaute ausgeschrieben, sonst Bindestriche.</summary>
    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            switch (c)
            {
                case 'ä': sb.Append("ae"); break;
                case 'ö': sb.Append("oe"); break;
                case 'ü': sb.Append("ue"); break;
                case 'ß': sb.Append("ss"); break;
                case var x when x is >= 'a' and <= 'z' or >= '0' and <= '9': sb.Append(x); break;
                default:
                    if (sb.Length > 0 && sb[^1] != '-')
                    {
                        sb.Append('-');
                    }

                    break;
            }
        }

        return sb.ToString().Trim('-');
    }

    /// <summary>„Do 19:00–20:30“ aus den regelmäßigen Terminen, sonst null.</summary>
    public static string? Zeiten(IReadOnlyList<Veranstaltung> termine)
    {
        var gruppen = termine.Where(v => !v.Ausgefallen)
            .GroupBy(v => (v.Beginn.DayOfWeek, Von: TimeOnly.FromDateTime(v.Beginn), Bis: TimeOnly.FromDateTime(v.Ende)))
            .OrderByDescending(g => g.Count()).Take(2).ToList();
        return gruppen.Count == 0
            ? null
            : string.Join(", ", gruppen.Select(g => $"{KursAnzeige.Wochentag(g.Key.DayOfWeek)} {g.Key.Von.ToString("HH:mm", De)}–{g.Key.Bis.ToString("HH:mm", De)}"));
    }
}
