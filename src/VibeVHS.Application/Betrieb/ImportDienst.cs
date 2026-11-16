using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Betrieb;

public enum Importfeld
{
    Ignorieren,
    Altnummer,
    Vorname,
    Nachname,
    Geburtsdatum,
    Strasse,
    Postleitzahl,
    Ort,
    EMail,
    Telefon,

    /// <summary>Akademischer Grad/Titel (z. B. „Dr.“). Fehlt die Spalte, wird ein Titel aus Vor- oder Nachname abgetrennt.</summary>
    Titel,
}

public enum Importstatus
{
    Neu,
    Dublette,
    DubletteInDatei,
    Fehler,
}

/// <summary>Eingelesene CSV-Datei (noch nichts gespeichert).</summary>
public sealed record ImportDatei(string Name, string Sha256, string Kodierung, char Trennzeichen, IReadOnlyList<string> Spalten, IReadOnlyList<string[]> Zeilen);

public sealed record Importzeile(int Nr, IReadOnlyDictionary<Importfeld, string> Werte, Importstatus Status, string Hinweis, Guid? DublettePersonId)
{
    public string Wert(Importfeld f) => Werte.GetValueOrDefault(f) ?? "";
}

public sealed record Importpruefung(IReadOnlyList<Importzeile> Zeilen, bool Ausgefuehrt, int Angelegt)
{
    public int Neu => Zeilen.Count(z => z.Status == Importstatus.Neu);

    public int Dubletten => Zeilen.Count(z => z.Status is Importstatus.Dublette or Importstatus.DubletteInDatei);

    public int Fehler => Zeilen.Count(z => z.Status == Importstatus.Fehler);
}

public interface IImportSpeicher
{
    void Hinzufuegen(object eintrag);

    Task<IReadOnlyList<Importlauf>> LaeufeAsync(CancellationToken abbruch = default);

    Task<bool> DateiBereitsImportiertAsync(string sha256, CancellationToken abbruch = default);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>
/// Import von Altdaten (Personen) aus KuferSQL-CSV-Exporten: Datei lesen (UTF-8 oder Windows-Zeichensatz, Trennzeichen
/// erkennen), Spalten zuordnen (Vorschlag aus typischen Spaltennamen), Probelauf mit Vorschau, Prüfung und
/// Dublettensuche – erst der ausdrückliche Import schreibt. Jede Altnummer bleibt der neuen Person zugeordnet.
/// </summary>
public sealed class ImportDienst(IDublettenSuche dubletten, IPersonenSpeicher personen, IImportSpeicher speicher, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    private static readonly Dictionary<string, Importfeld> Synonyme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tnnr"] = Importfeld.Altnummer, ["tn-nr"] = Importfeld.Altnummer, ["teilnehmernr"] = Importfeld.Altnummer, ["kundennummer"] = Importfeld.Altnummer,
        ["kdnr"] = Importfeld.Altnummer, ["nr"] = Importfeld.Altnummer, ["id"] = Importfeld.Altnummer, ["adressnr"] = Importfeld.Altnummer,
        ["vorname"] = Importfeld.Vorname, ["vname"] = Importfeld.Vorname,
        ["name"] = Importfeld.Nachname, ["nachname"] = Importfeld.Nachname, ["familienname"] = Importfeld.Nachname,
        ["geburtsdatum"] = Importfeld.Geburtsdatum, ["gebdatum"] = Importfeld.Geburtsdatum, ["geb"] = Importfeld.Geburtsdatum, ["geboren"] = Importfeld.Geburtsdatum,
        ["strasse"] = Importfeld.Strasse, ["straße"] = Importfeld.Strasse, ["str"] = Importfeld.Strasse, ["anschrift"] = Importfeld.Strasse,
        ["plz"] = Importfeld.Postleitzahl, ["postleitzahl"] = Importfeld.Postleitzahl,
        ["ort"] = Importfeld.Ort, ["wohnort"] = Importfeld.Ort,
        ["email"] = Importfeld.EMail, ["e-mail"] = Importfeld.EMail, ["mail"] = Importfeld.EMail,
        ["titel"] = Importfeld.Titel, ["akad. grad"] = Importfeld.Titel, ["akadgrad"] = Importfeld.Titel, ["grad"] = Importfeld.Titel,
        ["telefon"] = Importfeld.Telefon, ["tel"] = Importfeld.Telefon, ["telefonprivat"] = Importfeld.Telefon, ["mobil"] = Importfeld.Telefon, ["handy"] = Importfeld.Telefon,
    };

    // ---------- Lesen ----------

    public static ImportDatei Lesen(string name, byte[] inhalt)
    {
        ArgumentNullException.ThrowIfNull(inhalt);
        var sha = Convert.ToHexStringLower(SHA256.HashData(inhalt));
        string text;
        string kodierung;
        try
        {
            text = new UTF8Encoding(false, true).GetString(inhalt);
            kodierung = "UTF-8";
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(inhalt);
            kodierung = "Windows/ISO-8859-1";
        }

        text = text.TrimStart('﻿');
        var erste = text.Split('\n', 2)[0];
        var trenn = new[] { ';', '\t', ',' }.OrderByDescending(c => erste.Count(x => x == c)).First();
        var zeilen = Zerlegen(text, trenn).Where(z => z.Any(f => !string.IsNullOrWhiteSpace(f))).ToList();
        if (zeilen.Count == 0)
        {
            throw new InvalidOperationException("Die Datei ist leer.");
        }

        return new ImportDatei(name, sha, kodierung, trenn, zeilen[0].Select(s => s.Trim()).ToList(), zeilen.Skip(1).ToList());
    }

    /// <summary>CSV nach RFC 4180 (Anführungszeichen, verdoppelte Anführungszeichen, Zeilenumbrüche im Feld).</summary>
    private static IEnumerable<string[]> Zerlegen(string text, char trenn)
    {
        var feld = new StringBuilder();
        var zeile = new List<string>();
        var inZitat = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inZitat)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    feld.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inZitat = false;
                }
                else
                {
                    feld.Append(c);
                }
            }
            else if (c == '"')
            {
                inZitat = true;
            }
            else if (c == trenn)
            {
                zeile.Add(feld.ToString());
                feld.Clear();
            }
            else if (c == '\n')
            {
                zeile.Add(feld.ToString().TrimEnd('\r'));
                feld.Clear();
                yield return zeile.ToArray();
                zeile.Clear();
            }
            else
            {
                feld.Append(c);
            }
        }

        if (feld.Length > 0 || zeile.Count > 0)
        {
            zeile.Add(feld.ToString().TrimEnd('\r'));
            yield return zeile.ToArray();
        }
    }

    public static IReadOnlyDictionary<int, Importfeld> ZuordnungVorschlagen(IReadOnlyList<string> spalten)
    {
        ArgumentNullException.ThrowIfNull(spalten);
        var ergebnis = new Dictionary<int, Importfeld>();
        for (var i = 0; i < spalten.Count; i++)
        {
            var schluessel = new string(spalten[i].Where(c => char.IsLetterOrDigit(c) || c == '-' || c == 'ß').ToArray()).ToLowerInvariant();
            var feld = Synonyme.GetValueOrDefault(schluessel);
            ergebnis[i] = feld != Importfeld.Ignorieren && ergebnis.ContainsValue(feld) ? Importfeld.Ignorieren : feld;
        }

        return ergebnis;
    }

    // ---------- Prüfen und Importieren ----------

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !Betriebsrollen.Import.Contains(r)))
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Betriebsrollen.Import).Begruendung);
        }

        return wer;
    }

    /// <summary>Probelauf: prüft jede Zeile und sucht Dubletten (in der Datenbank und in der Datei) – schreibt nichts.</summary>
    public async Task<Importpruefung> ProbelaufAsync(ImportDatei datei, IReadOnlyDictionary<int, Importfeld> zuordnung, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(datei);
        ArgumentNullException.ThrowIfNull(zuordnung);
        await RechtAsync(abbruch);
        if (!zuordnung.Values.Contains(Importfeld.Nachname) || !zuordnung.Values.Contains(Importfeld.Vorname))
        {
            throw new InvalidOperationException("Bitte mindestens die Spalten für Vorname und Nachname zuordnen.");
        }

        var gesehen = new Dictionary<string, int>();
        var zeilen = new List<Importzeile>();
        for (var n = 0; n < datei.Zeilen.Count; n++)
        {
            var roh = datei.Zeilen[n];
            var werte = zuordnung.Where(z => z.Value != Importfeld.Ignorieren && z.Key < roh.Length && !string.IsNullOrWhiteSpace(roh[z.Key]))
                .ToDictionary(z => z.Value, z => roh[z.Key].Trim());
            var nr = n + 2; // Zeile in der Datei (1 = Kopf)
            string? fehler = null;
            string? titelHinweis = null;
            if (werte.ContainsKey(Importfeld.Vorname) && werte.ContainsKey(Importfeld.Nachname))
            {
                // Titel im Namen („Dr. Kai“) abtrennen; eine eigene Titelspalte bleibt maßgeblich und wird ergänzt.
                var (titel, vor, nach) = Titelerkennung.Abtrennen(werte[Importfeld.Vorname], werte[Importfeld.Nachname]);
                if (titel is not null && vor.Length > 0 && nach.Length > 0)
                {
                    werte[Importfeld.Vorname] = vor;
                    werte[Importfeld.Nachname] = nach;
                    werte[Importfeld.Titel] = Titelerkennung.Zusammen(werte.GetValueOrDefault(Importfeld.Titel), titel)!;
                    titelHinweis = $"Titel „{titel}“ aus dem Namen übernommen";
                }
            }

            DateOnly? geburt = null;
            if (!werte.ContainsKey(Importfeld.Vorname) || !werte.ContainsKey(Importfeld.Nachname))
            {
                fehler = "Vor- oder Nachname fehlt";
            }
            else if (werte.TryGetValue(Importfeld.Geburtsdatum, out var g))
            {
                if (DateOnly.TryParseExact(g, ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd.MM.yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    && d.Year > 1900 && d <= DateOnly.FromDateTime(uhr.GetLocalNow().Date))
                {
                    geburt = d.Year > DateOnly.FromDateTime(uhr.GetLocalNow().Date).Year ? d.AddYears(-100) : d;
                }
                else
                {
                    fehler = $"Geburtsdatum „{g}“ nicht lesbar (erwartet TT.MM.JJJJ)";
                }
            }

            if (fehler is null && werte.TryGetValue(Importfeld.EMail, out var mail) && !mail.Contains('@', StringComparison.Ordinal))
            {
                fehler = $"E-Mail „{mail}“ ungültig";
            }

            if (fehler is null && werte.TryGetValue(Importfeld.Postleitzahl, out var plz) && (plz.Length != 5 || !plz.All(char.IsDigit)))
            {
                fehler = $"Postleitzahl „{plz}“ ungültig";
            }

            if (fehler is not null)
            {
                zeilen.Add(new Importzeile(nr, werte, Importstatus.Fehler, fehler, null));
                continue;
            }

            var schluessel = $"{Suchtext.Normalisieren(werte[Importfeld.Vorname])}|{Suchtext.Normalisieren(werte[Importfeld.Nachname])}|{geburt}";
            if (gesehen.TryGetValue(schluessel, out var erste))
            {
                zeilen.Add(new Importzeile(nr, werte, Importstatus.DubletteInDatei, $"gleich wie Zeile {erste}", null));
                continue;
            }

            gesehen[schluessel] = nr;
            var kandidaten = await dubletten.KandidatenAsync(werte[Importfeld.Vorname], werte[Importfeld.Nachname], geburt, abbruch: abbruch);
            zeilen.Add(kandidaten.FirstOrDefault() is { } k
                ? new Importzeile(nr, werte, Importstatus.Dublette, $"vorhanden: {k.Anzeigename}{(k.Geburtsdatum is { } kg ? $", geb. {kg:dd.MM.yyyy}" : "")} – {k.Grund}", k.Id)
                : new Importzeile(nr, werte, Importstatus.Neu, titelHinweis is null ? "wird angelegt" : $"wird angelegt ({titelHinweis})", null));
        }

        return new Importpruefung(zeilen, false, 0);
    }

    /// <summary>Import: legt nur die Zeilen mit Status „Neu“ an; Dubletten und Fehler bleiben liegen (Bericht).</summary>
    public async Task<Importpruefung> ImportierenAsync(ImportDatei datei, IReadOnlyDictionary<int, Importfeld> zuordnung, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (await speicher.DateiBereitsImportiertAsync(datei.Sha256, abbruch))
        {
            throw new InvalidOperationException("Diese Datei wurde bereits importiert (gleiche Prüfsumme).");
        }

        var pruefung = await ProbelaufAsync(datei, zuordnung, abbruch);
        var jetzt = uhr.GetLocalNow();
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var zuordnungen = new List<Importzuordnung>();
        foreach (var z in pruefung.Zeilen.Where(z => z.Status == Importstatus.Neu))
        {
            DateOnly? geburt = DateOnly.TryParseExact(z.Wert(Importfeld.Geburtsdatum), ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd.MM.yy"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var g) ? (g.Year > heute.Year ? g.AddYears(-100) : g) : null;
            var p = Person.Anlegen(z.Wert(Importfeld.Vorname), z.Wert(Importfeld.Nachname), heute, jetzt, geburt,
                string.IsNullOrWhiteSpace(z.Wert(Importfeld.EMail)) ? null : z.Wert(Importfeld.EMail), "de", $"Import {datei.Name}");
            p.TitelSetzen(z.Wert(Importfeld.Titel));
            p.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, Erfassung.Erst(jetzt, $"Import {datei.Name}"));
            if (!string.IsNullOrWhiteSpace(z.Wert(Importfeld.Strasse)) && !string.IsNullOrWhiteSpace(z.Wert(Importfeld.Ort)))
            {
                p.AdresseHinzufuegen(null, z.Wert(Importfeld.Strasse), z.Wert(Importfeld.Postleitzahl), z.Wert(Importfeld.Ort), heute, Erfassung.Erst(jetzt, $"Import {datei.Name}"));
            }

            if (!string.IsNullOrWhiteSpace(z.Wert(Importfeld.Telefon)))
            {
                p.RufnummerHinzufuegen(RufnummerArt.Festnetz, z.Wert(Importfeld.Telefon), Messagingdienst.Anruf, false, true, heute, Erfassung.Erst(jetzt, $"Import {datei.Name}"));
            }

            personen.Hinzufuegen(p);
            zuordnungen.Add(new Importzuordnung(string.IsNullOrWhiteSpace(z.Wert(Importfeld.Altnummer)) ? $"Zeile {z.Nr}" : z.Wert(Importfeld.Altnummer), p.Id));
        }

        var lauf = new Importlauf(datei.Name, datei.Sha256, pruefung.Zeilen.Count, zuordnungen.Count, pruefung.Dubletten, pruefung.Fehler, zuordnungen, jetzt, wer.Anzeige);
        speicher.Hinzufuegen(lauf);
        await protokoll.ProtokollierenAsync("Import.Personen",
            $"Altdatenimport {datei.Name}: {zuordnungen.Count} Personen angelegt, {pruefung.Dubletten} Dubletten und {pruefung.Fehler} fehlerhafte Zeilen übersprungen (SHA-256 {datei.Sha256[..16]}…)",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return pruefung with { Ausgefuehrt = true, Angelegt = zuordnungen.Count };
    }

    public Task<IReadOnlyList<Importlauf>> LaeufeAsync(CancellationToken abbruch = default) => speicher.LaeufeAsync(abbruch);
}
