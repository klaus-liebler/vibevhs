using System.Security.Cryptography;
using System.Text;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Durchfuehrung;

public enum Tokenart
{
    /// <summary>Persönlicher Check-in-Link aus der Bestätigungsmail; einmal geöffnet, merkt sich das Gerät die Person.</summary>
    CheckinLink,

    /// <summary>Gemerktes Gerät (langlebiges Cookie).</summary>
    Geraet,

    /// <summary>QR-Code des Teilnehmerausweises (Pseudonym, keine Klardaten).</summary>
    Ausweis,
}

public static class TokenAnzeige
{
    public static string Art(Tokenart a) => a switch
    {
        Tokenart.CheckinLink => "Check-in-Link (Mail)",
        Tokenart.Geraet => "gemerktes Gerät",
        _ => "Teilnehmerausweis",
    };
}

/// <summary>
/// Termin-Code für den QR-Aushang: zufälliger Verweis auf Veranstaltung und Ort, nur im Zeitfenster gültig.
/// Enthält selbst keine Daten.
/// </summary>
public sealed class Termincode
{
    private Termincode()
    {
        Code = string.Empty;
    }

    public Termincode(Guid veranstaltungId, Guid ortId, DateTimeOffset gueltigVon, DateTimeOffset gueltigBis, DateTimeOffset jetzt)
    {
        Code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        VeranstaltungId = veranstaltungId;
        OrtId = ortId;
        GueltigVon = gueltigVon;
        GueltigBis = gueltigBis;
        ErstelltAm = jetzt;
    }

    public string Code { get; private set; }

    public Guid VeranstaltungId { get; private set; }

    public Guid OrtId { get; private set; }

    public DateTimeOffset GueltigVon { get; private set; }

    public DateTimeOffset GueltigBis { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }
}

/// <summary>
/// Widerrufbares Zugangstoken für den Check-in. Gespeichert wird nur der SHA-256-Hash des Geheimnisses;
/// das Geheimnis selbst steht nur im Link, im Cookie oder im QR-Code.
/// </summary>
public sealed class Zugangstoken
{
    private Zugangstoken()
    {
        Hash = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Tokenart Art { get; private set; }

    public string Hash { get; private set; }

    public DateTimeOffset ErstelltAm { get; private set; }

    public DateTimeOffset? ZuletztGenutzt { get; private set; }

    public DateTimeOffset? WiderrufenAm { get; private set; }

    public string? WiderrufGrund { get; private set; }

    /// <summary>Bei Geräten: über welchen Link es verbunden wurde.</summary>
    public Guid? Ursprung { get; private set; }

    public bool Gueltig => WiderrufenAm is null;

    /// <summary>Neues Token mit zufälligem Geheimnis (256 Bit, Base64url).</summary>
    public static (Zugangstoken Token, string Geheimnis) Erzeugen(Guid personId, Tokenart art, DateTimeOffset jetzt, Guid? ursprung = null)
    {
        var geheimnis = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new Zugangstoken
        {
            Id = Ids.Neu(),
            PersonId = personId,
            Art = art,
            Hash = HashVon(geheimnis),
            ErstelltAm = jetzt,
            Ursprung = ursprung,
        }, geheimnis);
    }

    public void Genutzt(DateTimeOffset jetzt) => ZuletztGenutzt = jetzt;

    public void Widerrufen(DateTimeOffset jetzt, string grund)
    {
        if (WiderrufenAm is null)
        {
            WiderrufenAm = jetzt;
            WiderrufGrund = grund;
        }
    }

    public static string HashVon(string geheimnis) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(geheimnis.Trim())));
}
