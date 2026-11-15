using System.Globalization;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Betrieb;

public enum Altdatenart
{
    Kurse,
    Belegungen,
}

public enum Kursfeld
{
    Ignorieren,
    Kursnummer,
    Titel,
    Semester,
    Fachbereich,
    Kursart,
    Min,
    Max,
    Entgelt,
    Ermaessigt,
    Beginn,
    Uhrzeit,
    Termine,
    Ort,
    Beschreibung,
    Status,
}

public enum Belegungsfeld
{
    Ignorieren,
    TeilnehmerAltnummer,
    Kursnummer,
    Status,
    Angemeldet,
    Zahlart,
}

/// <summary>Geprüfte Zeile eines Kurs- oder Belegungsimports; <see cref="Werte"/> nach Feldname.</summary>
public sealed record Altdatenzeile(int Nr, IReadOnlyDictionary<string, string> Werte, Importstatus Status, string Hinweis)
{
    public string Wert(string feld) => Werte.GetValueOrDefault(feld) ?? "";
}

public sealed record Altdatenpruefung(Altdatenart Art, IReadOnlyList<Altdatenzeile> Zeilen, bool Ausgefuehrt, int Angelegt)
{
    public int Neu => Zeilen.Count(z => z.Status == Importstatus.Neu);

    public int Dubletten => Zeilen.Count(z => z.Status is Importstatus.Dublette or Importstatus.DubletteInDatei);

    public int Fehler => Zeilen.Count(z => z.Status == Importstatus.Fehler);
}

/// <summary>
/// Altdatenimport für Kurse und Belegungen (KuferSQL-CSV): wie beim Personenimport erst Probelauf (Prüfung, Dubletten, Zuordnung),
/// dann ausdrücklich importieren. Belegungen finden ihre Person über die Altnummer aus früheren Personenimporten und ihren Kurs
/// über die Kursnummer. Beim Import gehen keine Mails hinaus; Entgelte richten sich nach dem Kurs (Stichtag Anmeldedatum).
/// </summary>
public sealed class AltdatenImportDienst(
    IProgrammSpeicher programm,
    IAnmeldungSpeicher anmeldungen,
    IImportSpeicher speicher,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] Datumsformate = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd.MM.yy"];

    private static readonly Dictionary<string, Kursfeld> KursSynonyme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kursnr"] = Kursfeld.Kursnummer, ["kursnummer"] = Kursfeld.Kursnummer, ["nr"] = Kursfeld.Kursnummer, ["veranstaltungsnr"] = Kursfeld.Kursnummer,
        ["titel"] = Kursfeld.Titel, ["kurstitel"] = Kursfeld.Titel, ["bezeichnung"] = Kursfeld.Titel,
        ["semester"] = Kursfeld.Semester, ["fb"] = Kursfeld.Fachbereich, ["fachbereich"] = Kursfeld.Fachbereich, ["programmbereich"] = Kursfeld.Fachbereich,
        ["kursart"] = Kursfeld.Kursart, ["art"] = Kursfeld.Kursart, ["min"] = Kursfeld.Min, ["mindest"] = Kursfeld.Min, ["max"] = Kursfeld.Max, ["hoechst"] = Kursfeld.Max,
        ["entgelt"] = Kursfeld.Entgelt, ["gebuehr"] = Kursfeld.Entgelt, ["preis"] = Kursfeld.Entgelt, ["ermaessigt"] = Kursfeld.Ermaessigt, ["ermäßigt"] = Kursfeld.Ermaessigt,
        ["beginn"] = Kursfeld.Beginn, ["start"] = Kursfeld.Beginn, ["erster termin"] = Kursfeld.Beginn, ["uhrzeit"] = Kursfeld.Uhrzeit, ["zeit"] = Kursfeld.Uhrzeit,
        ["termine"] = Kursfeld.Termine, ["anzahl"] = Kursfeld.Termine, ["ort"] = Kursfeld.Ort, ["raum"] = Kursfeld.Ort,
        ["beschreibung"] = Kursfeld.Beschreibung, ["text"] = Kursfeld.Beschreibung, ["status"] = Kursfeld.Status,
    };

    private static readonly Dictionary<string, Belegungsfeld> BelegungSynonyme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tn-nr"] = Belegungsfeld.TeilnehmerAltnummer, ["tnnr"] = Belegungsfeld.TeilnehmerAltnummer, ["teilnehmernr"] = Belegungsfeld.TeilnehmerAltnummer,
        ["kdnr"] = Belegungsfeld.TeilnehmerAltnummer, ["kursnr"] = Belegungsfeld.Kursnummer, ["kursnummer"] = Belegungsfeld.Kursnummer,
        ["status"] = Belegungsfeld.Status, ["anmeldestatus"] = Belegungsfeld.Status, ["angemeldet"] = Belegungsfeld.Angemeldet, ["anmeldedatum"] = Belegungsfeld.Angemeldet,
        ["datum"] = Belegungsfeld.Angemeldet, ["zahlart"] = Belegungsfeld.Zahlart, ["zahlweise"] = Belegungsfeld.Zahlart,
    };

    public static IReadOnlyDictionary<int, string> ZuordnungVorschlagen(Altdatenart art, IReadOnlyList<string> spalten)
    {
        ArgumentNullException.ThrowIfNull(spalten);
        var z = new Dictionary<int, string>();
        for (var i = 0; i < spalten.Count; i++)
        {
            var name = spalten[i].Trim().TrimStart('﻿');
            z[i] = art == Altdatenart.Kurse
                ? (KursSynonyme.TryGetValue(name, out var k) ? k : Kursfeld.Ignorieren).ToString()
                : (BelegungSynonyme.TryGetValue(name, out var b) ? b : Belegungsfeld.Ignorieren).ToString();
        }

        return z;
    }

    public static IReadOnlyList<string> Felder(Altdatenart art)
        => art == Altdatenart.Kurse ? Enum.GetNames<Kursfeld>() : Enum.GetNames<Belegungsfeld>();

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !Betriebsrollen.Import.Contains(r)))
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Betriebsrollen.Import).Begruendung);
        }

        return wer;
    }

    private static List<(int Nr, Dictionary<string, string> Werte)> Lesen(ImportDatei datei, IReadOnlyDictionary<int, string> zuordnung)
        => datei.Zeilen.Select((roh, n) => (n + 2, zuordnung.Where(z => z.Value != "Ignorieren" && z.Key < roh.Length && !string.IsNullOrWhiteSpace(roh[z.Key]))
            .GroupBy(z => z.Value).ToDictionary(g => g.Key, g => roh[g.First().Key].Trim()))).ToList();

    public async Task<Altdatenpruefung> ProbelaufAsync(Altdatenart art, ImportDatei datei, IReadOnlyDictionary<int, string> zuordnung, CancellationToken abbruch = default)
    {
        ArgumentNullException.ThrowIfNull(datei);
        ArgumentNullException.ThrowIfNull(zuordnung);
        await RechtAsync(abbruch);
        return art == Altdatenart.Kurse ? await KursePruefenAsync(datei, zuordnung, abbruch) : await BelegungenPruefenAsync(datei, zuordnung, abbruch);
    }

    // ---------- Kurse ----------

    private async Task<Altdatenpruefung> KursePruefenAsync(ImportDatei datei, IReadOnlyDictionary<int, string> zuordnung, CancellationToken abbruch)
    {
        foreach (var pflicht in new[] { Kursfeld.Kursnummer, Kursfeld.Titel, Kursfeld.Semester, Kursfeld.Beginn })
        {
            if (!zuordnung.Values.Contains(pflicht.ToString()))
            {
                throw new InvalidOperationException($"Bitte die Spalte für „{pflicht}“ zuordnen (Pflicht: Kursnummer, Titel, Semester, Beginn).");
            }
        }

        var semester = await programm.SemesterAsync(abbruch);
        var arten = await programm.KursartenAsync(abbruch);
        var orte = (await programm.OrtsverzeichnisAsync(abbruch)).Alle;
        var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zeilen = new List<Altdatenzeile>();
        foreach (var (nr, w) in Lesen(datei, zuordnung))
        {
            string F(Kursfeld f) => w.GetValueOrDefault(f.ToString()) ?? "";
            var fehler = new List<string>();
            var hinweise = new List<string>();
            if (F(Kursfeld.Kursnummer).Length == 0 || F(Kursfeld.Titel).Length == 0)
            {
                fehler.Add("Kursnummer oder Titel fehlt");
            }

            if (SemesterFinden(semester, F(Kursfeld.Semester)) is null)
            {
                fehler.Add($"Semester „{F(Kursfeld.Semester)}“ unbekannt (bitte zuerst anlegen)");
            }

            if (F(Kursfeld.Kursart).Length > 0 && arten.All(a => !a.Name.Equals(F(Kursfeld.Kursart), StringComparison.OrdinalIgnoreCase)))
            {
                fehler.Add($"Kursart „{F(Kursfeld.Kursart)}“ unbekannt");
            }
            else if (F(Kursfeld.Kursart).Length == 0)
            {
                hinweise.Add($"ohne Kursart → „{Standardart(arten)?.Name}“");
            }

            if (!DateOnly.TryParseExact(F(Kursfeld.Beginn), Datumsformate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                fehler.Add($"Beginn „{F(Kursfeld.Beginn)}“ nicht lesbar (TT.MM.JJJJ)");
            }

            if (F(Kursfeld.Uhrzeit).Length > 0 && Zeitraum(F(Kursfeld.Uhrzeit)) is null)
            {
                fehler.Add($"Uhrzeit „{F(Kursfeld.Uhrzeit)}“ nicht lesbar (z. B. 18:00-19:30)");
            }

            var min = Zahl(F(Kursfeld.Min), 1);
            var max = Zahl(F(Kursfeld.Max), 15);
            if (min is null || max is null || max < 1 || min > max)
            {
                fehler.Add("Mindest-/Höchstzahl passen nicht");
            }

            if (F(Kursfeld.Entgelt).Length > 0 && Euro(F(Kursfeld.Entgelt)) is null)
            {
                fehler.Add($"Entgelt „{F(Kursfeld.Entgelt)}“ nicht lesbar");
            }

            if (F(Kursfeld.Ort).Length > 0 && OrtFinden(orte, F(Kursfeld.Ort)) is null)
            {
                fehler.Add($"Ort „{F(Kursfeld.Ort)}“ unbekannt (Kurzname oder Name aus „Orte“)");
            }

            var status = Importstatus.Neu;
            if (fehler.Count > 0)
            {
                status = Importstatus.Fehler;
            }
            else if (!gesehen.Add(F(Kursfeld.Kursnummer)))
            {
                status = Importstatus.DubletteInDatei;
                hinweise.Add("Kursnummer doppelt in der Datei");
            }
            else if (await programm.KursnummerVergebenAsync(F(Kursfeld.Kursnummer), abbruch))
            {
                status = Importstatus.Dublette;
                hinweise.Add("Kursnummer gibt es schon");
            }

            zeilen.Add(new Altdatenzeile(nr, w, status, string.Join("; ", fehler.Concat(hinweise))));
        }

        return new Altdatenpruefung(Altdatenart.Kurse, zeilen, false, 0);
    }

    // ---------- Belegungen ----------

    private async Task<Altdatenpruefung> BelegungenPruefenAsync(ImportDatei datei, IReadOnlyDictionary<int, string> zuordnung, CancellationToken abbruch)
    {
        if (!zuordnung.Values.Contains(nameof(Belegungsfeld.TeilnehmerAltnummer)) || !zuordnung.Values.Contains(nameof(Belegungsfeld.Kursnummer)))
        {
            throw new InvalidOperationException("Bitte die Spalten für Teilnehmer-Altnummer und Kursnummer zuordnen.");
        }

        var personenZuordnung = await PersonenzuordnungAsync(abbruch);
        var kurse = new Dictionary<string, Kurs?>(StringComparer.OrdinalIgnoreCase);
        var gesehen = new HashSet<(Guid, Guid)>();
        var zeilen = new List<Altdatenzeile>();
        foreach (var (nr, w) in Lesen(datei, zuordnung))
        {
            string F(Belegungsfeld f) => w.GetValueOrDefault(f.ToString()) ?? "";
            var fehler = new List<string>();
            var hinweise = new List<string>();
            var personId = personenZuordnung.GetValueOrDefault(F(Belegungsfeld.TeilnehmerAltnummer));
            if (personId == Guid.Empty)
            {
                fehler.Add($"Teilnehmer {F(Belegungsfeld.TeilnehmerAltnummer)} ist nicht importiert (zuerst die Personen importieren)");
            }

            var nummer = F(Belegungsfeld.Kursnummer);
            if (!kurse.TryGetValue(nummer, out var kurs))
            {
                kurs = await KursNachNummerAsync(nummer, abbruch);
                kurse[nummer] = kurs;
            }

            if (kurs is null)
            {
                fehler.Add($"Kurs {nummer} unbekannt (zuerst die Kurse importieren)");
            }

            if (Status(F(Belegungsfeld.Status)) is null)
            {
                fehler.Add($"Status „{F(Belegungsfeld.Status)}“ unbekannt (angemeldet, warteliste, storniert)");
            }

            if (F(Belegungsfeld.Angemeldet).Length > 0 && !DateOnly.TryParseExact(F(Belegungsfeld.Angemeldet), Datumsformate, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                fehler.Add($"Anmeldedatum „{F(Belegungsfeld.Angemeldet)}“ nicht lesbar");
            }

            if (Zahlweise(F(Belegungsfeld.Zahlart)) is null)
            {
                fehler.Add($"Zahlart „{F(Belegungsfeld.Zahlart)}“ unbekannt");
            }

            var status = Importstatus.Neu;
            if (fehler.Count > 0)
            {
                status = Importstatus.Fehler;
            }
            else if (!gesehen.Add((personId, kurs!.Id)))
            {
                status = Importstatus.DubletteInDatei;
                hinweise.Add("Gleiche Person und Kurs doppelt in der Datei");
            }
            else if ((await anmeldungen.BelegungenDesKursesAsync(kurs.Id, abbruch)).Any(b => b.PersonId == personId))
            {
                status = Importstatus.Dublette;
                hinweise.Add("Belegung gibt es schon");
            }

            zeilen.Add(new Altdatenzeile(nr, w, status, string.Join("; ", fehler.Concat(hinweise))));
        }

        // Hinweis bei Überbuchung (Altdaten werden trotzdem übernommen)
        foreach (var g in zeilen.Where(z => z.Status == Importstatus.Neu && Status(z.Wert(nameof(Belegungsfeld.Status))) == Belegungsstatus.Angemeldet)
                     .GroupBy(z => z.Wert(nameof(Belegungsfeld.Kursnummer)), StringComparer.OrdinalIgnoreCase))
        {
            if (kurse.GetValueOrDefault(g.Key) is { } k)
            {
                var belegt = (await anmeldungen.BelegungenDesKursesAsync(k.Id, abbruch)).Count(b => b.HaeltPlatz) + g.Count();
                if (belegt > k.MaxTeilnehmende)
                {
                    zeilen = zeilen.Select(z => g.Contains(z) ? z with { Hinweis = Text(z.Hinweis, $"Kurs danach überbucht ({belegt} von {k.MaxTeilnehmende})") } : z).ToList();
                }
            }
        }

        return new Altdatenpruefung(Altdatenart.Belegungen, zeilen, false, 0);
    }

    // ---------- Importieren ----------

    public async Task<Altdatenpruefung> ImportierenAsync(Altdatenart art, ImportDatei datei, IReadOnlyDictionary<int, string> zuordnung, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (await speicher.DateiBereitsImportiertAsync(datei.Sha256, abbruch))
        {
            throw new InvalidOperationException("Diese Datei wurde bereits importiert (gleiche Prüfsumme).");
        }

        var pruefung = await ProbelaufAsync(art, datei, zuordnung, abbruch);
        var jetzt = uhr.GetLocalNow();
        var zuordnungen = new List<Importzuordnung>();
        var von = $"Import {datei.Name}";
        if (art == Altdatenart.Kurse)
        {
            var semester = await programm.SemesterAsync(abbruch);
            var arten = await programm.KursartenAsync(abbruch);
            var verzeichnis = await programm.OrtsverzeichnisAsync(abbruch);
            var kalender = await programm.KalenderAsync(abbruch);
            foreach (var z in pruefung.Zeilen.Where(z => z.Status == Importstatus.Neu))
            {
                string F(Kursfeld f) => z.Wert(f.ToString());
                var sem = SemesterFinden(semester, F(Kursfeld.Semester))!;
                var kursart = F(Kursfeld.Kursart).Length > 0 ? arten.First(a => a.Name.Equals(F(Kursfeld.Kursart), StringComparison.OrdinalIgnoreCase)) : Standardart(arten)!;
                var kurs = new Kurs(F(Kursfeld.Kursnummer), F(Kursfeld.Titel), sem, Zahl(F(Kursfeld.Fachbereich), 0) ?? 0, kursart, Zahl(F(Kursfeld.Min), 1)!.Value,
                    Zahl(F(Kursfeld.Max), 15)!.Value).MitBeschreibung(F(Kursfeld.Beschreibung).Length > 0 ? F(Kursfeld.Beschreibung) : null);
                if (Euro(F(Kursfeld.Entgelt)) is { } euro)
                {
                    kurs.EntgeltSetzenAb(new Geld(euro), Euro(F(Kursfeld.Ermaessigt)) is { } erm ? new Geld(erm) : null, sem.Beginn, Erfassung.Erst(jetzt, von));
                }

                var beginn = DateOnly.ParseExact(F(Kursfeld.Beginn), Datumsformate, CultureInfo.InvariantCulture);
                var (start, dauer) = Zeitraum(F(Kursfeld.Uhrzeit)) ?? (new TimeOnly(18, 0), 90);
                var ort = F(Kursfeld.Ort).Length > 0 ? OrtFinden(verzeichnis.Alle, F(Kursfeld.Ort)) : null;
                kurs.SerieFestlegen(new Serienregel
                {
                    Start = beginn, Anzahl = Zahl(F(Kursfeld.Termine), 1) ?? 1, Wochentage = [beginn.DayOfWeek], Beginn = start, DauerMinuten = dauer,
                    OrtIds = ort is null ? [] : [ort.Id], FerienAuslassen = kursart.FerienAuslassen,
                }, kalender, id => verzeichnis.Finden(id)?.Anzeige ?? "?", sem.Ende);
                kurs.StatusSetzen(F(Kursfeld.Status).ToLowerInvariant() switch
                {
                    "entwurf" or "geplant" => Kursstatus.Entwurf,
                    "abgesagt" or "ausgefallen" => Kursstatus.Abgesagt,
                    _ => Kursstatus.Veroeffentlicht,
                });
                programm.Hinzufuegen(kurs);
                zuordnungen.Add(new Importzuordnung(kurs.Kursnummer, kurs.Id));
            }

            await programm.SpeichernAsync(abbruch);
        }
        else
        {
            var personenZuordnung = await PersonenzuordnungAsync(abbruch);
            var buchender = new Buchender("Altdatenimport", null, "Import");
            foreach (var z in pruefung.Zeilen.Where(z => z.Status == Importstatus.Neu))
            {
                string F(Belegungsfeld f) => z.Wert(f.ToString());
                var kurs = (await KursNachNummerAsync(F(Belegungsfeld.Kursnummer), abbruch))!;
                var status = Status(F(Belegungsfeld.Status))!.Value;
                var am = F(Belegungsfeld.Angemeldet).Length > 0
                    ? new DateTimeOffset(DateOnly.ParseExact(F(Belegungsfeld.Angemeldet), Datumsformate, CultureInfo.InvariantCulture).ToDateTime(new TimeOnly(12, 0)), jetzt.Offset)
                    : jetzt;
                var b = Belegung.Anlegen(kurs, personenZuordnung[F(Belegungsfeld.TeilnehmerAltnummer)], status == Belegungsstatus.Warteliste, Buchungsweg.Tresen, buchender,
                    null, Zahlweise(F(Belegungsfeld.Zahlart))!.Value, am);
                if (status == Belegungsstatus.Storniert)
                {
                    b.Stornieren(0, "Altdaten: storniert übernommen", von, jetzt);
                }

                anmeldungen.Hinzufuegen(b);
                zuordnungen.Add(new Importzuordnung($"{F(Belegungsfeld.TeilnehmerAltnummer)}/{kurs.Kursnummer}", b.Id));
            }

            await anmeldungen.SpeichernAsync(abbruch);
        }

        speicher.Hinzufuegen(new Importlauf(datei.Name, datei.Sha256, pruefung.Zeilen.Count, zuordnungen.Count, pruefung.Dubletten, pruefung.Fehler, zuordnungen, jetzt, wer.Anzeige,
            art.ToString()));
        await protokoll.ProtokollierenAsync($"Import.{art}",
            $"Altdatenimport {datei.Name}: {zuordnungen.Count} {(art == Altdatenart.Kurse ? "Kurse" : "Belegungen")} angelegt, {pruefung.Dubletten} Dubletten und {pruefung.Fehler} fehlerhafte Zeilen übersprungen (SHA-256 {datei.Sha256[..16]}…)",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return pruefung with { Ausgefuehrt = true, Angelegt = zuordnungen.Count };
    }

    // ---------- Hilfen ----------

    /// <summary>Altnummer → Person aus allen bisherigen Personenimporten (jüngster gewinnt).</summary>
    private async Task<Dictionary<string, Guid>> PersonenzuordnungAsync(CancellationToken abbruch)
    {
        var d = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var lauf in (await speicher.LaeufeAsync(abbruch)).Where(l => l.Art == "Personen").OrderBy(l => l.Am))
        {
            foreach (var z in lauf.Zuordnungen)
            {
                d[z.Altnummer] = z.PersonId;
            }
        }

        return d;
    }

    private async Task<Kurs?> KursNachNummerAsync(string nummer, CancellationToken abbruch)
    {
        var zeile = (await programm.KurseAsync(new KursFilter(Suche: nummer), abbruch)).FirstOrDefault(k => k.Kursnummer.Equals(nummer, StringComparison.OrdinalIgnoreCase));
        return zeile is null ? null : await programm.KursLadenAsync(zeile.Id, abbruch);
    }

    private static Semester? SemesterFinden(IEnumerable<Semester> alle, string text)
        => alle.FirstOrDefault(s => s.Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase) || s.Kuerzel.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

    private static Kursart? Standardart(IEnumerable<Kursart> arten)
        => arten.FirstOrDefault(a => a.Name.Equals("Seminar", StringComparison.OrdinalIgnoreCase)) ?? arten.FirstOrDefault(a => a.Sichtbarkeit == Sichtbarkeit.Oeffentlich);

    private static Ort? OrtFinden(IEnumerable<Ort> orte, string text)
        => orte.FirstOrDefault(o => text.Equals(o.Kurzname, StringComparison.OrdinalIgnoreCase) || text.Equals(o.Name, StringComparison.OrdinalIgnoreCase)
                                    || text.Equals(o.Anzeige, StringComparison.OrdinalIgnoreCase));

    private static int? Zahl(string text, int standard) => text.Length == 0 ? standard : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>„89,00“, „89“, „89.00 €“ → Cent.</summary>
    private static long? Euro(string text)
    {
        var t = text.Replace("€", "", StringComparison.Ordinal).Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (t.Length == 0)
        {
            return null;
        }

        var kultur = t.Contains(',', StringComparison.Ordinal) ? De : CultureInfo.InvariantCulture;
        return decimal.TryParse(t, NumberStyles.Number, kultur, out var d) && d >= 0 ? (long)Math.Round(d * 100) : null;
    }

    /// <summary>„18:00-19:30“ oder „18.00–19.30“ → Beginn und Dauer in Minuten.</summary>
    private static (TimeOnly Beginn, int Dauer)? Zeitraum(string text)
    {
        var teile = text.Replace('–', '-').Replace('.', ':').Split('-', StringSplitOptions.TrimEntries);
        return teile.Length == 2 && TimeOnly.TryParse(teile[0], CultureInfo.InvariantCulture, out var a) && TimeOnly.TryParse(teile[1], CultureInfo.InvariantCulture, out var b) && b > a
            ? (a, (int)(b - a).TotalMinutes)
            : null;
    }

    private static Belegungsstatus? Status(string text) => text.Trim().ToLowerInvariant() switch
    {
        "" or "angemeldet" or "a" or "fest" or "teilnehmer" => Belegungsstatus.Angemeldet,
        "warteliste" or "w" or "wartend" => Belegungsstatus.Warteliste,
        "storniert" or "abgemeldet" or "s" or "storno" => Belegungsstatus.Storniert,
        _ => null,
    };

    private static Zahlart? Zahlweise(string text) => text.Trim().ToLowerInvariant() switch
    {
        "" or "rechnung" or "überweisung" or "ueberweisung" or "ü" => Zahlart.Ueberweisung,
        "lastschrift" or "sepa" or "l" or "einzug" => Zahlart.Lastschrift,
        "bar" or "b" or "kasse" => Zahlart.Bar,
        _ => null,
    };

    private static string Text(string bisher, string neu) => bisher.Length == 0 ? neu : $"{bisher}; {neu}";
}
