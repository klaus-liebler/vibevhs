using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Programm;

/// <summary>
/// Ein Schlüssel (bzw. Transponder) für einen Kursort (QA-67, aus SchoolCraft): Kursleitungen in Schulen, Gemeindehäusern und Außenstellen
/// bekommen Schlüssel ausgehändigt. Je Exemplar ein Eintrag mit eindeutiger Nummer; wer ihn gerade hat, steht in den Ausgaben.
/// </summary>
public sealed class Schluessel
{
    private Schluessel()
    {
        Nummer = Bezeichnung = string.Empty;
    }

    public Schluessel(string nummer, string bezeichnung, Guid ortId, DateTimeOffset angelegtAm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nummer);
        ArgumentException.ThrowIfNullOrWhiteSpace(bezeichnung);
        Id = Ids.Neu();
        Nummer = nummer.Trim();
        Bezeichnung = bezeichnung.Trim();
        OrtId = ortId;
        AngelegtAm = angelegtAm;
    }

    public Guid Id { get; private set; }

    /// <summary>Aufgedruckte bzw. eingeschlagene Nummer, eindeutig („S-12“, „Transponder 0815“).</summary>
    public string Nummer { get; private set; }

    /// <summary>Wofür, z. B. „Haupteingang und Raum 1.12“.</summary>
    public string Bezeichnung { get; private set; }

    /// <summary>Gebäude oder Raum, zu dem der Schlüssel gehört.</summary>
    public Guid OrtId { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Verloren gemeldet bzw. ausgesondert (bleibt mit seiner Geschichte erhalten).</summary>
    public DateOnly? AusserBetriebSeit { get; private set; }

    public string? AusserBetriebGrund { get; private set; }

    public bool InBetrieb => AusserBetriebSeit is null;

    public void AusserBetrieb(DateOnly ab, string grund)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grund);
        AusserBetriebSeit = ab;
        AusserBetriebGrund = grund.Trim();
    }
}

/// <summary>Ausgabe eines Schlüssels an eine Person (Kursleitung, Mitarbeitende) – mit Rückgabe; nichts wird überschrieben.</summary>
public sealed class Schluesselausgabe
{
    private Schluesselausgabe()
    {
        AusgegebenVon = string.Empty;
    }

    public Schluesselausgabe(Guid schluesselId, Guid personId, DateOnly am, string von, string? bemerkung)
    {
        Id = Ids.Neu();
        SchluesselId = schluesselId;
        PersonId = personId;
        AusgegebenAm = am;
        AusgegebenVon = von;
        Bemerkung = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim();
    }

    public Guid Id { get; private set; }

    public Guid SchluesselId { get; private set; }

    public Guid PersonId { get; private set; }

    public DateOnly AusgegebenAm { get; private set; }

    public string AusgegebenVon { get; private set; }

    public DateOnly? ZurueckAm { get; private set; }

    public string? ZurueckVon { get; private set; }

    public string? Bemerkung { get; private set; }

    public bool Offen => ZurueckAm is null;

    public void Zurueck(DateOnly am, string von)
    {
        if (ZurueckAm is not null)
        {
            throw new InvalidOperationException("Der Schlüssel ist bereits zurückgegeben.");
        }

        if (am < AusgegebenAm)
        {
            throw new ArgumentException("Die Rückgabe liegt vor der Ausgabe.", nameof(am));
        }

        ZurueckAm = am;
        ZurueckVon = von;
    }
}
