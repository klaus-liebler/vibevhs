using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Durchfuehrung;

/// <summary>Wie eine Unterrichts-App eingebunden ist; daraus ergibt sich, ob sie eingebaut oder angebunden ist (nicht gespeichert).</summary>
public enum Einbindung
{
    /// <summary>Podium (Live-Quiz) auf dem eigenen Server: Teilnahme mit Spitzname per QR/PIN, keine Daten an Dritte.</summary>
    Podium,

    /// <summary>Weitere selbst gehostete Anwendung der vhs (z. B. H5P, Zeichenfläche, Etherpad).</summary>
    SelbstGehostet,

    /// <summary>Externer Anbieter per Link; braucht eine Freigabe der vhs (Auftragsverarbeitung, Datenschutz).</summary>
    Link,
}

/// <summary>
/// Unterrichts-App im Katalog der vhs (QA-69, aus SchoolCraft FK-15): Quiz und Abstimmung, Übungen, Zeichenfläche, Lern-Apps. Eingebaute
/// Apps laufen auf dem Server der vhs (keine Daten an Dritte) und sind sofort nutzbar; angebundene brauchen eine Freigabe. Keine App hat
/// einen eigenen Navigationspunkt – alle stehen gleichrangig im Katalog.
/// </summary>
public sealed class Unterrichtsapp
{
    private Unterrichtsapp()
    {
        Name = Adresse = Beschreibung = Datenhinweis = string.Empty;
    }

    public Unterrichtsapp(string name, Einbindung einbindung, string adresse, string beschreibung, string datenhinweis, DateTimeOffset angelegtAm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Uri.TryCreate(adresse, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Bitte eine vollständige Adresse mit https:// angeben.", nameof(adresse));
        }

        Id = Ids.Neu();
        Name = name.Trim();
        Einbindung = einbindung;
        Adresse = adresse.Trim();
        Beschreibung = beschreibung.Trim();
        Datenhinweis = datenhinweis.Trim();
        AngelegtAm = angelegtAm;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Einbindung Einbindung { get; private set; }

    public string Adresse { get; private set; }

    public string Beschreibung { get; private set; }

    /// <summary>Wohin gehen Daten? (z. B. „bleibt auf dem Server der vhs, Spitzname statt Name“).</summary>
    public string Datenhinweis { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Freigabe der vhs für angebundene Apps (Vertrag, Datenschutz geprüft).</summary>
    public DateTimeOffset? FreigegebenAm { get; private set; }

    public string? FreigegebenVon { get; private set; }

    public bool Eingebaut => Einbindung is Einbindung.Podium or Einbindung.SelbstGehostet;

    /// <summary>Eingebaute Apps sind sofort nutzbar, angebundene erst nach Freigabe.</summary>
    public bool Nutzbar => Eingebaut || FreigegebenAm is not null;

    public void Freigeben(DateTimeOffset am, string von)
    {
        FreigegebenAm = am;
        FreigegebenVon = von;
    }

    public void FreigabeZuruecknehmen()
    {
        FreigegebenAm = null;
        FreigegebenVon = null;
    }
}

/// <summary>Einsatz einer App in einem Kurs durch die Kursleitung (Titel, Auftrag); Teilnehmende sehen ihn im Portal.</summary>
public sealed class Appeinsatz
{
    private Appeinsatz()
    {
        Titel = string.Empty;
    }

    public Appeinsatz(Guid appId, Guid kursId, Guid personId, string titel, string? auftrag, DateTimeOffset am)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titel);
        Id = Ids.Neu();
        AppId = appId;
        KursId = kursId;
        PersonId = personId;
        Titel = titel.Trim();
        Auftrag = string.IsNullOrWhiteSpace(auftrag) ? null : auftrag.Trim();
        AngelegtAm = am;
    }

    public Guid Id { get; private set; }

    public Guid AppId { get; private set; }

    public Guid KursId { get; private set; }

    /// <summary>Kursleitung, die die App eingesetzt hat.</summary>
    public Guid PersonId { get; private set; }

    public string Titel { get; private set; }

    public string? Auftrag { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    public DateTimeOffset? BeendetAm { get; private set; }

    public bool Aktiv => BeendetAm is null;

    public void Beenden(DateTimeOffset am) => BeendetAm ??= am;
}
