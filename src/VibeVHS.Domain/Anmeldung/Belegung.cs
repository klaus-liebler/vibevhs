using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Domain.Anmeldung;

public enum Belegungsstatus
{
    /// <summary>Hat einen Platz.</summary>
    Angemeldet,

    /// <summary>Wartet auf einen Platz (Reihenfolge nach <see cref="Belegung.WartelisteSeit"/>).</summary>
    Warteliste,

    /// <summary>Ein Platz ist frei geworden und reserviert, bis <see cref="Belegung.AngebotBis"/>.</summary>
    Nachrueckangebot,

    Storniert,

    /// <summary>Auf einen anderen Kurs umgebucht (<see cref="Belegung.UmgebuchtNachId"/>).</summary>
    Umgebucht,

    /// <summary>Nachrückangebot abgelehnt oder Frist verstrichen.</summary>
    AngebotVerfallen,
}

public enum Zahlart
{
    Lastschrift,
    Ueberweisung,
    Bar,
    Kostentraeger,
}

public static class BelegungAnzeige
{
    public static string Status(Belegungsstatus s) => s switch
    {
        Belegungsstatus.Angemeldet => "angemeldet",
        Belegungsstatus.Warteliste => "Warteliste",
        Belegungsstatus.Nachrueckangebot => "Nachrückangebot",
        Belegungsstatus.Storniert => "storniert",
        Belegungsstatus.Umgebucht => "umgebucht",
        _ => "Angebot verfallen",
    };

    public static string Zahlart(Zahlart z) => z switch
    {
        Anmeldung.Zahlart.Lastschrift => "SEPA-Lastschrift",
        Anmeldung.Zahlart.Ueberweisung => "Überweisung",
        Anmeldung.Zahlart.Bar => "Barzahlung am Tresen",
        _ => "Kostenträger",
    };

    public static string Weg(Buchungsweg w) => w switch
    {
        Buchungsweg.Tresen => "Tresen",
        Buchungsweg.IntegrationskursTeam => "Integrationskurs-Team",
        Buchungsweg.Online => "Online (selbst)",
        _ => "Auftraggeber",
    };
}

/// <summary>
/// Belegung: eine Person in einem Kurs. Hält fest, wer für wen über welchen Weg und mit welcher Vollmacht gebucht hat,
/// das Entgelt zum Buchungszeitpunkt (eingefroren) und – bei Kontingenten – die unveränderlichen Kontingentereignisse.
/// </summary>
public sealed class Belegung
{
    private readonly List<KontingentEreignis> _ereignisse = [];

    private Belegung()
    {
        Kursnummer = string.Empty;
        GebuchtVon = string.Empty;
    }

    private Belegung(Kurs kurs, Guid personId, Belegungsstatus status, Buchungsweg weg, Buchender buchender, long entgeltCent,
        Ermaessigung? ermaessigung, Zahlart zahlart, DateTimeOffset jetzt)
    {
        Id = Ids.Neu();
        KursId = kurs.Id;
        Kursnummer = kurs.Kursnummer;
        PersonId = personId;
        Status = status;
        Weg = weg;
        GebuchtAm = jetzt;
        GebuchtVon = buchender.Name;
        GebuchtVonKennung = buchender.Kennung;
        GebuchtInRolle = buchender.Rolle;
        BevollmaechtigterId = buchender.BevollmaechtigterId;
        BevollmaechtigterName = buchender.BevollmaechtigterName;
        VollmachtId = buchender.VollmachtId;
        EntgeltCent = entgeltCent;
        Ermaessigt = ermaessigung is not null;
        ErmaessigungNachweis = ermaessigung is null ? null : $"{ermaessigung.Art}: {ermaessigung.Nachweis} (gültig bis {ermaessigung.GueltigBis:dd.MM.yyyy})";
        Zahlart = zahlart;
        Belegungsart = kurs.Richtlinie.Belegung;
        if (status == Belegungsstatus.Warteliste)
        {
            WartelisteSeit = jetzt;
        }
    }

    /// <summary>Neue Anmeldung. Bei vollem Kurs (ohne Überbuchung) landet die Person auf der Warteliste.</summary>
    public static Belegung Anlegen(Kurs kurs, Guid personId, bool aufWarteliste, Buchungsweg weg, Buchender buchender,
        Ermaessigung? ermaessigung, Zahlart zahlart, DateTimeOffset jetzt, DateTimeOffset? entgeltBekanntAm = null)
    {
        ArgumentNullException.ThrowIfNull(kurs);
        ArgumentNullException.ThrowIfNull(buchender);
        var entgelt = kurs.EntgeltFuerBuchung(jetzt, entgeltBekanntAm);
        var ermaessigt = ermaessigung is not null && entgelt?.ErmaessigtCent is not null;
        var cent = entgelt is null ? 0 : ermaessigt ? entgelt.ErmaessigtCent!.Value : entgelt.Cent;
        var b = new Belegung(kurs, personId, aufWarteliste ? Belegungsstatus.Warteliste : Belegungsstatus.Angemeldet, weg, buchender, cent,
            ermaessigt ? ermaessigung : null, zahlart, jetzt);
        if (!aufWarteliste)
        {
            b.KontingentEroeffnen(kurs.Richtlinie, jetzt, buchender.Name);
        }

        return b;
    }

    public Guid Id { get; private set; }

    public Guid KursId { get; private set; }

    /// <summary>Kursnummer zum Buchungszeitpunkt (für Listen und Protokoll).</summary>
    public string Kursnummer { get; private set; }

    /// <summary>Für wen gebucht wurde.</summary>
    public Guid PersonId { get; private set; }

    public Belegungsstatus Status { get; private set; }

    /// <summary>Über welchen Weg (Tresen, Integrationskurs-Team, online, Auftraggeber).</summary>
    public Buchungsweg Weg { get; private set; }

    public DateTimeOffset GebuchtAm { get; private set; }

    /// <summary>Wer gebucht hat (Mitarbeitende mit Rolle oder die Person selbst).</summary>
    public string GebuchtVon { get; private set; }

    public string? GebuchtVonKennung { get; private set; }

    public string? GebuchtInRolle { get; private set; }

    /// <summary>Bei Buchung im Auftrag: die bevollmächtigte Person oder Organisation.</summary>
    public Guid? BevollmaechtigterId { get; private set; }

    public string? BevollmaechtigterName { get; private set; }

    /// <summary>Die Vollmacht, auf deren Grundlage gebucht wurde.</summary>
    public Guid? VollmachtId { get; private set; }

    /// <summary>Entgelt zum Buchungszeitpunkt, eingefroren (spätere Preisänderungen betreffen diese Belegung nicht).</summary>
    public long EntgeltCent { get; private set; }

    public Geld Entgelt => new(EntgeltCent);

    public bool Ermaessigt { get; private set; }

    /// <summary>Art, Nachweis und Gültigkeit der Ermäßigung zum Buchungszeitpunkt.</summary>
    public string? ErmaessigungNachweis { get; private set; }

    public Zahlart Zahlart { get; private set; }

    public Belegungsart Belegungsart { get; private set; }

    public DateTimeOffset? WartelisteSeit { get; private set; }

    public DateTimeOffset? AngebotBis { get; private set; }

    public DateTimeOffset? StorniertAm { get; private set; }

    public string? StorniertVon { get; private set; }

    public string? StornoGrund { get; private set; }

    /// <summary>Bei Storno oder Umbuchung fällige Gebühr (bei Abmeldung nach Frist: das volle Entgelt).</summary>
    public long? GebuehrCent { get; private set; }

    public Guid? UmgebuchtVonId { get; private set; }

    public Guid? UmgebuchtNachId { get; private set; }

    public int? KontingentAnzahl { get; private set; }

    /// <summary>Wann die Erinnerung vor Kursbeginn verschickt wurde (nur einmal je Belegung).</summary>
    public DateTimeOffset? ErinnertAm { get; private set; }

    public DateOnly? KontingentGueltigBis { get; private set; }

    public IReadOnlyCollection<KontingentEreignis> Ereignisse => _ereignisse;

    /// <summary>Hält einen Platz im Kurs (angemeldet oder mit offenem Nachrückangebot).</summary>
    public bool HaeltPlatz => Status is Belegungsstatus.Angemeldet or Belegungsstatus.Nachrueckangebot;

    /// <summary>Noch im Kurs oder auf dem Weg hinein (angemeldet, wartend, Angebot offen).</summary>
    public bool Offen => Status is Belegungsstatus.Angemeldet or Belegungsstatus.Warteliste or Belegungsstatus.Nachrueckangebot;

    public bool IstKontingent => Belegungsart == Belegungsart.Kontingent;

    public Kontingentstand Kontingent => Kontingentrechnung.Stand(KontingentAnzahl ?? 0, KontingentGueltigBis, _ereignisse);

    internal void AlsUmbuchungVon(Guid alteBelegungId) => UmgebuchtVonId = alteBelegungId;

    public void NachrueckenAnbieten(DateTimeOffset bis)
    {
        if (Status != Belegungsstatus.Warteliste)
        {
            throw new InvalidOperationException("Nur wer auf der Warteliste steht, kann ein Nachrückangebot bekommen.");
        }

        Status = Belegungsstatus.Nachrueckangebot;
        AngebotBis = bis;
    }

    public void AngebotAnnehmen(Buchungsrichtlinie richtlinie, DateTimeOffset jetzt, string von)
    {
        if (Status != Belegungsstatus.Nachrueckangebot)
        {
            throw new InvalidOperationException("Es liegt kein offenes Nachrückangebot vor.");
        }

        if (AngebotBis < jetzt)
        {
            throw new InvalidOperationException($"Die Annahmefrist ist am {AngebotBis.Value.ToLocalTime():dd.MM.yyyy HH:mm} abgelaufen.");
        }

        Status = Belegungsstatus.Angemeldet;
        AngebotBis = null;
        KontingentEroeffnen(richtlinie, jetzt, von);
    }

    /// <summary>Angebot abgelehnt oder Frist verstrichen: Der Platz geht an die nächste Person auf der Warteliste.</summary>
    public void AngebotVerfallenLassen(DateTimeOffset jetzt, string grund)
    {
        if (Status != Belegungsstatus.Nachrueckangebot)
        {
            throw new InvalidOperationException("Es liegt kein offenes Nachrückangebot vor.");
        }

        Status = Belegungsstatus.AngebotVerfallen;
        StorniertAm = jetzt;
        StornoGrund = grund;
    }

    /// <summary>Überbuchung aus der Warteliste heraus (nur mit Recht zum Überbuchen) – direkt angemeldet.</summary>
    public void AusWartelisteAnmelden(Buchungsrichtlinie richtlinie, DateTimeOffset jetzt, string von)
    {
        if (Status is not (Belegungsstatus.Warteliste or Belegungsstatus.Nachrueckangebot))
        {
            throw new InvalidOperationException("Nur wartende Personen können so angemeldet werden.");
        }

        Status = Belegungsstatus.Angemeldet;
        AngebotBis = null;
        KontingentEroeffnen(richtlinie, jetzt, von);
    }

    public void Erinnern(DateTimeOffset jetzt) => ErinnertAm ??= jetzt;

    public void Stornieren(long gebuehrCent, string grund, string von, DateTimeOffset jetzt)
    {
        if (!Offen)
        {
            throw new InvalidOperationException($"Die Belegung ist bereits {BelegungAnzeige.Status(Status)}.");
        }

        Status = Belegungsstatus.Storniert;
        StorniertAm = jetzt;
        StorniertVon = von;
        StornoGrund = grund;
        GebuehrCent = gebuehrCent;
        AngebotBis = null;
        EinbuchungenAufheben(jetzt, von, "Abmeldung vom Kurs");
    }

    public void Umbuchen(Belegung neu, long gebuehrCent, string von, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(neu);
        if (!Offen)
        {
            throw new InvalidOperationException($"Die Belegung ist bereits {BelegungAnzeige.Status(Status)}.");
        }

        Status = Belegungsstatus.Umgebucht;
        UmgebuchtNachId = neu.Id;
        StorniertAm = jetzt;
        StorniertVon = von;
        StornoGrund = $"Umgebucht auf {neu.Kursnummer}";
        GebuehrCent = gebuehrCent;
        neu.AlsUmbuchungVon(Id);
        EinbuchungenAufheben(jetzt, von, "Umbuchung");
    }

    // ---------- Kontingent und Vorab-Einbuchung ----------

    private void KontingentEroeffnen(Buchungsrichtlinie richtlinie, DateTimeOffset jetzt, string von)
    {
        if (richtlinie.Belegung != Belegungsart.Kontingent || KontingentAnzahl is not null)
        {
            return;
        }

        KontingentAnzahl = richtlinie.KontingentAnzahl ?? 10;
        KontingentGueltigBis = richtlinie.KontingentGueltigMonate is { } m ? DateOnly.FromDateTime(jetzt.Date).AddMonths(m) : null;
        Anfuegen(Kontingentereignisart.Erworben, null, null, KontingentAnzahl.Value, jetzt, von, $"{KontingentAnzahl}er-Kontingent erworben");
    }

    /// <summary>Veranstaltungen, in die diese Belegung gerade eingebucht ist (Veranstaltung → Ort).</summary>
    public IReadOnlyDictionary<Guid, Guid?> Einbuchungen => Kontingentrechnung.Eingebucht(_ereignisse);

    /// <summary>Veranstaltungen, für die diese Belegung auf der Warteliste steht (Veranstaltung → Ort und Zeitpunkt).</summary>
    public IReadOnlyDictionary<Guid, (Guid? Ort, DateTimeOffset Seit)> WartetAuf => Kontingentrechnung.Wartend(_ereignisse);

    /// <summary>
    /// Vorab-Einbuchung in eine Veranstaltung. Prüft Status, Frist, Kontingent und die Kapazität am gewählten Ort;
    /// bei vollem Ort steht die Person auf der Warteliste dieser Veranstaltung.
    /// </summary>
    public Einbuchungsergebnis Einbuchen(Veranstaltung v, Guid ortId, int belegtAmOrt, int kapazitaet, Buchungsrichtlinie richtlinie,
        DateTimeOffset jetzt, string von)
    {
        var pruefung = EinbuchungPruefen(v, ortId, belegtAmOrt, kapazitaet, richtlinie, jetzt);
        if (pruefung.Code == Einbuchungscode.Eingebucht)
        {
            Anfuegen(Kontingentereignisart.Eingebucht, v.Id, ortId, 1, jetzt, von, null);
        }
        else if (pruefung.Code == Einbuchungscode.Warteliste)
        {
            Anfuegen(Kontingentereignisart.WartelisteEingetragen, v.Id, ortId, 0, jetzt, von, null);
        }

        return pruefung;
    }

    /// <summary>Wie <see cref="Einbuchen"/>, aber ohne etwas zu ändern (für Buttons mit Begründung).</summary>
    public Einbuchungsergebnis EinbuchungPruefen(Veranstaltung v, Guid ortId, int belegtAmOrt, int kapazitaet, Buchungsrichtlinie richtlinie,
        DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(richtlinie);
        if (Status != Belegungsstatus.Angemeldet)
        {
            return new(Einbuchungscode.NichtAngemeldet, $"Einbuchen geht nur mit einer bestätigten Anmeldung (Status: {BelegungAnzeige.Status(Status)}).");
        }

        if (v.Ausgefallen)
        {
            return new(Einbuchungscode.Ausgefallen, "Diese Veranstaltung fällt aus.");
        }

        if (Einbuchungen.ContainsKey(v.Id))
        {
            return new(Einbuchungscode.BereitsEingebucht, "Für diese Veranstaltung bereits eingebucht.");
        }

        var frist = v.Beginn.AddHours(-richtlinie.EinbuchungsfristStunden);
        if (jetzt.LocalDateTime > frist)
        {
            return new(Einbuchungscode.FristAbgelaufen,
                $"Einbuchen ist bis {richtlinie.EinbuchungsfristStunden} Stunden vor Beginn möglich (bis {frist:dd.MM. HH:mm} Uhr).");
        }

        if (IstKontingent)
        {
            var stand = Kontingent;
            if (stand.GueltigBis is { } bis && v.Datum > bis)
            {
                return new(Einbuchungscode.KontingentAbgelaufen, $"Das Kontingent gilt nur bis {bis:dd.MM.yyyy}. Bitte ein neues Kontingent buchen.");
            }

            if (stand.Verbleibend <= 0)
            {
                return new(Einbuchungscode.KontingentAufgebraucht,
                    $"Kontingent aufgebraucht: alle {stand.Anzahl} Einheiten sind besucht, verfallen oder eingebucht. Neues Kontingent buchen oder eine Einbuchung austragen.");
            }
        }

        if (belegtAmOrt >= kapazitaet)
        {
            return WartetAuf.ContainsKey(v.Id)
                ? new(Einbuchungscode.BereitsWartend, "Steht für diese Veranstaltung schon auf der Warteliste.")
                : new(Einbuchungscode.Warteliste, $"Ausgebucht: {belegtAmOrt} von {kapazitaet} Plätzen sind reserviert. Eintrag auf die Warteliste dieser Veranstaltung.");
        }

        return new(Einbuchungscode.Eingebucht, null);
    }

    /// <summary>Austragen aus einer Veranstaltung (mit Frist); die Einheit geht zurück ins Kontingent.</summary>
    public void Austragen(Guid veranstaltungId, DateTime beginn, Buchungsrichtlinie richtlinie, DateTimeOffset jetzt, string von, bool fristIgnorieren = false)
    {
        ArgumentNullException.ThrowIfNull(richtlinie);
        if (WartetAuf.ContainsKey(veranstaltungId))
        {
            Anfuegen(Kontingentereignisart.WartelisteVerlassen, veranstaltungId, null, 0, jetzt, von, null);
            return;
        }

        if (!Einbuchungen.ContainsKey(veranstaltungId))
        {
            throw new InvalidOperationException("Für diese Veranstaltung besteht keine Einbuchung.");
        }

        var frist = beginn.AddHours(-richtlinie.AustragefristStunden);
        if (!fristIgnorieren && jetzt.LocalDateTime > frist)
        {
            throw new InvalidOperationException(
                $"Austragen ist nur bis {richtlinie.AustragefristStunden} Stunden vor Beginn möglich (bis {frist:dd.MM. HH:mm} Uhr). Danach gilt die Regel für Nichterscheinen.");
        }

        Anfuegen(Kontingentereignisart.Ausgetragen, veranstaltungId, null, 1, jetzt, von, null);
    }

    /// <summary>Anwesenheit einer eingebuchten Veranstaltung festhalten (bis zur QR-Anwesenheit in I4 von Hand).</summary>
    public void AnwesenheitFesthalten(Guid veranstaltungId, bool erschienen, Buchungsrichtlinie richtlinie, DateTimeOffset jetzt, string von)
    {
        ArgumentNullException.ThrowIfNull(richtlinie);
        var art = erschienen ? Kontingentereignisart.Besucht
            : richtlinie.NichterscheinenZaehlt ? Kontingentereignisart.NichtErschienen : Kontingentereignisart.NichtErschienenOhneAnrechnung;
        if (!erschienen && !Einbuchungen.ContainsKey(veranstaltungId))
        {
            throw new InvalidOperationException("Nichterscheinen gibt es nur bei eingebuchten Veranstaltungen.");
        }

        if (_ereignisse.Any(e => e.VeranstaltungId == veranstaltungId && e.Art is Kontingentereignisart.Besucht or Kontingentereignisart.NichtErschienen
                or Kontingentereignisart.NichtErschienenOhneAnrechnung))
        {
            throw new InvalidOperationException("Für diese Veranstaltung ist die Anwesenheit schon festgehalten.");
        }

        if (erschienen && IstKontingent && !Einbuchungen.ContainsKey(veranstaltungId) && Kontingent.Verbleibend <= 0)
        {
            throw new InvalidOperationException("Kontingent aufgebraucht: Ohne verbleibende Einheit kann kein Besuch verbucht werden.");
        }

        Anfuegen(art, veranstaltungId, null, 1, jetzt, von, null);
    }

    /// <summary>Kontingent nach Ablauf der Gültigkeit schließen: Der Rest verfällt.</summary>
    public int KontingentAblaufenLassen(DateOnly heute, DateTimeOffset jetzt)
    {
        var stand = Kontingent;
        if (!IstKontingent || stand.GueltigBis is not { } bis || bis >= heute || stand.Verbleibend <= 0
            || _ereignisse.Any(e => e.Art == Kontingentereignisart.Abgelaufen))
        {
            return 0;
        }

        Anfuegen(Kontingentereignisart.Abgelaufen, null, null, stand.Verbleibend, jetzt, "Automatik", $"Gültigkeit bis {bis:dd.MM.yyyy} abgelaufen");
        return stand.Verbleibend;
    }

    private void EinbuchungenAufheben(DateTimeOffset jetzt, string von, string grund)
    {
        foreach (var vid in Einbuchungen.Keys.ToList())
        {
            Anfuegen(Kontingentereignisart.Ausgetragen, vid, null, 1, jetzt, von, grund);
        }

        foreach (var vid in WartetAuf.Keys.ToList())
        {
            Anfuegen(Kontingentereignisart.WartelisteVerlassen, vid, null, 0, jetzt, von, grund);
        }
    }

    private void Anfuegen(Kontingentereignisart art, Guid? veranstaltungId, Guid? ortId, int anzahl, DateTimeOffset jetzt, string von, string? bemerkung)
        => _ereignisse.Add(new KontingentEreignis(Id, _ereignisse.Count + 1, art, veranstaltungId, ortId, anzahl, jetzt, von, bemerkung));

    /// <summary>Anonymisierung der bevollmächtigten Person: Name ersetzen, Verknüpfung bleibt.</summary>
    public void BevollmaechtigtenUmbenennen(Guid id, string platzhalter)
    {
        if (BevollmaechtigterId == id)
        {
            BevollmaechtigterName = platzhalter;
        }
    }

    /// <summary>Anonymisierung der gebuchten Person: Nachweistext der Ermäßigung entfernen (das Entgelt bleibt für die Buchhaltung).</summary>
    public void PersonendatenEntfernen()
    {
        if (ErmaessigungNachweis is not null)
        {
            ErmaessigungNachweis = "–";
        }
    }
}

/// <summary>Wer bucht: Mitarbeitende (Name, Kennung, Rolle) und ggf. im Auftrag welcher bevollmächtigten Person/Organisation.</summary>
public sealed record Buchender(string Name, string? Kennung, string? Rolle, Guid? BevollmaechtigterId = null, string? BevollmaechtigterName = null,
    Guid? VollmachtId = null);

public enum Einbuchungscode
{
    Eingebucht,
    Warteliste,
    NichtAngemeldet,
    Ausgefallen,
    BereitsEingebucht,
    BereitsWartend,
    FristAbgelaufen,
    KontingentAbgelaufen,
    KontingentAufgebraucht,
}

public sealed record Einbuchungsergebnis(Einbuchungscode Code, string? Begruendung)
{
    public bool Moeglich => Code is Einbuchungscode.Eingebucht or Einbuchungscode.Warteliste;
}
