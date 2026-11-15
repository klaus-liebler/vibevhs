using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Application.Stammdaten;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Finanzen;

public sealed record Kettenpruefung(int Eintraege, long? FehlerNr, string? Grund)
{
    public bool Unversehrt => FehlerNr is null;
}

/// <summary>
/// GoBD: Übergabe an die Finanzsoftware (Buchungsstapel, Mock), Datenzugriff für Prüfungen (CSV + index.xml),
/// Prüfung der Hash-Kette und Bausteine der Verfahrensdokumentation (FA-34, FA-128, FA-132, FA-133).
/// </summary>
public sealed class GobdDienst(
    IFinanzSpeicher speicher,
    IFinanzprotokoll finanzprotokoll,
    FinanzDienst finanzen,
    IDatenschutzSpeicher datenschutz,
    IStammdaten stammdaten,
    PostausgangDienst post,
    IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Wer prüft und exportiert (lesend).</summary>
    public static readonly Rolle[] PruefRollen = [Rolle.Pruefung, Rolle.Buchhaltung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    // ---------- Übergabe an die Finanzsoftware ----------

    /// <summary>Alle festgeschriebenen, noch nicht übergebenen Belege als Buchungsstapel (CSV) an die Finanzsoftware (Mock).</summary>
    public async Task<Uebergabe?> UebergebenAsync(CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(Finanzrollen.Buchen, abbruch);
        var offen = (await speicher.BelegeAsync(abbruch: abbruch)).Where(b => b.Festgeschrieben && b.UebergabeId is null)
            .OrderBy(b => b.Art).ThenBy(b => b.Laufnummer).ToList();
        if (offen.Count == 0)
        {
            return null;
        }

        var alle = (await speicher.BelegeAsync(abbruch: abbruch)).ToDictionary(b => b.Id);
        var sb = new StringBuilder();
        sb.AppendLine("Belegnummer;Belegdatum;Belegart;Personenkonto;Name;Betrag;SollHaben;Sachkonto;Bezug;Buchungstext");
        foreach (var b in offen)
        {
            var wirkung = Offenposten.Wirkung(b, alle);
            foreach (var p in b.Positionen)
            {
                sb.AppendLine(string.Join(';', b.Nummer, b.Belegdatum.ToString("dd.MM.yyyy", De), BelegartInfo.Name(b.Art), Konto(b), Csv(b.EmpfaengerName),
                    (p.BetragCent / 100m).ToString("0.00", De), wirkung >= 0 ? "S" : "H", Sachkonto(b, p), b.BezugBelegId is { } bid && alle.TryGetValue(bid, out var bezug) ? bezug.Nummer : "",
                    Csv(p.Text)));
            }
        }

        var inhalt = sb.ToString();
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(inhalt)));
        var jetzt = uhr.GetLocalNow();
        var uebergabe = new Uebergabe(offen.Count, offen.Sum(b => b.BetragCent), $"buchungsstapel-{jetzt:yyyyMMdd-HHmmss}.csv", inhalt, sha, jetzt, wer.Anzeige);
        speicher.Hinzufuegen(uebergabe);
        foreach (var b in await speicher.BelegeZumAendernAsync(offen.Select(b => b.Id), abbruch))
        {
            b.UebergabeVermerken(uebergabe.Id);
        }

        await post.SchnittstelleEinreihenAsync("Finanzsoftware (Mock)", $"Buchungsstapel {uebergabe.Datei} ({offen.Count} Belege)", inhalt, abbruch);
        await finanzprotokoll.SchreibenAsync("Uebergabe.Finanzsoftware", "Uebergabe", uebergabe.Id, null,
            $"{offen.Count} Belege; {offen.First().Nummer} … {offen.Last().Nummer}; SHA-256 {sha}", abbruch);
        await protokoll.ProtokollierenAsync("Finanzen.Uebergabe", $"{offen.Count} Belege an die Finanzsoftware übergeben ({uebergabe.Datei})", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return uebergabe;
    }

    private static string Konto(Beleg b) => b.PersonId is { } p ? $"P-{p.ToString("N")[..8].ToUpperInvariant()}" : b.OrganisationId is { } o ? $"O-{o.ToString("N")[..8].ToUpperInvariant()}" : "";

    /// <summary>Platzhalter-Sachkonten des Mock-Exports (mit der Kämmerei abzustimmen).</summary>
    public static string Sachkonto(Beleg b, Belegposition p)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(p);
        return p.Art switch
        {
            Positionsart.Kursentgelt => "4411",
            Positionsart.Gebuehr => "4412",
            Positionsart.Mahngebuehr => "4413",
            Positionsart.Honorar => "5291",
            Positionsart.Zahlung => "1810",
            _ => b.Art == Belegart.Storno ? "Gegenbuchung" : "4419",
        };
    }

    // ---------- Hash-Kette ----------

    public async Task<Kettenpruefung> KettePruefenAsync(CancellationToken abbruch = default)
    {
        await finanzen.PruefenAsync(PruefRollen, abbruch);
        var eintraege = await finanzprotokoll.AlleAsync(abbruch);
        var fehler = Finanzprotokolleintrag.KettePruefen(eintraege);
        await protokoll.ProtokollierenAsync("Finanzen.Kettenpruefung", fehler is null
            ? $"Hash-Kette des Finanzprotokolls geprüft: {eintraege.Count} Einträge unversehrt"
            : $"Hash-Kette gebrochen bei Eintrag {fehler.Value.Nr}: {fehler.Value.Grund}", erfolgreich: fehler is null, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new(eintraege.Count, fehler?.Nr, fehler?.Grund);
    }

    // ---------- Datenzugriff (Z3) ----------

    /// <summary>ZIP mit Belegen, Positionen und Finanzprotokoll als CSV und index.xml (Beschreibungsstandard) – für Betriebsprüfungen.</summary>
    public async Task<(string Datei, byte[] Zip)> PruefexportAsync(DateOnly von, DateOnly bis, CancellationToken abbruch = default)
    {
        var wer = await finanzen.PruefenAsync(PruefRollen, abbruch);
        var belege = (await speicher.BelegeAsync(abbruch: abbruch)).Where(b => b.Festgeschrieben && b.Belegdatum >= von && b.Belegdatum <= bis)
            .OrderBy(b => b.Art).ThenBy(b => b.Jahr).ThenBy(b => b.Laufnummer).ToList();
        var nummern = (await speicher.BelegeAsync(abbruch: abbruch)).Where(b => b.Nummer is not null).ToDictionary(b => b.Id, b => b.Nummer!);
        var vonZeit = new DateTimeOffset(von.ToDateTime(TimeOnly.MinValue), uhr.GetLocalNow().Offset);
        var bisZeit = new DateTimeOffset(bis.AddDays(1).ToDateTime(TimeOnly.MinValue), uhr.GetLocalNow().Offset);
        var eintraege = (await finanzprotokoll.AlleAsync(abbruch)).Where(e => e.Zeitpunkt >= vonZeit && e.Zeitpunkt < bisZeit).ToList();

        var tBelege = string.Concat(belege.Select(b => Zeile(b.Nummer, BelegartInfo.Name(b.Art), D(b.Belegdatum), b.Faellig is { } f ? D(f) : "", b.ErfasstAm.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", De),
            b.FestgeschriebenAm?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", De), Konto(b), b.EmpfaengerName, G(b.BetragCent),
            b.BezugBelegId is { } x && nummern.TryGetValue(x, out var bn) ? bn : "", b.StorniertDurchId is { } s && nummern.TryGetValue(s, out var sn) ? sn : "", b.Text, b.Sha256)));
        var tPositionen = string.Concat(belege.SelectMany(b => b.Positionen.Select(p => Zeile(b.Nummer, p.Nr.ToString(De), p.Text, p.Menge.ToString("0.####", De), G(p.EinzelCent), G(p.BetragCent),
            p.Art.ToString(), Sachkonto(b, p)))));
        var tProtokoll = string.Concat(eintraege.Select(e => Zeile(e.Nr.ToString(De), e.Zeitpunkt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", De), e.BenutzerKennung, e.Aktion, e.ObjektTyp,
            e.ObjektId?.ToString(), e.Vorher, e.Nachher, e.VorigerHash, e.Hash)));
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
        var index = Index(einrichtung, von, bis);
        var doku = await VerfahrensdokuAsync(abbruch);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Datei(string name, string inhalt)
            {
                using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(inhalt);
            }

            Datei("index.xml", index);
            Datei("belege.csv", tBelege);
            Datei("positionen.csv", tPositionen);
            Datei("finanzprotokoll.csv", tProtokoll);
            Datei("verfahrensdokumentation.md", doku);
        }

        await protokoll.ProtokollierenAsync("Finanzen.Pruefexport", $"Prüfexport {von:dd.MM.yyyy}–{bis:dd.MM.yyyy} erstellt ({belege.Count} Belege, {eintraege.Count} Protokolleinträge) durch {wer.Anzeige}",
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return ($"pruefexport-{von:yyyyMMdd}-{bis:yyyyMMdd}.zip", ms.ToArray());
    }

    private static string D(DateOnly d) => d.ToString("dd.MM.yyyy", De);

    private static string G(long cent) => (cent / 100m).ToString("0.00", De);

    private static string Csv(string? s) => s is null ? "" : s.Contains(';') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal).Replace('\n', ' ')}\"" : s;

    private static string Zeile(params string?[] felder) => string.Join(';', felder.Select(Csv)) + "\r\n";

    private static readonly (string Datei, string Name, (string Spalte, string Typ)[] Spalten)[] Tabellen =
    [
        ("belege.csv", "Belege", [("Belegnummer", "A"), ("Belegart", "A"), ("Belegdatum", "D"), ("Faellig", "D"), ("Erfasst", "A"), ("Festgeschrieben", "A"), ("Personenkonto", "A"),
            ("Empfaenger", "A"), ("Betrag", "N"), ("Bezug", "A"), ("StorniertDurch", "A"), ("Text", "A"), ("SHA256", "A")]),
        ("positionen.csv", "Belegpositionen", [("Belegnummer", "A"), ("Position", "N0"), ("Text", "A"), ("Menge", "N4"), ("Einzelpreis", "N"), ("Betrag", "N"), ("Positionsart", "A"), ("Sachkonto", "A")]),
        ("finanzprotokoll.csv", "Finanzprotokoll (Hash-Kette)", [("Nr", "N0"), ("Zeitpunkt", "A"), ("Benutzer", "A"), ("Aktion", "A"), ("Objekttyp", "A"), ("ObjektId", "A"),
            ("Vorher", "A"), ("Nachher", "A"), ("VorigerHash", "A"), ("Hash", "A")]),
    ];

    /// <summary>index.xml nach dem Beschreibungsstandard für die Datenträgerüberlassung (GDPdU, Version 01-09-2004).</summary>
    public static string Index(string einrichtung, DateOnly von, DateOnly bis)
    {
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("DataSet", null, "gdpdu-01-09-2004.dtd", null),
            new XElement("DataSet",
                new XElement("Version", "1.0"),
                new XElement("DataSupplier", new XElement("Name", einrichtung), new XElement("Location", "Deutschland"), new XElement("Comment", "VibeVHS Prüfexport")),
                new XElement("Media",
                    new XElement("Name", $"Prüfexport {D(von)} bis {D(bis)}"),
                    Tabellen.Select(t => new XElement("Table",
                        new XElement("URL", t.Datei),
                        new XElement("Name", t.Name),
                        new XElement("Validity", new XElement("Range", new XElement("From", D(von)), new XElement("To", D(bis))), new XElement("Format", "DD.MM.YYYY")),
                        new XElement("UTF8"),
                        new XElement("DecimalSymbol", ","),
                        new XElement("DigitGroupingSymbol", "."),
                        new XElement("VariableLength",
                            new XElement("ColumnDelimiter", ";"),
                            new XElement("RecordDelimiter", "§CRLF§"),
                            new XElement("TextEncapsulator", "\""),
                            t.Spalten.Select((s, i) => new XElement(i == 0 && t.Datei != "positionen.csv" ? "VariablePrimaryKey" : "VariableColumn",
                                new XElement("Name", s.Spalte),
                                s.Typ switch
                                {
                                    "D" => new XElement("Date", new XElement("Format", "DD.MM.YYYY")),
                                    "N" => new XElement("Numeric", new XElement("Accuracy", "2")),
                                    "N4" => new XElement("Numeric", new XElement("Accuracy", "4")),
                                    "N0" => new XElement("Numeric"),
                                    _ => new XElement("AlphaNumeric"),
                                }))))))));
        return (doc.Declaration + Environment.NewLine + doc.DocumentType + Environment.NewLine + doc.Root).Replace("§CRLF§", "&#13;&#10;", StringComparison.Ordinal);
    }

    // ---------- Verfahrensdokumentation ----------

    /// <summary>Aktuelle Bausteine der Verfahrensdokumentation aus dem Systemstand (FA-133).</summary>
    public async Task<string> VerfahrensdokuAsync(CancellationToken abbruch = default)
    {
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var kreise = await speicher.NummernkreiseAsync(abbruch);
        var fristen = await datenschutz.FristenAsync(abbruch);
        var eintraege = await finanzprotokoll.AlleAsync(abbruch);
        var einrichtung = (await stammdaten.EinrichtungAsync(abbruch))?.Name ?? "Volkshochschule";
        var sb = new StringBuilder();
        sb.AppendLine($"# Verfahrensdokumentation Finanzen – Bausteine ({einrichtung})").AppendLine();
        sb.AppendLine($"Erzeugt am {uhr.GetLocalNow():dd.MM.yyyy HH:mm} aus dem aktuellen Systemstand von VibeVHS. Diese Bausteine ersetzen nicht die von der Einrichtung zu verantwortende Gesamtdokumentation.").AppendLine();
        sb.AppendLine("## 1. Belegarten und Nummernkreise").AppendLine();
        sb.AppendLine("| Belegart | Kürzel | Wirkung auf das Personenkonto | Aufbewahrung |").AppendLine("|---|---|---|---|");
        foreach (var art in Enum.GetValues<Belegart>())
        {
            sb.AppendLine($"| {BelegartInfo.Name(art)} | {BelegartInfo.Kuerzel(art)} | {(BelegartInfo.Vorzeichen(art) > 0 ? "Soll" : BelegartInfo.Vorzeichen(art) < 0 ? "Haben" : "Gegenbuchung zum stornierten Beleg")} | Frist „{BelegartInfo.Frist(art)}“ |");
        }

        sb.AppendLine().AppendLine("Nummern haben die Form `KÜRZEL-JAHR-LAUFNUMMER` (z. B. RE-2026-00042) und laufen je Belegart und Belegjahr lückenlos. Eine Nummer wird erst bei der Festschreibung gezogen, verworfene Entwürfe verbrauchen keine Nummer. Eine optimistische Sperre auf dem Nummernkreis verhindert doppelte Nummern bei gleichzeitiger Festschreibung.").AppendLine();
        sb.AppendLine("| Belegart | Jahr | zuletzt vergeben |").AppendLine("|---|---|---|");
        foreach (var k in kreise.OrderBy(k => k.Jahr).ThenBy(k => k.Art))
        {
            sb.AppendLine($"| {BelegartInfo.Name(k.Art)} | {k.Jahr} | {k.Letzte} |");
        }

        sb.AppendLine().AppendLine("## 2. Festschreibung und Unveränderbarkeit").AppendLine();
        sb.AppendLine("- Rechnungen, Gebührenrechnungen, Zahlungen, Mahnungen und Stornos werden bei der Erstellung festgeschrieben. Honorarabrechnungen entstehen als Entwurf und werden mit der Freigabe festgeschrieben.");
        sb.AppendLine("- Festgeschriebene Belege werden nie geändert oder gelöscht. Korrekturen erfolgen ausschließlich durch einen Stornobeleg mit Begründung und Verweis auf den Ursprungsbeleg und ggf. einen Neubeleg.");
        sb.AppendLine("- Jeder Beleg trägt Belegdatum, Erfassungszeitpunkt und erfassende Person sowie eine SHA-256-Prüfsumme über alle steuerlich relevanten Inhalte. Druckstücke liegen im Dokumentarchiv mit eigener Prüfsumme; ein Nachdruck wird nur nach erfolgreicher Prüfsummenprüfung ausgegeben.");
        sb.AppendLine("- Honorarabrechnungen speichern Honorarmodell, Version und alle Parameter als Snapshot (Teil der Prüfsumme).");
        sb.AppendLine("- Spätestens mit der Übergabe an die Finanzsoftware sind alle übergebenen Belege festgeschrieben; Entwürfe werden nicht übergeben.").AppendLine();
        sb.AppendLine("## 3. Änderungsprotokoll (Hash-Kette)").AppendLine();
        sb.AppendLine($"Alle Änderungen an Finanzdaten (Belege, Mandate, Bankverbindungen, Läufe, Zuordnungen, Verträge, Einstellungen) stehen mit Zeitpunkt, Person, vorherigem und neuem Wert im Finanzprotokoll. Jeder Eintrag enthält den SHA-256 seines Vorgängers; eine Änderung oder Löschung bricht die Kette und wird bei der Prüfung erkannt. Personenbezug nur über Pseudonyme. Aktueller Stand: {eintraege.Count} Einträge.").AppendLine();
        sb.AppendLine("## 4. Schnittstellen").AppendLine();
        sb.AppendLine("- **SEPA-Lastschrift** (pain.008.001.08) mit Mandatsverwaltung, Vorabankündigung (" + einst.VorabankuendigungTage + " Tage) und Freigabe durch die Buchhaltung; Gläubiger-ID " + einst.GlaeubigerId + ".");
        sb.AppendLine("- **SEPA-Überweisung** (pain.001.001.09) für Honorare.");
        sb.AppendLine("- **Kontoauszug** camt.053 (001.02–001.08): Einlesen mit Dublettenprüfung (SHA-256 und Auszugs-ID), Zuordnungsvorschlag, Buchung nur durch Bestätigung.");
        sb.AppendLine("- **Finanzsoftware** (Mock): Buchungsstapel als CSV mit Prüfsumme; jede Übergabe ist im Postausgang und im Finanzprotokoll dokumentiert.");
        sb.AppendLine("- **XRechnung** 3.0 (UBL) für Firmenkunden und öffentliche Auftraggeber.").AppendLine();
        sb.AppendLine("## 5. Rollen und Rechte").AppendLine();
        sb.AppendLine("| Rolle | Finanzen |").AppendLine("|---|---|");
        sb.AppendLine("| Buchhaltung, Verwaltungsleitung, Leitung | buchen, stornieren, Läufe freigeben, exportieren |");
        sb.AppendLine("| Teilnehmerservice | Konto ansehen, Mandate und Bankverbindungen erfassen |");
        sb.AppendLine("| Fachbereichsleitung | Honorarverträge pflegen |");
        sb.AppendLine("| Prüfung | nur lesen: Belege, Konten, Protokolle; Prüfexport (CSV + index.xml) |").AppendLine();
        sb.AppendLine("## 6. Aufbewahrung").AppendLine();
        sb.AppendLine("| Frist | Jahre | ab Jahresende | Rechtsgrundlage |").AppendLine("|---|---|---|---|");
        foreach (var f in fristen)
        {
            sb.AppendLine($"| {f.Bezeichnung} | {f.Jahre} | {(f.AbJahresende ? "ja" : "nein")} | {f.Rechtsgrundlage} |");
        }

        sb.AppendLine().AppendLine("Personen mit Belegen in laufender Frist werden auf ein Löschverlangen hin gesperrt statt anonymisiert (Art. 17 Abs. 3 DSGVO); der Löschlauf nennt die längste Frist und anonymisiert nach deren Ende. Belegnummer, Betrag und Prüfsumme bleiben dabei erhalten.");
        return sb.ToString();
    }

    public static string Geldtext(long cent) => new Geld(cent).ToString();
}
