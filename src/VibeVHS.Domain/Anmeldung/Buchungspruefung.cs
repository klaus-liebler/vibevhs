using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Anmeldung;

/// <summary>Ergebnis der Buchungsprüfung. Jeder Code hat einen verständlichen Grund und – wo möglich – den Weg, wie es doch geht.</summary>
public enum Buchungscode
{
    Buchbar,
    Warteliste,
    Archiviert,
    Abgesagt,
    Entwurf,
    Beendet,
    PersonNichtAktiv,
    NurOeffentlich,
    InternOhneBerechtigung,
    WegNichtErlaubt,
    GeschlossenZielgruppe,
    VollmachtFehlt,
    VollmachtAbgelaufen,
    BereitsAngemeldet,
    BereitsWartend,
    Ausgebucht,
}

public sealed record Buchungsergebnis(Buchungscode Code, string? Begruendung = null, string? Weg = null, bool Ueberbuchbar = false, Guid? VollmachtId = null)
{
    public bool Moeglich => Code is Buchungscode.Buchbar or Buchungscode.Warteliste;

    public bool AufWarteliste => Code == Buchungscode.Warteliste;

    /// <summary>Text für Tooltips: Grund und Weg.</summary>
    public string Erklaerung => string.Join(" ", new[] { Begruendung, Weg }.Where(t => !string.IsNullOrWhiteSpace(t)));

    public string Kurztext => Code switch
    {
        Buchungscode.Buchbar => "buchbar",
        Buchungscode.Warteliste => "Warteliste",
        Buchungscode.BereitsAngemeldet => "bereits angemeldet",
        Buchungscode.BereitsWartend => "auf der Warteliste",
        _ => "nicht buchbar",
    };
}

/// <summary>Belegungslage eines Kurses und die bestehende Belegung der Person (falls vorhanden).</summary>
public sealed record Buchungslage(int Belegt, int Wartend, Belegungsstatus? Bestehend = null);

/// <summary>
/// Die eine Buchungsprüfung für alle Wege (Tresen, Integrationskurs-Team, online, Auftraggeber).
/// Liefert einen Begründungscode, den die Oberfläche als Tooltip erklärt.
/// </summary>
public static class Buchungspruefung
{
    public static Buchungsergebnis Pruefen(Kurs kurs, Person person, Buchungsweg weg, Guid? bevollmaechtigterId, Buchungslage lage, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(lage);
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var r = kurs.Richtlinie;

        if (kurs.Archiviert)
        {
            return new(Buchungscode.Archiviert, "Der Kurs ist archiviert und schreibgeschützt.");
        }

        if (kurs.Status == Kursstatus.Abgesagt)
        {
            return new(Buchungscode.Abgesagt, "Der Kurs ist abgesagt.", "Einen ähnlichen Kurs wählen (Umbuchungsvorschläge auf der Belegung).");
        }

        if (kurs.Status == Kursstatus.Entwurf)
        {
            return new(Buchungscode.Entwurf, "Der Kurs ist noch ein Entwurf und nicht veröffentlicht.", "Die Fachbereichsleitung veröffentlicht ihn.");
        }

        if (kurs.BeendetAm(jetzt.LocalDateTime))
        {
            return new(Buchungscode.Beendet, $"Der Kurs ist am {kurs.Kursende:dd.MM.yyyy} zu Ende gegangen.");
        }

        if (person.Datenschutz != Datenschutzstatus.Aktiv)
        {
            return new(Buchungscode.PersonNichtAktiv, $"{person.Anzeigename} ist {DatenschutzAnzeige.Text(person.Datenschutz)}.",
                person.Datenschutz == Datenschutzstatus.Archiviert ? "Die Person zuerst auf ihrer Seite reaktivieren." : null);
        }

        // FA-62: Selbstregistrierte buchen nur öffentliche Kurse – unabhängig von der Richtlinie.
        if (weg == Buchungsweg.Online && kurs.Sichtbarkeit != Sichtbarkeit.Oeffentlich)
        {
            return new(Buchungscode.NurOeffentlich,
                $"Online können nur öffentliche Kurse gebucht werden; dieser Kurs ist {KursAnzeige.Sichtbarkeit(kurs.Sichtbarkeit)}.",
                "Bitte wenden Sie sich an den Teilnehmerservice der vhs.");
        }

        if (!r.Darf(weg))
        {
            var wege = r.Rechte.Where(x => x.DarfBuchen).Select(x => Buchungsrichtlinie.WegAnzeige(x.Weg)).ToList();
            var ueber = wege.Count == 0 ? "niemanden" : string.Join(" oder ", wege);
            return kurs.Sichtbarkeit == Sichtbarkeit.Intern
                ? new(Buchungscode.InternOhneBerechtigung, $"Interner Kurs: Buchung nur über {ueber}.", $"Bitte an {wege.FirstOrDefault() ?? "die Verwaltung"} weitergeben.")
                : new(Buchungscode.WegNichtErlaubt, $"Dieser Kurs ist nur über {ueber} buchbar.", $"Bitte an {wege.FirstOrDefault() ?? "die Verwaltung"} weitergeben.");
        }

        Guid? vollmachtId = null;
        if (bevollmaechtigterId is { } bev && bev != person.Id)
        {
            var gueltig = person.Vollmachten.AlleAmStichtag(heute, jetzt).FirstOrDefault(v => v.BevollmaechtigterId == bev);
            var ueberOrganisation = weg == Buchungsweg.Auftraggeber
                && person.Organisationen.AlleAmStichtag(heute, jetzt).Any(o => o.OrganisationId == bev);
            if (gueltig is not null)
            {
                vollmachtId = gueltig.VollmachtId;
            }
            else if (!ueberOrganisation)
            {
                var frueher = person.Vollmachten.Where(v => v.BevollmaechtigterId == bev && v.IstAktuellerWissensstand && !v.IstEndemarke)
                    .OrderByDescending(v => v.GueltigVon).FirstOrDefault();
                return frueher is not null
                    ? new(Buchungscode.VollmachtAbgelaufen,
                        $"Die Buchungsvollmacht von {frueher.BevollmaechtigterName} für {person.Anzeigename} gilt heute nicht (Zeitraum {frueher.GueltigVon:dd.MM.yyyy}–{(frueher.GueltigBis is { } b ? b.AddDays(-1).ToString("dd.MM.yyyy") : "offen")}, ggf. widerrufen).",
                        "Vollmacht mit neuem Nachweis an der Person erfassen.")
                    : new(Buchungscode.VollmachtFehlt, $"Für {person.Anzeigename} liegt keine Buchungsvollmacht dieser buchenden Person oder Organisation vor.",
                        "Vollmacht mit Nachweis an der Person erfassen oder die Person selbst buchen lassen.");
            }
        }

        if (kurs.Sichtbarkeit == Sichtbarkeit.Geschlossen && kurs.AuftraggeberId is { } auftraggeber)
        {
            var dazu = person.Organisationen.AlleAmStichtag(heute, jetzt).Any(o => o.OrganisationId == auftraggeber)
                       || person.Vollmachten.AlleAmStichtag(heute, jetzt).Any(v => v.BevollmaechtigterId == auftraggeber);
            if (!dazu)
            {
                return new(Buchungscode.GeschlossenZielgruppe,
                    $"Geschlossener Kurs für {kurs.AuftraggeberName}. {person.Anzeigename} gehört laut Stammdaten nicht dazu.",
                    "Zugehörigkeit zur Organisation an der Person eintragen, wenn das zutrifft.");
            }
        }

        switch (lage.Bestehend)
        {
            case Belegungsstatus.Angemeldet:
                return new(Buchungscode.BereitsAngemeldet, $"{person.Anzeigename} ist in diesem Kurs bereits angemeldet.");
            case Belegungsstatus.Nachrueckangebot:
                return new(Buchungscode.BereitsAngemeldet, $"Für {person.Anzeigename} liegt bereits ein Nachrückangebot vor.", "Das Angebot auf der Belegung annehmen.");
            case Belegungsstatus.Warteliste:
                return new(Buchungscode.BereitsWartend, $"{person.Anzeigename} steht bereits auf der Warteliste.");
        }

        // Kontingentkurse: die Kapazität wird je Veranstaltung bei der Vorab-Einbuchung geprüft.
        if (r.Belegung != Belegungsart.Kontingent && lage.Belegt >= kurs.MaxTeilnehmende)
        {
            var ueberbuchbar = r.DarfUeberbuchen(weg);
            return r.Warteliste
                ? new(Buchungscode.Warteliste, $"Ausgebucht ({lage.Belegt} von {kurs.MaxTeilnehmende}): Anmeldung auf die Warteliste (Platz {lage.Wartend + 1}).",
                    ueberbuchbar ? "Ihr Buchungsweg darf überbuchen." : null, ueberbuchbar, vollmachtId)
                : new(Buchungscode.Ausgebucht, $"Ausgebucht ({lage.Belegt} von {kurs.MaxTeilnehmende}); dieser Kurs führt keine Warteliste.",
                    ueberbuchbar ? "Ihr Buchungsweg darf überbuchen." : null, ueberbuchbar, vollmachtId);
        }

        return new(Buchungscode.Buchbar, VollmachtId: vollmachtId);
    }

    /// <summary>Storno nach den AGB-Fristen der Richtlinie: rechtzeitig nur Bearbeitungsgebühr, danach bleibt das Entgelt fällig.</summary>
    public static Stornoregel Storno(Belegung belegung, Kurs kurs, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(belegung);
        ArgumentNullException.ThrowIfNull(kurs);
        if (belegung.Status is Belegungsstatus.Warteliste or Belegungsstatus.Nachrueckangebot)
        {
            return new(0, "Abmeldung von der Warteliste ist kostenlos.");
        }

        if (kurs.Status == Kursstatus.Abgesagt)
        {
            return new(0, "Der Kurs wurde von der vhs abgesagt: kostenlos, gezahltes Entgelt wird erstattet.");
        }

        var r = kurs.Richtlinie;
        if (kurs.ErsterTermin is not { } beginn)
        {
            return new(r.BearbeitungsgebuehrCent, "Noch keine Termine: Abmeldung zur Bearbeitungsgebühr.");
        }

        var frist = beginn.Date.AddDays(-r.StornofristTage);
        if (belegung.IstKontingent)
        {
            var stand = belegung.Kontingent;
            return stand.Besucht + stand.Verfallen == 0
                ? new(r.BearbeitungsgebuehrCent, "Kontingent noch nicht genutzt: Abmeldung zur Bearbeitungsgebühr.")
                : new(belegung.EntgeltCent, $"Kontingent bereits genutzt ({stand.Besucht} besucht, {stand.Verfallen} verfallen): das Entgelt bleibt fällig.");
        }

        return jetzt.LocalDateTime.Date <= frist
            ? new(r.BearbeitungsgebuehrCent, r.BearbeitungsgebuehrCent == 0
                ? $"Rechtzeitig (bis {frist:dd.MM.yyyy}, {r.StornofristTage} Tage vor Beginn): kostenlos."
                : $"Rechtzeitig (bis {frist:dd.MM.yyyy}): Bearbeitungsgebühr {new Geld(r.BearbeitungsgebuehrCent)}.")
            : new(belegung.EntgeltCent, $"Nach der Abmeldefrist ({frist:dd.MM.yyyy}, {r.StornofristTage} Tage vor Beginn): das Entgelt von {belegung.Entgelt} bleibt fällig (AGB).");
    }
}

public sealed record Stornoregel(long GebuehrCent, string Erklaerung)
{
    public Geld Gebuehr => new(GebuehrCent);
}
