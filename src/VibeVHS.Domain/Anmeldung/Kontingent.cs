using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Anmeldung;

public enum Kontingentereignisart
{
    /// <summary>Kontingent gekauft (Anzahl Einheiten).</summary>
    Erworben,

    /// <summary>Vorab in eine Veranstaltung eingebucht (Einheit reserviert).</summary>
    Eingebucht,

    /// <summary>Einbuchung zurückgenommen (Einheit wieder frei).</summary>
    Ausgetragen,

    /// <summary>Teilgenommen (Einheit verbraucht).</summary>
    Besucht,

    /// <summary>Eingebucht, aber nicht erschienen; nach Kursregel verbraucht.</summary>
    NichtErschienen,

    /// <summary>Eingebucht, nicht erschienen; nach Kursregel nicht angerechnet.</summary>
    NichtErschienenOhneAnrechnung,

    /// <summary>Gültigkeit abgelaufen; der Rest verfällt (Anzahl).</summary>
    Abgelaufen,

    /// <summary>Auf die Warteliste einer vollen Veranstaltung gesetzt.</summary>
    WartelisteEingetragen,

    WartelisteVerlassen,
}

/// <summary>Unveränderliches Ereignis eines Kontingents. Der Kontingentstand ist immer die Summe dieser Ereignisse.</summary>
public sealed class KontingentEreignis
{
    private KontingentEreignis()
    {
        Von = string.Empty;
    }

    internal KontingentEreignis(Guid belegungId, int laufnummer, Kontingentereignisart art, Guid? veranstaltungId, Guid? ortId, int anzahl,
        DateTimeOffset zeitpunkt, string von, string? bemerkung)
    {
        Id = Ids.Neu();
        BelegungId = belegungId;
        Laufnummer = laufnummer;
        Art = art;
        VeranstaltungId = veranstaltungId;
        OrtId = ortId;
        Anzahl = anzahl;
        Zeitpunkt = zeitpunkt;
        Von = von;
        Bemerkung = bemerkung;
    }

    public Guid Id { get; private set; }

    public Guid BelegungId { get; private set; }

    /// <summary>Reihenfolge innerhalb der Belegung.</summary>
    public int Laufnummer { get; private set; }

    public Kontingentereignisart Art { get; private set; }

    public Guid? VeranstaltungId { get; private set; }

    public Guid? OrtId { get; private set; }

    public int Anzahl { get; private set; }

    public DateTimeOffset Zeitpunkt { get; private set; }

    public string Von { get; private set; }

    public string? Bemerkung { get; private set; }

    public static string ArtAnzeige(Kontingentereignisart a) => a switch
    {
        Kontingentereignisart.Erworben => "Kontingent erworben",
        Kontingentereignisart.Eingebucht => "eingebucht",
        Kontingentereignisart.Ausgetragen => "ausgetragen",
        Kontingentereignisart.Besucht => "besucht",
        Kontingentereignisart.NichtErschienen => "nicht erschienen (zählt)",
        Kontingentereignisart.NichtErschienenOhneAnrechnung => "nicht erschienen (zählt nicht)",
        Kontingentereignisart.Abgelaufen => "Rest verfallen",
        Kontingentereignisart.WartelisteEingetragen => "Warteliste der Veranstaltung",
        _ => "Warteliste verlassen",
    };
}

/// <summary>Stand eines Kontingents: gekauft, eingebucht (reserviert), besucht, verfallen, verbleibend.</summary>
public sealed record Kontingentstand(int Anzahl, int Eingebucht, int Besucht, int Verfallen, DateOnly? GueltigBis)
{
    public int Verbleibend => Anzahl - Eingebucht - Besucht - Verfallen;
}

/// <summary>Berechnet Kontingentstand und offene Einbuchungen ausschließlich aus den Ereignissen.</summary>
public static class Kontingentrechnung
{
    public static Kontingentstand Stand(int anzahl, DateOnly? gueltigBis, IEnumerable<KontingentEreignis> ereignisse)
    {
        var erworben = 0;
        var besucht = 0;
        var verfallen = 0;
        var eingebucht = new HashSet<Guid>();
        foreach (var e in Geordnet(ereignisse))
        {
            switch (e.Art)
            {
                case Kontingentereignisart.Erworben:
                    erworben += e.Anzahl;
                    break;
                case Kontingentereignisart.Eingebucht:
                    eingebucht.Add(e.VeranstaltungId!.Value);
                    break;
                case Kontingentereignisart.Ausgetragen:
                case Kontingentereignisart.NichtErschienenOhneAnrechnung:
                    eingebucht.Remove(e.VeranstaltungId!.Value);
                    break;
                case Kontingentereignisart.Besucht:
                    eingebucht.Remove(e.VeranstaltungId!.Value);
                    besucht++;
                    break;
                case Kontingentereignisart.NichtErschienen:
                    eingebucht.Remove(e.VeranstaltungId!.Value);
                    verfallen++;
                    break;
                case Kontingentereignisart.Abgelaufen:
                    verfallen += e.Anzahl;
                    break;
            }
        }

        return new Kontingentstand(erworben > 0 ? erworben : anzahl, eingebucht.Count, besucht, verfallen, gueltigBis);
    }

    /// <summary>Aktuell eingebuchte Veranstaltungen mit gewähltem Ort.</summary>
    public static IReadOnlyDictionary<Guid, Guid?> Eingebucht(IEnumerable<KontingentEreignis> ereignisse)
    {
        var ergebnis = new Dictionary<Guid, Guid?>();
        foreach (var e in Geordnet(ereignisse).Where(e => e.VeranstaltungId is not null))
        {
            var vid = e.VeranstaltungId!.Value;
            switch (e.Art)
            {
                case Kontingentereignisart.Eingebucht:
                    ergebnis[vid] = e.OrtId;
                    break;
                case Kontingentereignisart.Ausgetragen or Kontingentereignisart.Besucht or Kontingentereignisart.NichtErschienen
                    or Kontingentereignisart.NichtErschienenOhneAnrechnung:
                    ergebnis.Remove(vid);
                    break;
            }
        }

        return ergebnis;
    }

    /// <summary>Veranstaltungen, auf deren Warteliste die Belegung steht.</summary>
    public static IReadOnlyDictionary<Guid, (Guid? Ort, DateTimeOffset Seit)> Wartend(IEnumerable<KontingentEreignis> ereignisse)
    {
        var ergebnis = new Dictionary<Guid, (Guid?, DateTimeOffset)>();
        foreach (var e in Geordnet(ereignisse).Where(e => e.VeranstaltungId is not null))
        {
            var vid = e.VeranstaltungId!.Value;
            switch (e.Art)
            {
                case Kontingentereignisart.WartelisteEingetragen:
                    ergebnis[vid] = (e.OrtId, e.Zeitpunkt);
                    break;
                case Kontingentereignisart.WartelisteVerlassen or Kontingentereignisart.Eingebucht:
                    ergebnis.Remove(vid);
                    break;
            }
        }

        return ergebnis;
    }

    private static IEnumerable<KontingentEreignis> Geordnet(IEnumerable<KontingentEreignis> ereignisse)
        => ereignisse.OrderBy(e => e.Laufnummer);
}

/// <summary>Ermäßigung einer Person mit Nachweis und Gültigkeitszeitraum (z. B. Bürgergeld, Schüler/Studierende, Ehrenamtskarte).</summary>
public sealed class Ermaessigung
{
    private Ermaessigung()
    {
        Art = string.Empty;
        Nachweis = string.Empty;
        ErfasstVon = string.Empty;
    }

    public Ermaessigung(Guid personId, string art, string nachweis, DateOnly gueltigVon, DateOnly gueltigBis, DateTimeOffset erfasstAm, string erfasstVon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(art);
        ArgumentException.ThrowIfNullOrWhiteSpace(nachweis);
        if (gueltigBis < gueltigVon)
        {
            throw new ArgumentException("Das Ende der Gültigkeit liegt vor dem Beginn.");
        }

        Id = Ids.Neu();
        PersonId = personId;
        Art = art.Trim();
        Nachweis = nachweis.Trim();
        GueltigVon = gueltigVon;
        GueltigBis = gueltigBis;
        ErfasstAm = erfasstAm;
        ErfasstVon = erfasstVon;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public string Art { get; private set; }

    public string Nachweis { get; private set; }

    public DateOnly GueltigVon { get; private set; }

    /// <summary>Letzter gültiger Tag (einschließlich).</summary>
    public DateOnly GueltigBis { get; private set; }

    public DateTimeOffset ErfasstAm { get; private set; }

    public string ErfasstVon { get; private set; }

    public bool GiltAm(DateOnly tag) => GueltigVon <= tag && tag <= GueltigBis;

    public void Anonymisieren() => Nachweis = "–";

    public static IReadOnlyList<string> Arten { get; } =
        ["Bürgergeld / Grundsicherung", "Schüler/in, Auszubildende, Studierende", "Schwerbehinderung (ab GdB 50)", "Ehrenamtskarte NRW", "Familienpass", "Sonstige"];
}
