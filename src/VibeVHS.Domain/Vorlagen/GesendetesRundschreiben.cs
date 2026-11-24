using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Vorlagen;

/// <summary>
/// Archiv gesendeter Rundschreiben (QA-66, aus SchoolCraft FA-824): Betreff, bereinigter Text mit Platzhaltern, Gestaltung, Anredestil,
/// Zweck und die Zielgruppe in lesbarer Form – zum Nachsehen und als Vorlage für ein neues Rundschreiben. Die Empfänger selbst stehen
/// im Postausgang (je Mail), nicht hier.
/// </summary>
public sealed class GesendetesRundschreiben
{
    private GesendetesRundschreiben()
    {
        Betreff = Html = Zielgruppe = GesendetVon = string.Empty;
    }

    public GesendetesRundschreiben(string betreff, string html, Gestaltung gestaltung, Anredestil anredestil, Kontaktzweck zweck, string zielgruppe,
        int eingereiht, int uebersprungen, DateTimeOffset am, string von)
    {
        Id = Ids.Neu(am.ToUnixTimeMilliseconds());
        Betreff = betreff.Trim();
        Html = html;
        Gestaltung = gestaltung;
        Anredestil = anredestil;
        Zweck = zweck;
        Zielgruppe = zielgruppe;
        Eingereiht = eingereiht;
        Uebersprungen = uebersprungen;
        GesendetAm = am;
        GesendetVon = von;
    }

    public Guid Id { get; private set; }

    public string Betreff { get; private set; }

    /// <summary>Bereinigtes HTML mit Platzhaltern ({Anrede} …), ohne Rahmen der Gestaltung.</summary>
    public string Html { get; private set; }

    public Gestaltung Gestaltung { get; private set; }

    public Anredestil Anredestil { get; private set; }

    public Kontaktzweck Zweck { get; private set; }

    /// <summary>Zielgruppe zum Zeitpunkt des Versands, lesbar (z. B. „Teilnehmende · FB 5“).</summary>
    public string Zielgruppe { get; private set; }

    public int Eingereiht { get; private set; }

    public int Uebersprungen { get; private set; }

    public DateTimeOffset GesendetAm { get; private set; }

    public string GesendetVon { get; private set; }
}
