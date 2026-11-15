using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Durchfuehrung;

namespace VibeVHS.Application.Durchfuehrung;

/// <summary>Öffentliche Adresse der Anwendung (für Links in Mails und QR-Codes).</summary>
public sealed record OeffentlicheAdresse(Uri Basis)
{
    public Uri Link(string pfad) => new($"{Basis.ToString().TrimEnd('/')}/{pfad.TrimStart('/')}");
}

public interface IZugangstokenSpeicher
{
    Task<Zugangstoken?> ZumHashAsync(string hash, CancellationToken abbruch = default);

    Task<Zugangstoken?> LadenAsync(Guid id, CancellationToken abbruch = default);

    Task<IReadOnlyList<Zugangstoken>> DerPersonAsync(Guid personId, CancellationToken abbruch = default);

    void Hinzufuegen(Zugangstoken token);

    Task<Termincode?> TermincodeAsync(string code, CancellationToken abbruch = default);

    Task<Termincode?> TermincodeFuerAsync(Guid veranstaltungId, Guid ortId, CancellationToken abbruch = default);

    void Hinzufuegen(Termincode code);

    Task SpeichernAsync(CancellationToken abbruch = default);
}

/// <summary>Eine gemerkte Person auf einem Gerät (für die Auswahl, wenn eine Familie ein Handy teilt).</summary>
public sealed record GemerktePerson(Guid PersonId, string Geheimnis);

/// <summary>
/// Persönliche, widerrufbare Zugänge zum Check-in: Link in der Bestätigungsmail → gemerktes Gerät (Cookie),
/// QR-Code des Teilnehmerausweises (Pseudonym-Token ohne Klardaten). Gespeichert werden nur Hashes.
/// </summary>
public sealed class CheckinZugang(IZugangstokenSpeicher speicher, OeffentlicheAdresse adresse, IAuditProtokoll protokoll, IAktuellerBenutzer benutzer, TimeProvider uhr)
{
    public const string AusweisPraefix = "VHS-A:";

    /// <summary>Neuer persönlicher Check-in-Link (für die Bestätigungsmail). Wird mit der Mail gespeichert.</summary>
    public Uri LinkErzeugen(Guid personId)
    {
        var (token, geheimnis) = Zugangstoken.Erzeugen(personId, Tokenart.CheckinLink, uhr.GetUtcNow());
        speicher.Hinzufuegen(token);
        return adresse.Link($"checkin/geraet/{geheimnis}");
    }

    /// <summary>Neues Ausweis-Token; Inhalt des QR-Codes (nur Pseudonym, keine Klardaten).</summary>
    public string AusweisErzeugen(Guid personId)
    {
        var (token, geheimnis) = Zugangstoken.Erzeugen(personId, Tokenart.Ausweis, uhr.GetUtcNow());
        speicher.Hinzufuegen(token);
        return AusweisPraefix + geheimnis;
    }

    /// <summary>Link geöffnet: Gerät merken. Liefert das Geräte-Geheimnis für das Cookie, oder null bei ungültigem/widerrufenem Link.</summary>
    public async Task<(string Geheimnis, Guid PersonId)?> GeraetVerbindenAsync(string linkGeheimnis, CancellationToken abbruch = default)
    {
        var link = await speicher.ZumHashAsync(Zugangstoken.HashVon(linkGeheimnis), abbruch);
        if (link is not { Art: Tokenart.CheckinLink, Gueltig: true })
        {
            return null;
        }

        var jetzt = uhr.GetUtcNow();
        link.Genutzt(jetzt);
        var (geraet, geheimnis) = Zugangstoken.Erzeugen(link.PersonId, Tokenart.Geraet, jetzt, link.Id);
        speicher.Hinzufuegen(geraet);
        await protokoll.ProtokollierenAsync("Checkin.GeraetVerbunden", $"Gerät für den Check-in von {Pseudonym.Person(link.PersonId)} gemerkt (über persönlichen Link)", "Person",
            link.PersonId, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return (geheimnis, link.PersonId);
    }

    /// <summary>Person zum persönlichen Link (gültig, nicht gesperrt), sonst null.</summary>
    public async Task<Guid?> PersonZumLinkAsync(string linkGeheimnis, CancellationToken abbruch = default)
    {
        var link = await speicher.ZumHashAsync(Zugangstoken.HashVon(linkGeheimnis), abbruch);
        if (link is not { Art: Tokenart.CheckinLink, Gueltig: true })
        {
            return null;
        }

        link.Genutzt(uhr.GetUtcNow());
        return link.PersonId;
    }

    /// <summary>Gültige Personen zu den Geräte-Geheimnissen aus dem Cookie.</summary>
    public async Task<IReadOnlyList<GemerktePerson>> GemerktePersonenAsync(IEnumerable<string> geheimnisse, CancellationToken abbruch = default)
    {
        var liste = new List<GemerktePerson>();
        foreach (var g in geheimnisse.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().Take(8))
        {
            if (await speicher.ZumHashAsync(Zugangstoken.HashVon(g), abbruch) is { Art: Tokenart.Geraet, Gueltig: true } t && liste.All(p => p.PersonId != t.PersonId))
            {
                liste.Add(new GemerktePerson(t.PersonId, g));
            }
        }

        return liste;
    }

    /// <summary>Gescannter Ausweis → Person (nur gültige, nicht widerrufene Tokens).</summary>
    public async Task<Guid?> AusweisLesenAsync(string? inhalt, CancellationToken abbruch = default)
    {
        if (inhalt is null || !inhalt.StartsWith(AusweisPraefix, StringComparison.Ordinal))
        {
            return null;
        }

        var t = await speicher.ZumHashAsync(Zugangstoken.HashVon(inhalt[AusweisPraefix.Length..]), abbruch);
        return t is { Art: Tokenart.Ausweis, Gueltig: true } ? t.PersonId : null;
    }

    public async Task GenutztAsync(string geheimnis, CancellationToken abbruch = default)
    {
        if (await speicher.ZumHashAsync(Zugangstoken.HashVon(geheimnis), abbruch) is { } t)
        {
            t.Genutzt(uhr.GetUtcNow());
        }
    }

    public Task<IReadOnlyList<Zugangstoken>> DerPersonAsync(Guid personId, CancellationToken abbruch = default) => speicher.DerPersonAsync(personId, abbruch);

    public async Task WiderrufenAsync(Guid tokenId, string grund, CancellationToken abbruch = default)
    {
        await RolleAsync(abbruch);
        var t = await speicher.LadenAsync(tokenId, abbruch) ?? throw new InvalidOperationException("Token nicht gefunden.");
        t.Widerrufen(uhr.GetUtcNow(), grund);
        await protokoll.ProtokollierenAsync("Checkin.TokenWiderrufen", $"{TokenAnzeige.Art(t.Art)} von {Pseudonym.Person(t.PersonId)} widerrufen: {grund}", "Person", t.PersonId,
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Alle Zugänge einer Person widerrufen (z. B. Gerät verloren, Anonymisierung).</summary>
    public async Task<int> AlleWiderrufenAsync(Guid personId, string grund, bool speichern = true, CancellationToken abbruch = default)
    {
        var jetzt = uhr.GetUtcNow();
        var offen = (await speicher.DerPersonAsync(personId, abbruch)).Where(t => t.Gueltig).ToList();
        foreach (var t in offen)
        {
            t.Widerrufen(jetzt, grund);
        }

        if (offen.Count > 0 && speichern)
        {
            await protokoll.ProtokollierenAsync("Checkin.TokensWiderrufen", $"{offen.Count} Check-in-Zugänge von {Pseudonym.Person(personId)} widerrufen: {grund}", "Person", personId,
                abbruch: abbruch);
            await speicher.SpeichernAsync(abbruch);
        }

        return offen.Count;
    }

    private async Task RolleAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        Rolle[] erlaubt = [Rolle.Teilnehmerservice, Rolle.IntegrationskursTeam, Rolle.Verwaltungsleitung, Rolle.Leitung, Rolle.Fachbereichsleitung];
        if (!wer.IstAutomatik && (wer.Rolle is not { } r || !erlaubt.Contains(r)))
        {
            throw new InvalidOperationException(Oberflaeche.Freigabe.NurFuer(wer.Rolle, erlaubt).Begruendung);
        }
    }
}
