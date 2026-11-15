using System.Globalization;
using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Datenschutz;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Programm;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Application.Aufgaben;

/// <summary>Ergebnis eines Laufs der zeitgesteuerten Routinen (für Log und Tests).</summary>
public sealed record Routinenlauf(Abgleichergebnis Aufgaben, int Abgesagt, int Nachrueckangebote, int Eskaliert);

/// <summary>
/// Zeitgesteuerte Routinen (Quartz): Mindestteilnehmerprüfung, Erinnerungen vor Kursbeginn, Wartelisten-Nachrücken,
/// fehlgeschlagene Nachrichten, wartende Löschläufe und Eskalation. Alles läuft dokumentiert; kritische Schritte (Absage)
/// nur als Vorschlag, solange die Regel „Mensch gibt frei“ eingestellt ist.
/// </summary>
public sealed class Routinen(
    AufgabenDienst aufgaben,
    AnmeldungDienst anmeldung,
    IAnmeldungSpeicher belegungen,
    IProgrammSpeicher programm,
    INachrichtenSpeicher nachrichten,
    IDatenschutzSpeicher datenschutz,
    Protokoll.IAuditProtokoll protokoll,
    TimeProvider uhr)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    /// <summary>Stündlicher Lauf.</summary>
    public async Task<Routinenlauf> StuendlichAsync(CancellationToken abbruch = default)
    {
        var (mindest, abgesagt) = await MindestteilnehmerPruefenAsync(abbruch);
        var nachruecken = await anmeldung.WartelistenNachrueckenAsync(abbruch);
        var post = await PostausgangPruefenAsync(abbruch);
        var loeschlauf = await LoeschlaeufePruefenAsync(abbruch);
        var eskaliert = await aufgaben.EskalierenAsync(abbruch);
        return new(mindest + post + loeschlauf, abgesagt, nachruecken, eskaliert);
    }

    // ---------- Mindestteilnehmerzahl (FA-07) ----------

    /// <summary>
    /// Kurse, die in den nächsten X Tagen beginnen und die Mindestzahl nicht erreichen, bekommen die Aufgabe
    /// „absagen oder durchführen“. Ist eingestellt, dass die Automatik selbst handelt, wird bei Fälligkeit abgesagt.
    /// </summary>
    public async Task<(Abgleichergebnis Ergebnis, int Abgesagt)> MindestteilnehmerPruefenAsync(CancellationToken abbruch = default)
    {
        var typ = await aufgaben.TypAsync(Aufgabentypen.Mindestteilnehmer, abbruch);
        var vorlauf = typ.VorlaufTage ?? 10;
        var heute = Heute;
        var kurse = await programm.KurseMitBeginnZwischenAsync(heute.AddDays(-60).ToDateTime(TimeOnly.MinValue),
            heute.AddDays(vorlauf + 1).ToDateTime(TimeOnly.MinValue), abbruch);
        var zahlen = await belegungen.BelegungszahlenAsync(kurse.Select(k => k.Id), abbruch);
        var anlaesse = new List<AufgabenAnlass>();
        var gruende = new Dictionary<string, string>();
        var abgesagt = 0;

        foreach (var kurs in kurse)
        {
            var schluessel = Schluessel(kurs.Id);
            var (belegt, wartend) = zahlen.GetValueOrDefault(kurs.Id);
            if (kurs.Status == Kursstatus.Abgesagt)
            {
                gruende[schluessel] = "Kurs abgesagt";
                continue;
            }

            if (kurs.ErsterTermin is not { } ersterTermin)
            {
                continue;
            }

            var beginn = DateOnly.FromDateTime(ersterTermin);

            if (beginn < heute)
            {
                gruende[schluessel] = "Kurs hat begonnen";
                continue;
            }

            if (belegt >= kurs.MinTeilnehmende)
            {
                gruende[schluessel] = $"Mindestzahl erreicht ({belegt} von mindestens {kurs.MinTeilnehmende})";
                continue;
            }

            if (kurs.Status != Kursstatus.Veroeffentlicht || beginn > heute.AddDays(vorlauf))
            {
                continue;
            }

            var faellig = beginn.AddDays(-3);
            if (typ.MenschGibtFrei == false && faellig <= heute)
            {
                await anmeldung.KursAbsagenAsync(kurs.Id, $"Mindestteilnehmerzahl nicht erreicht ({belegt} von {kurs.MinTeilnehmende})", abbruch);
                gruende[schluessel] = "Kurs automatisch abgesagt (Regel: Automatik handelt selbst)";
                abgesagt++;
                continue;
            }

            anlaesse.Add(new(schluessel,
                $"{kurs.Kursnummer} {kurs.Titel}: {belegt} von mindestens {kurs.MinTeilnehmende} – absagen oder durchführen?",
                $"Beginn {ersterTermin.ToString("ddd dd.MM.yyyy, HH:mm", De)} Uhr · {belegt} angemeldet, {wartend} auf der Warteliste. "
                + "Entscheiden Sie bis zur Fälligkeit: trotzdem durchführen oder absagen (alle Teilnehmenden werden benachrichtigt, Entgelte entfallen).",
                faellig, $"kurse/{kurs.Id}", kurs.Fachbereich, beginn <= heute.AddDays(3) ? Dringlichkeit.Hoch : Dringlichkeit.Normal));
        }

        var ergebnis = await aufgaben.AbgleichenAsync(typ.Schluessel, anlaesse,
            grund: a => gruende.GetValueOrDefault(a.Schluessel, "Kurs nicht mehr im Prüfzeitraum"), abbruch: abbruch);
        return (ergebnis, abgesagt);
    }

    /// <summary>Entscheidung auf der Aufgabe: Kurs absagen oder trotz Unterbelegung durchführen.</summary>
    public async Task MindestzahlEntscheidenAsync(Guid aufgabeId, bool absagen, string? begruendung, CancellationToken abbruch = default)
    {
        var a = await aufgaben.ZumErledigenLadenAsync(aufgabeId, abbruch);
        if (a.Typ != Aufgabentypen.Mindestteilnehmer || !Guid.TryParseExact(a.Schluessel[(Aufgabentypen.Mindestteilnehmer.Length + 1)..], "N", out var kursId))
        {
            throw new InvalidOperationException("Diese Aufgabe betrifft keine Mindestteilnehmerzahl.");
        }

        if (absagen)
        {
            var n = await anmeldung.KursAbsagenAsync(kursId, string.IsNullOrWhiteSpace(begruendung) ? "Mindestteilnehmerzahl nicht erreicht" : begruendung, abbruch);
            await aufgaben.ErledigenAsync(aufgabeId, $"Kurs abgesagt, {n} Teilnehmende benachrichtigt", abbruch);
        }
        else
        {
            var kurs = await programm.KursLadenAsync(kursId, abbruch) ?? throw new InvalidOperationException("Kurs nicht gefunden.");
            await protokoll.ProtokollierenAsync("Kurs.TrotzUnterbelegung", $"{kurs.Kursnummer}: wird trotz Unterbelegung durchgeführt (Entscheidung zur Mindestteilnehmerzahl)",
                nameof(Kurs), kurs.Id, abbruch: abbruch);
            await aufgaben.ErledigenAsync(aufgabeId, "Wird trotz Unterbelegung durchgeführt" + (string.IsNullOrWhiteSpace(begruendung) ? "" : $": {begruendung.Trim()}"), abbruch);
        }
    }

    public static string Schluessel(Guid kursId) => $"{Aufgabentypen.Mindestteilnehmer}:{kursId:N}";

    // ---------- Erinnerungen vor Kursbeginn ----------

    /// <summary>Teilnehmende von Kursen, die in den nächsten <paramref name="tageVorher"/> Tagen beginnen, bekommen einmal eine Erinnerung.</summary>
    public async Task<int> ErinnerungenAsync(int tageVorher = 2, CancellationToken abbruch = default)
    {
        var heute = Heute;
        var kurse = (await programm.KurseMitBeginnZwischenAsync(heute.ToDateTime(TimeOnly.MinValue), heute.AddDays(tageVorher + 1).ToDateTime(TimeOnly.MinValue), abbruch))
            .Where(k => k.Status == Kursstatus.Veroeffentlicht && k.ErsterTermin is { } b && b > uhr.GetLocalNow().DateTime)
            .ToList();
        var anzahl = 0;
        foreach (var kurs in kurse)
        {
            anzahl += await anmeldung.ErinnernAsync(kurs.Id, abbruch);
        }

        return anzahl;
    }

    // ---------- Weitere Anlässe ----------

    public async Task<Abgleichergebnis> PostausgangPruefenAsync(CancellationToken abbruch = default)
    {
        var fehler = await nachrichten.AnzahlAsync(NachrichtenStatus.Fehlgeschlagen, abbruch);
        AufgabenAnlass[] anlaesse = fehler == 0 ? [] :
        [
            new($"{Aufgabentypen.PostausgangFehler}:offen", $"{fehler} fehlgeschlagene Nachricht(en) im Postausgang prüfen",
                "Empfängeradresse oder Schnittstelle prüfen und erneut versuchen.", Heute, "postausgang?status=Fehlgeschlagen", Dringlichkeit: Dringlichkeit.Hoch),
        ];
        return await aufgaben.AbgleichenAsync(Aufgabentypen.PostausgangFehler, anlaesse, grund: _ => "keine fehlgeschlagenen Nachrichten mehr", abbruch: abbruch);
    }

    public async Task<Abgleichergebnis> LoeschlaeufePruefenAsync(CancellationToken abbruch = default)
    {
        var anlaesse = (await datenschutz.LoeschlaeufeAsync(abbruch))
            .Where(l => l.Status is Loeschlaufstatus.Vorschau or Loeschlaufstatus.TeilweiseFreigegeben)
            .Select(l => new AufgabenAnlass($"{Aufgabentypen.LoeschlaufFreigabe}:{l.Id:N}",
                $"Löschlauf vom {l.ErstelltAm.ToLocalTime():dd.MM.yyyy} prüfen und freigeben ({l.ZuAnonymisieren} anonymisieren, {l.ZuSperren} sperren)"
                + (l.Status == Loeschlaufstatus.TeilweiseFreigegeben ? " – zweite Freigabe fehlt" : ""),
                "Vier-Augen-Prinzip: Zwei verschiedene Personen geben frei, dann wird der Lauf ausgeführt.",
                DateOnly.FromDateTime(l.ErstelltAm.ToLocalTime().Date).AddDays(14), "datenschutz"))
            .ToList();
        return await aufgaben.AbgleichenAsync(Aufgabentypen.LoeschlaufFreigabe, anlaesse, grund: _ => "Löschlauf ausgeführt oder verworfen", abbruch: abbruch);
    }
}
