using System.Globalization;
using System.Text;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Durchfuehrung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Durchfuehrung;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Integration;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Integration;

/// <summary>
/// Integrationskurse (FA-40 bis FA-44, FA-134 bis FA-137): Kursdaten und Abschnitte, Teilnahmeberechtigung und Kostenbefreiung,
/// Identitätsprüfung ohne Kopie, Lichtbild nur zum Sichtvergleich, UE-genaue Anwesenheit je Abschnitt, Kostenbeitrag,
/// Abrechnung gegenüber dem BAMF, Fahrtkosten und Prüfungen – mit BAMF-Mock-Adapter.
/// </summary>
public sealed class IntegrationDienst(
    IIntegrationSpeicher speicher,
    IAnmeldungSpeicher anmeldungen,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    AnwesenheitDienst anwesenheit,
    CheckinZugang zugang,
    FinanzDienst finanzen,
    IFinanzSpeicher finanzSpeicher,
    Entfernungen entfernungen,
    IBamfSchnittstelle bamf,
    IntegrationOptionen optionen,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    public const string GrundZweifel = "Zweifel beim Sichtvergleich – Ausweiskontrolle offen";
    public const string GrundOhneAusweis = "Ausweis noch nicht geprüft";
    public const string BamfName = "Bundesamt für Migration und Flüchtlinge";
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !Integrationsrollen.Bearbeiten.Contains(r)))
        {
            throw new InvalidOperationException(Freigabe.NurFuer(wer.Rolle, Integrationsrollen.Bearbeiten).Begruendung);
        }

        return wer;
    }

    // ---------- Kursdaten ----------

    public async Task<Integrationskursdaten> KursEinrichtenAsync(Guid kursId, string bamfKursnummer, Integrationskursart art, long erstattungCentJeUe = 471,
        long beitragCentJeUe = 229, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        if ((await programm.OrtsverzeichnisAsync(abbruch)).NutzungsartVon(kurs) != Nutzungsart.Integrationskurs)
        {
            throw new InvalidOperationException($"{kurs.Kursnummer} ist kein Integrationskurs (Kursart prüfen).");
        }

        var daten = await speicher.KursdatenAsync(kursId, abbruch);
        if (daten is null)
        {
            daten = new Integrationskursdaten(kursId, bamfKursnummer, art, IntegrationskursVorlagen.Abschnitte(art), erstattungCentJeUe, beitragCentJeUe);
            speicher.Hinzufuegen(daten);
        }
        else
        {
            daten.Aendern(bamfKursnummer, art, daten.Art == art ? daten.Abschnitte : IntegrationskursVorlagen.Abschnitte(art), erstattungCentJeUe, beitragCentJeUe);
        }

        await protokoll.ProtokollierenAsync("Integration.Kursdaten", $"{kurs.Kursnummer}: BAMF-Kursnummer {daten.BamfKursnummer}, {IntegrationskursVorlagen.Name(art)}, {daten.GesamtUe} UE",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return daten;
    }

    // ---------- Übersicht ----------

    public async Task<IkUebersicht?> UebersichtAsync(Guid kursId, CancellationToken abbruch = default)
    {
        var daten = await speicher.KursdatenAsync(kursId, abbruch);
        var kurs = await programm.KursLadenAsync(kursId, abbruch);
        if (daten is null || kurs is null)
        {
            return null;
        }

        var jetzt = uhr.GetLocalNow().DateTime;
        var termine = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).OrderBy(v => v.Beginn).ToList();
        var abschnittVon = new Dictionary<Guid, int>();
        var laufend = 0m;
        foreach (var v in termine)
        {
            // Ungerundet aufsummieren (3 × 240 Min. = 16 UE, nicht 3 × 5,33 = 15,99), damit Termine im richtigen Abschnitt landen.
            var ue = (decimal)(v.Ende - v.Beginn).TotalMinutes / 45m;
            abschnittVon[v.Id] = daten.AbschnittZu(laufend + 0.001m)?.Nr ?? daten.Abschnitte.Last().Nr;
            laufend += ue;
        }

        var belegungen = (await anmeldungen.BelegungenDesKursesAsync(kursId, abbruch)).Where(b => b.Status == Belegungsstatus.Angemeldet).ToList();
        var ids = belegungen.Select(b => b.PersonId).ToList();
        var namen = await personen.AnzeigenamenAsync(ids, abbruch);
        var berechtigungen = (await speicher.BerechtigungenAsync(ids, abbruch)).GroupBy(b => b.PersonId).ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.Ausgestellt).First());
        var ausweise = (await speicher.IdentitaetspruefungenAsync(ids, abbruch)).GroupBy(p => p.PersonId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.GeprueftAm).First());
        var bilder = (await speicher.LichtbilderVorhandenAsync(kursId, abbruch)).Select(x => x.PersonId).ToHashSet();
        var anw = await speicher.AnwesenheitenDesKursesAsync(kursId, abbruch);

        var stand = daten.Abschnitte.Select(a =>
        {
            var vs = termine.Where(v => abschnittVon.GetValueOrDefault(v.Id) == a.Nr).ToList();
            var gehalten = Math.Round(vs.Where(v => v.Ende <= jetzt).Sum(v => (decimal)(v.Ende - v.Beginn).TotalMinutes) / 45m, 2);
            return new Abschnittsstand(a, Math.Round(vs.Sum(v => (decimal)(v.Ende - v.Beginn).TotalMinutes) / 45m, 2), gehalten,
                vs.Count > 0 ? DateOnly.FromDateTime(vs[0].Beginn) : null, vs.Count > 0 ? DateOnly.FromDateTime(vs[^1].Beginn) : null,
                vs.Count > 0 && vs.All(v => v.Ende <= jetzt), daten.Abrechnungen.FirstOrDefault(x => x.Nr == a.Nr)?.BelegId);
        }).ToList();

        var teilnehmende = belegungen.Select(b =>
        {
            var eigene = anw.Where(x => x.BelegungId == b.Id).ToList();
            var abschnitte = daten.Abschnitte.Select(a =>
            {
                var hier = eigene.Where(x => abschnittVon.GetValueOrDefault(x.VeranstaltungId) == a.Nr).ToList();
                return new AbschnittUe(a.Nr,
                    hier.Where(x => x.Status == Anwesenheitsstatus.Anwesend && !x.Vorlaeufig).Sum(x => x.Ue),
                    hier.Where(x => x.Status is Anwesenheitsstatus.Anwesend or Anwesenheitsstatus.Unbestaetigt && x.Vorlaeufig).Sum(x => x.Ue),
                    hier.Where(x => x.Status == Anwesenheitsstatus.Entschuldigt).Sum(x => AnwesenheitAnzeige.Ue(Dauer(kurs, x.VeranstaltungId))),
                    stand.First(s => s.Abschnitt.Nr == a.Nr).Gehalten);
            }).ToList();
            return new IkTeilnehmer(b, namen.GetValueOrDefault(b.PersonId) ?? "?", berechtigungen.GetValueOrDefault(b.PersonId), ausweise.GetValueOrDefault(b.PersonId),
                bilder.Contains(b.PersonId), abschnitte);
        }).OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();

        var hindernisse = new List<string>();
        var beginn = kurs.ErsterTermin is { } eb ? DateOnly.FromDateTime(eb) : Heute;
        foreach (var t in teilnehmende)
        {
            if (t.Berechtigung is null)
            {
                hindernisse.Add($"{t.Name}: keine Teilnahmeberechtigung erfasst");
            }
            else if (!t.Berechtigung.GiltZuKursbeginn(beginn))
            {
                hindernisse.Add($"{t.Name}: Berechtigung gilt nur bis {t.Berechtigung.GueltigBis:dd.MM.yyyy}, Kursbeginn {beginn:dd.MM.yyyy}");
            }

            if (!t.AusweisOk)
            {
                hindernisse.Add($"{t.Name}: Ausweis nicht geprüft (Originaldokument mit Berechtigung vergleichen)");
            }
        }

        if (teilnehmende.Count == 0)
        {
            hindernisse.Add("Keine angemeldeten Teilnehmenden.");
        }

        return new IkUebersicht(kurs, daten, teilnehmende, stand, hindernisse);
    }

    private static int Dauer(Kurs kurs, Guid veranstaltungId)
        => kurs.Veranstaltungen.FirstOrDefault(v => v.Id == veranstaltungId) is { } v ? (int)(v.Ende - v.Beginn).TotalMinutes : 0;

    // ---------- Berechtigung und Identität ----------

    public async Task<Teilnahmeberechtigung> BerechtigungErfassenAsync(Guid personId, Berechtigungsart art, string aktenzeichen, string stelle, DateOnly ausgestellt,
        DateOnly? gueltigBis, int ueUmfang, bool kostenbefreit, string? befreiungsgrund, bool fahrtkosten, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        Teilnahmeberechtigung b;
        try
        {
            b = new Teilnahmeberechtigung(personId, art, aktenzeichen, stelle, ausgestellt, gueltigBis, ueUmfang, kostenbefreit, befreiungsgrund,
                kostenbefreit && fahrtkosten, uhr.GetLocalNow(), wer.Anzeige);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        speicher.Hinzufuegen(b);
        await protokoll.ProtokollierenAsync("Integration.Berechtigung",
            $"{art} für {Pseudonym.Person(personId)} erfasst ({ueUmfang} UE, {(kostenbefreit ? "kostenbefreit" : "Kostenbeitrag")}{(b.Fahrtkosten ? ", Fahrtkosten" : "")})", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return b;
    }

    /// <summary>
    /// Ausweisprüfung am Originaldokument (FA-134): gespeichert werden nur Datum, Person, Dokumentart und Ergebnis.
    /// Stimmt alles, werden „vorläufig“-Vermerke wegen fehlender Prüfung aufgehoben.
    /// </summary>
    public async Task IdentitaetPruefenAsync(Guid personId, Ausweisart dokument, bool stimmtUeberein, string? bemerkung, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (!stimmtUeberein && string.IsNullOrWhiteSpace(bemerkung))
        {
            throw new InvalidOperationException("Bitte kurz festhalten, was nicht übereinstimmt (ohne Ausweisnummer).");
        }

        speicher.Hinzufuegen(new Identitaetspruefung(personId, dokument, stimmtUeberein, bemerkung, uhr.GetLocalNow(), wer.Anzeige));
        var geklaert = 0;
        if (stimmtUeberein)
        {
            foreach (var a in (await speicher.VorlaeufigeAnwesenheitenAsync(abbruch)).Where(a => a.PersonId == personId && a.VorlaeufigGrund == GrundOhneAusweis))
            {
                a.Klaeren();
                geklaert++;
            }
        }

        await protokoll.ProtokollierenAsync("Integration.Ausweis",
            $"Ausweis von {Pseudonym.Person(personId)} geprüft ({dokument}): {(stimmtUeberein ? "stimmt überein" : "Abweichung")}{(geklaert > 0 ? $"; {geklaert} vorläufige Anwesenheit(en) bestätigt" : "")}",
            nameof(Person), personId, erfolgreich: stimmtUeberein, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Ausweiskontrolle nach „Zweifel“ (FA-137): bestätigt → Anwesenheiten zählen; sonst bleiben sie unberücksichtigt (abwesend).</summary>
    public async Task AusweiskontrolleAbschliessenAsync(Guid personId, Ausweisart dokument, bool bestaetigt, string? bemerkung, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (!bestaetigt && string.IsNullOrWhiteSpace(bemerkung))
        {
            throw new InvalidOperationException("Bitte das Ergebnis der Kontrolle kurz festhalten.");
        }

        speicher.Hinzufuegen(new Identitaetspruefung(personId, dokument, bestaetigt, bemerkung, uhr.GetLocalNow(), wer.Anzeige));
        var betroffen = (await speicher.VorlaeufigeAnwesenheitenAsync(abbruch)).Where(a => a.PersonId == personId).ToList();
        foreach (var a in betroffen)
        {
            if (bestaetigt)
            {
                a.Klaeren();
            }
            else
            {
                a.Aendern(a.OrtId, Anwesenheitsstatus.Abwesend, 0, Erfassungsweg.Nacherfassung, wer.Anzeige, uhr.GetLocalNow(), $"Identität nicht bestätigt: {bemerkung}", a.Minuten);
                a.Klaeren();
            }
        }

        await protokoll.ProtokollierenAsync("Integration.Ausweiskontrolle",
            $"Ausweiskontrolle {Pseudonym.Person(personId)}: {(bestaetigt ? "bestätigt" : "nicht bestätigt")}, {betroffen.Count} vorläufige Anwesenheit(en) {(bestaetigt ? "anerkannt" : "verworfen")}",
            nameof(Person), personId, erfolgreich: bestaetigt, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Lichtbild ----------

    /// <summary>Lichtbild aufnehmen (FA-135): nur mit Information nach Art. 13 DSGVO; Löschung nach Kursende plus Frist.</summary>
    public async Task LichtbildSpeichernAsync(Guid personId, Guid kursId, byte[] inhalt, string typ, bool informiert, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        if (!informiert)
        {
            throw new InvalidOperationException("Vor der Aufnahme bitte über Zweck, Speicherdauer und Löschung informieren (Art. 13 DSGVO) und das bestätigen.");
        }

        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        var ende = kurs.Veranstaltungen.Where(v => !v.Ausgefallen).Select(v => (DateTime?)v.Ende).Max() is { } e ? DateOnly.FromDateTime(e) : Heute;
        if (await speicher.LichtbildAsync(personId, kursId, abbruch) is { } alt)
        {
            alt.Loeschen(uhr.GetLocalNow());
        }

        try
        {
            speicher.Hinzufuegen(new Lichtbild(personId, kursId, inhalt, typ, ende.AddDays(optionen.LichtbildFristTage), uhr.GetLocalNow(), wer.Anzeige));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        await protokoll.ProtokollierenAsync("Integration.Lichtbild", $"Lichtbild von {Pseudonym.Person(personId)} für {kurs.Kursnummer} aufgenommen (Löschung ab {ende.AddDays(optionen.LichtbildFristTage):dd.MM.yyyy})",
            nameof(Person), personId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task LichtbildLoeschenAsync(Guid personId, Guid kursId, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        if (await speicher.LichtbildAsync(personId, kursId, abbruch) is { } bild)
        {
            bild.Loeschen(uhr.GetLocalNow());
            await protokoll.ProtokollierenAsync("Integration.LichtbildGeloescht", $"Lichtbild von {Pseudonym.Person(personId)} gelöscht", nameof(Person), personId, abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
        }
    }

    /// <summary>Automatik (täglich): Lichtbilder nach Kursende plus Frist löschen.</summary>
    public async Task<int> LichtbilderLoeschenAsync(CancellationToken abbruch = default)
    {
        var faellig = await speicher.LichtbilderZumLoeschenAsync(Heute, abbruch);
        foreach (var b in faellig)
        {
            b.Loeschen(uhr.GetLocalNow());
            await protokoll.ProtokollierenAsync("Integration.LichtbildGeloescht", $"Lichtbild von {Pseudonym.Person(b.PersonId)} nach Kursende und Frist gelöscht", nameof(Person), b.PersonId,
                abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return faellig.Count;
    }

    // ---------- Check-in Weg B mit Sichtvergleich ----------

    /// <summary>
    /// Ausweis-Scan der Kursleitung: Bei Integrationskursen kommt zuerst der Sichtvergleich (Name und Lichtbild), erst die
    /// Bestätigung checkt ein. Leer = kein Integrationskurs (normaler Check-in).
    /// </summary>
    public async Task<Sichtvergleich?> AusweisScannenAsync(Guid veranstaltungId, string scaninhalt, CancellationToken abbruch = default)
    {
        var liste = await anwesenheit.ListeAsync(veranstaltungId, abbruch) ?? throw new InvalidOperationException("Veranstaltung nicht gefunden.");
        if (await speicher.KursdatenAsync(liste.Kurs.Id, abbruch) is null)
        {
            return null;
        }

        if (await anwesenheit.ZugriffVerweigertAsync(liste.Kurs, liste.Veranstaltung, abbruch) is { } grund)
        {
            throw new InvalidOperationException(grund);
        }

        if (await zugang.AusweisLesenAsync(scaninhalt, abbruch) is not { } personId)
        {
            throw new InvalidOperationException("Kein gültiger Teilnehmerausweis erkannt (ggf. gesperrt).");
        }

        var name = (await personen.AnzeigenamenAsync([personId], abbruch)).GetValueOrDefault(personId) ?? "?";
        var bild = await speicher.LichtbildAsync(personId, liste.Kurs.Id, abbruch);
        var ausweis = (await speicher.IdentitaetspruefungenAsync([personId], abbruch)).OrderByDescending(p => p.GeprueftAm).FirstOrDefault();
        await protokoll.ProtokollierenAsync("Integration.Sichtvergleich", $"Sichtvergleich für {Pseudonym.Person(personId)} in {liste.Kurs.Kursnummer} angezeigt", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return new Sichtvergleich(personId, name, bild is { Vorhanden: true } ? $"data:{bild.Typ};base64,{Convert.ToBase64String(bild.Inhalt)}" : null,
            ausweis is { StimmtUeberein: true },
            bild is null ? "Kein Lichtbild hinterlegt – Ausweis zeigen lassen." : ausweis is not { StimmtUeberein: true } ? "Ausweisprüfung bei der Anmeldung fehlt – Anwesenheit wird vorläufig." : null);
    }

    /// <summary>
    /// Ergebnis des Sichtvergleichs (FA-136/137): „Stimmt überein“ checkt ein; „Zweifel“ checkt vorläufig ein (zählt nicht
    /// für die BAMF-Abrechnung) und löst die Aufgabe „Ausweiskontrolle“ aus.
    /// </summary>
    public async Task<CheckinAntwort> SichtvergleichAsync(Guid veranstaltungId, Guid personId, bool stimmtUeberein, CancellationToken abbruch = default)
    {
        var antwort = await anwesenheit.AusweisCheckinPersonAsync(veranstaltungId, personId, abbruch);
        if (!antwort.Ergebnis.Ok && antwort.Ergebnis.Code != Checkincode.BereitsEingecheckt)
        {
            return antwort;
        }

        var liste = await anwesenheit.ListeAsync(veranstaltungId, abbruch);
        var a = (await speicher.AnwesenheitenDesKursesAsync(liste!.Kurs.Id, abbruch)).FirstOrDefault(x => x.VeranstaltungId == veranstaltungId && x.PersonId == personId);
        var ausweisOk = (await speicher.IdentitaetspruefungenAsync([personId], abbruch)).OrderByDescending(p => p.GeprueftAm).FirstOrDefault() is { StimmtUeberein: true };
        if (a is not null)
        {
            if (!stimmtUeberein)
            {
                a.VorlaeufigSetzen(GrundZweifel);
            }
            else if (!ausweisOk)
            {
                a.VorlaeufigSetzen(GrundOhneAusweis);
            }
        }

        await protokoll.ProtokollierenAsync("Integration.Sichtvergleich",
            $"Sichtvergleich {Pseudonym.Person(personId)} in {liste.Kurs.Kursnummer}: {(stimmtUeberein ? "stimmt überein" : "Zweifel – Anwesenheit vorläufig, Ausweiskontrolle")}",
            nameof(Person), personId, erfolgreich: stimmtUeberein, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return stimmtUeberein && ausweisOk ? antwort
            : antwort with { Ergebnis = new Checkinergebnis(Checkincode.Ok, stimmtUeberein ? "Eingecheckt – vorläufig, bis der Ausweis geprüft ist." : "Vorläufig eingecheckt – die Verwaltung kontrolliert den Ausweis.") };
    }

    // ---------- Kursbeginn ----------

    public static Freigabe KursbeginnFreigabe(IkUebersicht u)
    {
        ArgumentNullException.ThrowIfNull(u);
        return u.Daten.KursbeginnGemeldetAm is { } am ? Freigabe.Nein($"Kursbeginn wurde am {am.ToLocalTime():dd.MM.yyyy} gemeldet ({u.Daten.KursbeginnVorgang}).")
            : u.Kursbeginnhindernisse.Count > 0 ? Freigabe.Nein("Kursstart gesperrt: " + string.Join("; ", u.Kursbeginnhindernisse.Take(4)) + (u.Kursbeginnhindernisse.Count > 4 ? " …" : ""))
            : Freigabe.Ja;
    }

    /// <summary>Kursbeginn an das BAMF melden (InGe-Online, Mock) – erst wenn alle Berechtigungen und Ausweise geprüft sind (FA-134).</summary>
    public async Task<string> KursbeginnMeldenAsync(Guid kursId, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var u = await UebersichtAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kein Integrationskurs.");
        var f = KursbeginnFreigabe(u);
        if (!f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }

        var ersterOrt = u.Kurs.Veranstaltungen.FirstOrDefault()?.Orte.FirstOrDefault();
        var ort = ersterOrt is null ? "–" : (await programm.FuerKurseAsync([u.Kurs], abbruch)).Ort(ersterOrt.OrtId);
        var vorgang = await bamf.KursbeginnMeldenAsync(new BamfKursmeldung(u.Daten.BamfKursnummer, u.Kurs.Kursnummer, IntegrationskursVorlagen.Name(u.Daten.Art),
            u.Kurs.ErsterTermin is { } b ? DateOnly.FromDateTime(b) : Heute, ort, u.Teilnehmende.Select(t => t.Berechtigung!.Aktenzeichen).ToList()), abbruch);
        var daten = (await speicher.KursdatenAsync(kursId, abbruch))!;
        daten.KursbeginnGemeldet(vorgang, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Integration.Kursbeginn", $"{u.Kurs.Kursnummer}: Kursbeginn an das BAMF gemeldet ({u.Teilnehmende.Count} Teilnehmende, Vorgang {vorgang})",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return vorgang;
    }

    // ---------- Kostenbeitrag und BAMF-Abrechnung ----------

    /// <summary>Kostenbeitrag der nicht befreiten Teilnehmenden für einen Abschnitt (Rechnung je Person, vor Beginn des Abschnitts).</summary>
    public async Task<int> KostenbeitragAbrechnenAsync(Guid kursId, int abschnittNr, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var u = await UebersichtAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kein Integrationskurs.");
        var a = u.Daten.Abschnitte.FirstOrDefault(x => x.Nr == abschnittNr) ?? throw new InvalidOperationException("Abschnitt nicht gefunden.");
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var vorhanden = (await finanzSpeicher.BelegeAsync(abbruch: abbruch))
            .Where(b => b.Art == Belegart.Rechnung && !b.Storniert)
            .SelectMany(b => b.Positionen).Where(p => p.KursId == kursId && p.Text.StartsWith($"Kostenbeitrag {a.Bezeichnung} ", StringComparison.Ordinal))
            .Select(p => p.BelegungId).ToHashSet();
        var n = 0;
        foreach (var t in u.Teilnehmende.Where(t => t.Berechtigung is { Kostenbefreit: false } && !vorhanden.Contains(t.Belegung.Id)))
        {
            if (await personen.LadenAsync(t.Belegung.PersonId, abbruch) is not { IstAnonymisiert: false } person)
            {
                continue;
            }

            var empf = FinanzDienst.Personenempfaenger(person, uhr.GetLocalNow());
            var r = new Beleg(Belegart.Rechnung, Heute, person.Id, null, empf.Name, string.Join('\n', empf.Anschrift),
                [new Belegposition($"Kostenbeitrag {a.Bezeichnung} {u.Kurs.Kursnummer} ({a.Ue} UE × {new Geld(u.Daten.KostenbeitragCentJeUe)})", a.Ue * u.Daten.KostenbeitragCentJeUe,
                    a.Ue, u.Daten.KostenbeitragCentJeUe, t.Belegung.Id, kursId, art: Positionsart.Kursentgelt)],
                uhr.GetLocalNow(), wer.Anzeige, Heute.AddDays(einst.ZahlungszielTage), text: "Kostenbeitrag Integrationskurs (§ 9 IntV)");
            finanzSpeicher.Hinzufuegen(r);
            await finanzen.FestschreibenAsync(r, empf, true, abbruch: abbruch);
            n++;
        }

        await protokoll.ProtokollierenAsync("Integration.Kostenbeitrag", $"{u.Kurs.Kursnummer} {a.Bezeichnung}: {n} Kostenbeitragsrechnung(en)", nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return n;
    }

    public static Freigabe AbrechnungFreigabe(Abschnittsstand s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.AbrechnungBelegId is not null ? Freigabe.Nein("Der Abschnitt ist bereits abgerechnet.")
            : !s.Beendet ? Freigabe.Nein(s.Von is null ? "Dem Abschnitt sind noch keine Termine zugeordnet." : $"Der Abschnitt läuft noch (bis {s.Bis:dd.MM.yyyy}); abgerechnet wird nach dem letzten Termin.")
            : Freigabe.Ja;
    }

    /// <summary>
    /// Abrechnung eines beendeten Abschnitts gegenüber dem BAMF: anwesende UE (ohne vorläufige) × (Erstattungssatz − ggf. Kostenbeitrag)
    /// plus bewilligte Fahrtkosten. Beleg an das BAMF (mit XRechnung) und Abrechnungsliste an die Schnittstelle (nur Teilnehmernummern).
    /// </summary>
    public async Task<Beleg> BamfAbrechnenAsync(Guid kursId, int abschnittNr, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var u = await UebersichtAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kein Integrationskurs.");
        var s = u.Abschnitte.FirstOrDefault(x => x.Abschnitt.Nr == abschnittNr) ?? throw new InvalidOperationException("Abschnitt nicht gefunden.");
        var f = AbrechnungFreigabe(s);
        if (!f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }

        var positionen = new List<Belegposition>();
        var csv = new StringBuilder("Teilnehmernummer;Kostenbefreit;UE anwesend;UE vorlaeufig (nicht abgerechnet);UE entschuldigt;Satz;Betrag\r\n");
        foreach (var t in u.Teilnehmende.Where(t => t.Berechtigung is not null))
        {
            var ue = t.Abschnitte.First(x => x.Nr == abschnittNr);
            var satz = u.Daten.KostenerstattungCentJeUe - (t.Berechtigung!.Kostenbefreit ? 0 : u.Daten.KostenbeitragCentJeUe);
            var betrag = (long)Math.Round(ue.Anwesend * satz);
            csv.Append(CultureInfo.InvariantCulture, $"{t.Berechtigung.Aktenzeichen};{(t.Berechtigung.Kostenbefreit ? "ja" : "nein")};{ue.Anwesend.ToString("0.##", De)};{ue.Vorlaeufig.ToString("0.##", De)};{ue.Entschuldigt.ToString("0.##", De)};{(satz / 100m).ToString("0.00", De)};{(betrag / 100m).ToString("0.00", De)}\r\n");
            if (betrag > 0)
            {
                positionen.Add(new Belegposition($"{u.Daten.BamfKursnummer} {s.Abschnitt.Bezeichnung} TN {t.Berechtigung.Aktenzeichen}: {ue.Anwesend.ToString("0.##", De)} UE × {new Geld(satz)}",
                    betrag, ue.Anwesend, satz, t.Belegung.Id, kursId, art: Positionsart.Kursentgelt));
            }
        }

        foreach (var fk in (await speicher.FahrtkostenAsync(kursId, abbruch)).Where(x => x.Status == Fahrtkostenstatus.Bewilligt
                     && s.Von is { } von && s.Bis is { } bis && new DateOnly(x.Jahr, x.Monat, 1) >= new DateOnly(von.Year, von.Month, 1) && new DateOnly(x.Jahr, x.Monat, 1) <= bis))
        {
            var nr = u.Teilnehmende.FirstOrDefault(t => t.Belegung.PersonId == fk.PersonId)?.Berechtigung?.Aktenzeichen ?? "?";
            positionen.Add(new Belegposition($"Fahrtkostenerstattung TN {nr} {fk.Monat:00}/{fk.Jahr}", fk.BetragCent, kursId: kursId));
            csv.Append(CultureInfo.InvariantCulture, $"{nr};Fahrtkosten {fk.Monat:00}/{fk.Jahr};;;;;{(fk.BetragCent / 100m).ToString("0.00", De)}\r\n");
        }

        if (positionen.Count == 0)
        {
            throw new InvalidOperationException("Es gibt keine abrechenbaren UE in diesem Abschnitt (vorläufige Anwesenheiten zählen nicht).");
        }

        var org = await BamfOrganisationAsync(abbruch);
        var empf = FinanzDienst.Firmenempfaenger(org);
        var einst = await finanzen.EinstellungenAsync(abbruch);
        var r = new Beleg(Belegart.Rechnung, Heute, null, org.Id, empf.Name, string.Join('\n', empf.Anschrift), positionen, uhr.GetLocalNow(), wer.Anzeige,
            Heute.AddDays(30), text: $"Kostenerstattung Integrationskurs {u.Daten.BamfKursnummer}, {s.Abschnitt.Bezeichnung} ({s.Von:dd.MM.yyyy}–{s.Bis:dd.MM.yyyy})");
        finanzSpeicher.Hinzufuegen(r);
        await finanzen.FestschreibenAsync(r, empf, false, b => XRechnung.Erzeugen(b, empf, einst), abbruch);
        var vorgang = await bamf.AbrechnungUebermittelnAsync(new BamfAbrechnung(u.Daten.BamfKursnummer, s.Abschnitt.Bezeichnung, s.Von!.Value, s.Bis!.Value, csv.ToString(), r.BetragCent), abbruch);
        (await speicher.KursdatenAsync(kursId, abbruch))!.AbrechnungVermerken(abschnittNr, r.Id, vorgang, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Integration.Abrechnung", $"{u.Kurs.Kursnummer} {s.Abschnitt.Bezeichnung}: BAMF-Abrechnung {r.Nummer} über {new Geld(r.BetragCent)} (Vorgang {vorgang})",
            nameof(Kurs), kursId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return r;
    }

    private async Task<Organisation> BamfOrganisationAsync(CancellationToken abbruch)
    {
        var org = (await finanzSpeicher.AuftraggeberAsync(abbruch)).FirstOrDefault(o => o.Name == BamfName)
                  ?? await finanzSpeicher.OrganisationNachNameAsync(BamfName, abbruch);
        if (org is null)
        {
            org = new Organisation(BamfName, Organisationsart.Behoerde, "Nürnberg");
            org.RechnungsdatenSetzen("Frankenstraße 210\n90461 Nürnberg", "991-11111-11", "abrechnung-integrationskurse@bamf.example");
            finanzSpeicher.Hinzufuegen(org);
            await finanzSpeicher.SpeichernAsync(abbruch);
        }

        return org;
    }

    // ---------- Fahrtkosten ----------

    /// <summary>
    /// Fahrtkostenantrag (FA-42): nur für kostenbefreite Teilnehmende mit Fahrtkostenanspruch und ab 3 km einfacher Entfernung
    /// (Mock: Postleitzahl → Kursort). Ohne berechenbare Entfernung ist eine Angabe aus dem Antrag nötig.
    /// </summary>
    public async Task<IkFahrtkosten> FahrtkostenBeantragenAsync(Guid kursId, Guid personId, int jahr, int monat, long betragCent, decimal? entfernungLautAntrag = null,
        CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var berechtigung = (await speicher.BerechtigungenAsync([personId], abbruch)).OrderByDescending(b => b.Ausgestellt).FirstOrDefault();
        if (berechtigung is not { Kostenbefreit: true, Fahrtkosten: true })
        {
            throw new InvalidOperationException("Fahrtkosten erstattet das BAMF nur kostenbefreiten Teilnehmenden mit Fahrtkostenanspruch (laut Berechtigung).");
        }

        var person = await personen.LadenAsync(personId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
        var km = (await entfernungen.FuerPersonAsync(person, abbruch))(kurs.Veranstaltungen.FirstOrDefault()?.Orte.FirstOrDefault()?.OrtId) ?? entfernungLautAntrag
                 ?? throw new InvalidOperationException("Die Entfernung lässt sich nicht berechnen (Postleitzahl unbekannt) – bitte die Entfernung laut Antrag eintragen.");
        if (km < 3)
        {
            throw new InvalidOperationException($"Die einfache Entfernung beträgt nur {km:0.#} km; Fahrtkosten gibt es ab 3 km.");
        }

        if ((await speicher.FahrtkostenAsync(kursId, abbruch)).Any(f => f.PersonId == personId && f.Jahr == jahr && f.Monat == monat && f.Status != Fahrtkostenstatus.Abgelehnt))
        {
            throw new InvalidOperationException($"Für {monat:00}/{jahr} liegt bereits ein Antrag vor.");
        }

        var antrag = new IkFahrtkosten(personId, kursId, jahr, monat, km, betragCent, $"Entfernung {km:0.#} km (Wohnort → Kursort, geschätzt)", uhr.GetLocalNow());
        speicher.Hinzufuegen(antrag);
        await protokoll.ProtokollierenAsync("Integration.Fahrtkosten", $"Fahrtkostenantrag {monat:00}/{jahr} für {Pseudonym.Person(personId)} ({new Geld(betragCent)}, {km:0.#} km)", nameof(Person), personId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return antrag;
    }

    /// <summary>Bewilligung erzeugt eine Gutschrift (Auszahlung per Überweisungslauf); das BAMF erstattet über die Abschnittsabrechnung.</summary>
    public async Task FahrtkostenEntscheidenAsync(Guid antragId, bool bewilligt, string? grund, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        var antrag = await speicher.FahrtkostenantragAsync(antragId, abbruch) ?? throw new InvalidOperationException("Antrag nicht gefunden.");
        Guid? gutschrift = null;
        if (bewilligt)
        {
            var person = await personen.LadenAsync(antrag.PersonId, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
            var empf = FinanzDienst.Personenempfaenger(person, uhr.GetLocalNow());
            var g = new Beleg(Belegart.Gutschrift, Heute, person.Id, null, empf.Name, string.Join('\n', empf.Anschrift),
                [new Belegposition($"Fahrtkostenerstattung Integrationskurs {antrag.Monat:00}/{antrag.Jahr}", antrag.BetragCent, kursId: antrag.KursId)], uhr.GetLocalNow(), wer.Anzeige,
                Heute.AddDays(14), text: "Auszahlung auf Ihr Konto");
            finanzSpeicher.Hinzufuegen(g);
            await finanzen.FestschreibenAsync(g, empf, true, abbruch: abbruch);
            gutschrift = g.Id;
        }
        else if (string.IsNullOrWhiteSpace(grund))
        {
            throw new InvalidOperationException("Eine Ablehnung braucht einen Grund.");
        }

        antrag.Entscheiden(bewilligt, grund, gutschrift);
        await protokoll.ProtokollierenAsync("Integration.Fahrtkosten", $"Fahrtkostenantrag {antrag.Monat:00}/{antrag.Jahr} für {Pseudonym.Person(antrag.PersonId)} {(bewilligt ? "bewilligt" : "abgelehnt")}",
            nameof(Person), antrag.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    // ---------- Prüfungen ----------

    public async Task<Pruefungstermin> PruefungAnlegenAsync(Pruefungsart art, DateTime beginn, string ort, Guid? kursId, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var p = new Pruefungstermin(art, beginn, ort, kursId);
        speicher.Hinzufuegen(p);
        await protokoll.ProtokollierenAsync("Integration.Pruefung", $"Prüfungstermin {art} am {beginn:dd.MM.yyyy HH:mm} ({ort}) angelegt", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return p;
    }

    /// <summary>Teilnehmende anmelden und an die Prüfungsstelle melden (Mock). Nur mit erfasster Berechtigung (Teilnehmernummer).</summary>
    public async Task<string> PruefungAnmeldenAsync(Guid pruefungId, IReadOnlyList<Guid> personIds, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var p = await speicher.PruefungAsync(pruefungId, abbruch) ?? throw new InvalidOperationException("Prüfung nicht gefunden.");
        var berechtigungen = (await speicher.BerechtigungenAsync(personIds, abbruch)).GroupBy(b => b.PersonId).ToDictionary(g => g.Key, g => g.First());
        var ohne = personIds.Where(id => !berechtigungen.ContainsKey(id)).ToList();
        if (ohne.Count > 0)
        {
            throw new InvalidOperationException($"{ohne.Count} Person(en) ohne Teilnahmeberechtigung – ohne Teilnehmernummer keine Prüfungsanmeldung.");
        }

        var vorhanden = (await speicher.PruefungsanmeldungenAsync(pruefungId, abbruch)).Select(a => a.PersonId).ToHashSet();
        foreach (var id in personIds.Where(id => !vorhanden.Contains(id)))
        {
            speicher.Hinzufuegen(new Pruefungsanmeldung(pruefungId, id, uhr.GetLocalNow()));
        }

        var vorgang = await bamf.PruefungAnmeldenAsync(new BamfPruefungsmeldung(p.Art, p.Beginn, p.Ort, personIds.Select(id => berechtigungen[id].Aktenzeichen).ToList()), abbruch);
        p.Gemeldet(uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Integration.Pruefungsanmeldung", $"{personIds.Count} Person(en) zur Prüfung {p.Art} am {p.Beginn:dd.MM.yyyy} gemeldet (Vorgang {vorgang})", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return vorgang;
    }

    public async Task ErgebnisSetzenAsync(Guid anmeldungId, Pruefungsergebnis ergebnis, int? punkte, CancellationToken abbruch = default)
    {
        await RechtAsync(abbruch);
        var a = await speicher.PruefungsanmeldungAsync(anmeldungId, abbruch) ?? throw new InvalidOperationException("Anmeldung nicht gefunden.");
        var p = await speicher.PruefungAsync(a.PruefungId, abbruch) ?? throw new InvalidOperationException("Prüfung nicht gefunden.");
        try
        {
            a.ErgebnisSetzen(p.Art, ergebnis, punkte);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        await protokoll.ProtokollierenAsync("Integration.Pruefungsergebnis", $"Ergebnis {p.Art} für {Pseudonym.Person(a.PersonId)}: {ergebnis}{(punkte is { } x ? $" ({x} Punkte)" : "")}", nameof(Person),
            a.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }
}
