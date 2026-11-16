using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Durchfuehrung;

public enum Anwesenheitsstatus
{
    Anwesend,
    Abwesend,
    Entschuldigt,

    /// <summary>Selbst eingecheckt; die Kursleitung muss noch bestätigen (abrechnungsrelevante Kurse).</summary>
    Unbestaetigt,
}

/// <summary>Wie die Anwesenheit erfasst wurde.</summary>
public enum Erfassungsweg
{
    /// <summary>Teilnehmende haben den Raum-QR-Code gescannt und ihren persönlichen Code eingegeben.</summary>
    RaumQr,

    /// <summary>Dozent/in oder Verwaltung hat den persönlichen QR-Code (Teilnehmerausweis) gescannt.</summary>
    Ausweis,

    /// <summary>Häkchen in der Anwesenheitsliste.</summary>
    Liste,

    /// <summary>Nachträglich erfasst (nach Ende der Veranstaltung), mit Begründung.</summary>
    Nacherfassung,

    /// <summary>Beim Abschließen der Liste: nicht erfasst = nicht anwesend.</summary>
    Automatik,
}

public static class AnwesenheitAnzeige
{
    public static string Status(Anwesenheitsstatus s) => s switch
    {
        Anwesenheitsstatus.Anwesend => "anwesend",
        Anwesenheitsstatus.Abwesend => "nicht anwesend",
        Anwesenheitsstatus.Unbestaetigt => "selbst eingecheckt – Bestätigung offen",
        _ => "entschuldigt",
    };

    public static string Weg(Erfassungsweg w) => w switch
    {
        Erfassungsweg.RaumQr => "Raum-QR",
        Erfassungsweg.Ausweis => "Ausweis gescannt",
        Erfassungsweg.Liste => "Liste",
        Erfassungsweg.Nacherfassung => "Nacherfassung",
        _ => "automatisch",
    };

    /// <summary>Unterrichtseinheiten (45 Minuten), auf zwei Stellen gerundet.</summary>
    public static decimal Ue(int minuten) => Math.Round(minuten / 45m, 2);
}

/// <summary>Anwesenheit einer Person in einer Veranstaltung, minuten- und UE-genau.</summary>
public sealed class Anwesenheit
{
    private Anwesenheit()
    {
        ErfasstVon = string.Empty;
    }

    public Anwesenheit(Belegung belegung, Veranstaltung veranstaltung, Guid? ortId, Anwesenheitsstatus status, int minuten, Erfassungsweg weg,
        string von, DateTimeOffset jetzt, string? bemerkung = null)
    {
        ArgumentNullException.ThrowIfNull(belegung);
        ArgumentNullException.ThrowIfNull(veranstaltung);
        Id = Ids.Neu();
        KursId = belegung.KursId;
        VeranstaltungId = veranstaltung.Id;
        PersonId = belegung.PersonId;
        BelegungId = belegung.Id;
        Datum = veranstaltung.Datum;
        ErfasstVon = von;
        Setzen(ortId, status, minuten, weg, von, jetzt, bemerkung, (int)(veranstaltung.Ende - veranstaltung.Beginn).TotalMinutes);
        ErstErfasstAm = jetzt;
    }

    public Guid Id { get; private set; }

    public Guid KursId { get; private set; }

    public Guid VeranstaltungId { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid BelegungId { get; private set; }

    public DateOnly Datum { get; private set; }

    public Guid? OrtId { get; private set; }

    public Anwesenheitsstatus Status { get; private set; }

    /// <summary>Anwesende Minuten (bei voller Teilnahme die Dauer der Veranstaltung).</summary>
    public int Minuten { get; private set; }

    public decimal Ue => AnwesenheitAnzeige.Ue(Minuten);

    public Erfassungsweg Weg { get; private set; }

    public DateTimeOffset ErstErfasstAm { get; private set; }

    public DateTimeOffset ErfasstAm { get; private set; }

    public string ErfasstVon { get; private set; }

    public string? Bemerkung { get; private set; }

    /// <summary>
    /// Vorläufig, solange die Identität nicht geklärt ist (Zweifel beim Sichtvergleich oder fehlende Ausweisprüfung):
    /// zählt nicht für die BAMF-Abrechnung (FA-137).
    /// </summary>
    public bool Vorlaeufig { get; private set; }

    public string? VorlaeufigGrund { get; private set; }

    public void VorlaeufigSetzen(string grund)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grund);
        Vorlaeufig = true;
        VorlaeufigGrund = grund;
    }

    public void Klaeren()
    {
        Vorlaeufig = false;
        VorlaeufigGrund = null;
    }

    public void Aendern(Guid? ortId, Anwesenheitsstatus status, int minuten, Erfassungsweg weg, string von, DateTimeOffset jetzt, string? bemerkung, int dauer)
        => Setzen(ortId, status, minuten, weg, von, jetzt, bemerkung, dauer);

    private void Setzen(Guid? ortId, Anwesenheitsstatus status, int minuten, Erfassungsweg weg, string von, DateTimeOffset jetzt, string? bemerkung, int dauer)
    {
        if (status is Anwesenheitsstatus.Anwesend or Anwesenheitsstatus.Unbestaetigt && (minuten <= 0 || minuten > dauer))
        {
            throw new ArgumentException($"Die Minuten müssen zwischen 1 und {dauer} liegen (Dauer der Veranstaltung).");
        }

        OrtId = ortId ?? OrtId;
        Status = status;
        Minuten = status is Anwesenheitsstatus.Anwesend or Anwesenheitsstatus.Unbestaetigt ? minuten : 0;
        Weg = weg;
        ErfasstVon = von;
        ErfasstAm = jetzt;
        Bemerkung = string.IsNullOrWhiteSpace(bemerkung) ? Bemerkung : bemerkung.Trim();
    }
}

/// <summary>Abschluss einer Anwesenheitsliste: Wer bis dahin nicht erfasst ist, gilt als nicht anwesend (Nichterscheinen-Regel).</summary>
public sealed class Listenabschluss
{
    private Listenabschluss()
    {
        Von = string.Empty;
    }

    public Listenabschluss(Guid veranstaltungId, Guid kursId, DateTimeOffset am, string von)
    {
        VeranstaltungId = veranstaltungId;
        KursId = kursId;
        Am = am;
        Von = von;
    }

    public Guid VeranstaltungId { get; private set; }

    public Guid KursId { get; private set; }

    public DateTimeOffset Am { get; private set; }

    public string Von { get; private set; }
}

public enum Checkincode
{
    Ok,
    NichtAngemeldet,
    Ausgefallen,
    ZuFrueh,
    ZuSpaet,
    KeineEinbuchung,
    FalscherOrt,
    KontingentAufgebraucht,
    BereitsEingecheckt,
}

public sealed record Checkinergebnis(Checkincode Code, string Text)
{
    public bool Ok => Code == Checkincode.Ok;
}

/// <summary>Regeln für den Check-in: Zeitfenster, Status der Belegung, Vorab-Einbuchung bei Kontingenten.</summary>
public static class Checkinregel
{
    /// <summary>Check-in ab 30 Minuten vor Beginn bis zum Ende der Veranstaltung.</summary>
    public static readonly TimeSpan Vorlauf = TimeSpan.FromMinutes(30);

    public static Checkinergebnis Pruefen(Belegung? belegung, Kurs kurs, Veranstaltung v, Guid? ortId, bool bereitsAnwesend, DateTimeOffset jetzt,
        bool zeitfensterPruefen = true)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        ArgumentNullException.ThrowIfNull(v);
        if (belegung is null || belegung.Status != Belegungsstatus.Angemeldet)
        {
            return new(Checkincode.NichtAngemeldet, $"Für {kurs.Kursnummer} „{kurs.Titel}“ liegt keine Anmeldung vor. Bitte am Tresen melden.");
        }

        if (v.Ausgefallen)
        {
            return new(Checkincode.Ausgefallen, "Diese Veranstaltung fällt aus.");
        }

        var lokal = jetzt.LocalDateTime;
        if (zeitfensterPruefen && lokal < v.Beginn - Vorlauf)
        {
            return new(Checkincode.ZuFrueh, $"Check-in ist ab {v.Beginn - Vorlauf:HH:mm} Uhr möglich.");
        }

        if (zeitfensterPruefen && lokal > v.Ende)
        {
            return new(Checkincode.ZuSpaet, $"Die Veranstaltung ist um {v.Ende:HH:mm} Uhr zu Ende. Bitte an die Kursleitung wenden (Nacherfassung).");
        }

        if (ortId is { } o && v.Orte.All(x => x.OrtId != o))
        {
            return new(Checkincode.FalscherOrt, "Dieser Code gehört nicht zum Ort dieser Veranstaltung. Bitte den QR-Code im richtigen Raum scannen.");
        }

        if (bereitsAnwesend)
        {
            return new(Checkincode.BereitsEingecheckt, "Bereits eingecheckt – danke!");
        }

        if (belegung.IstKontingent && kurs.Richtlinie.VorabEinbuchung && !belegung.Einbuchungen.ContainsKey(v.Id))
        {
            return new(Checkincode.KeineEinbuchung,
                "Für diese Veranstaltung besteht keine Einbuchung. Ohne Einbuchung ist kein Check-in möglich – bitte vorher einbuchen (am Tresen oder bei der Kursleitung).");
        }

        if (belegung.IstKontingent && !belegung.Einbuchungen.ContainsKey(v.Id) && belegung.Kontingent.Verbleibend <= 0)
        {
            return new(Checkincode.KontingentAufgebraucht, "Das Kontingent ist aufgebraucht. Bitte ein neues Kontingent buchen.");
        }

        return new(Checkincode.Ok, "Eingecheckt – viel Spaß!");
    }
}
