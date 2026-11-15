using VibeVHS.Application.Anmeldung;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Portal;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Application.Personen;

/// <summary>Sperrgrund beim Beenden einer Rolle (null = möglich).</summary>
public sealed record Rollenpruefung(Personenrolle Rolle, string? Sperrgrund);

/// <summary>
/// Rollen einer Person (Teilnehmende, Dozierende) hinzufügen und beenden – additiv und zeitlich gültig, mit Grund in der
/// Datenhistorie. Beenden ist gesperrt, solange noch etwas daran hängt (künftige Einsätze, offene Honorare, offene Belegungen).
/// Ein Portal-Konto der Person erhält bzw. verliert dabei den Zugang zum passenden Portal.
/// </summary>
public sealed class PersonenrollenDienst(
    IPersonenSpeicher personen,
    IProgrammSpeicher programm,
    IAnmeldungSpeicher anmeldungen,
    IFinanzSpeicher finanzen,
    IPortalSpeicher portal,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    public static readonly Rolle[] Rollen = [Rolle.Teilnehmerservice, Rolle.Fachbereichsleitung, Rolle.Verwaltungsleitung, Rolle.Leitung];

    /// <summary>Hier pflegbare Rollen; „Mitarbeitende“ entsteht über das Benutzerkonto (Einstellungen → Benutzer).</summary>
    public static readonly Personenrolle[] Pflegbar = [Personenrolle.Teilnehmende, Personenrolle.Dozierende];

    public async Task RolleHinzufuegenAsync(Guid personId, Personenrolle rolle, DateOnly ab, string grund, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        Pruefen(rolle, grund);
        var person = await PersonAsync(personId, abbruch);
        if (person.RollenAm(ab, uhr.GetLocalNow()).HasFlag(rolle))
        {
            throw new InvalidOperationException($"Die Person ist am {ab:dd.MM.yyyy} bereits {PersonMerkmal.RollenAnzeige(rolle)}.");
        }

        person.RolleHinzufuegen(rolle, ab, Erfassung.Aenderung(uhr.GetLocalNow(), grund.Trim(), wer.Anzeige));
        await PortalkontoAbgleichenAsync(person, abbruch);
        await protokoll.ProtokollierenAsync("Person.RolleHinzugefuegt", $"{Pseudonym.Person(personId)}: Rolle {PersonMerkmal.RollenAnzeige(rolle)} ab {ab:dd.MM.yyyy} ({grund.Trim()})",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <param name="letzterTag">Letzter Tag mit der Rolle; ab dem Folgetag gilt sie nicht mehr.</param>
    public async Task RolleBeendenAsync(Guid personId, Personenrolle rolle, DateOnly letzterTag, string grund, CancellationToken abbruch = default)
    {
        var wer = await RechtAsync(abbruch);
        Pruefen(rolle, grund);
        if ((await PruefenAsync(personId, rolle, letzterTag, abbruch)).Sperrgrund is { } sperre)
        {
            throw new InvalidOperationException(sperre);
        }

        var person = await PersonAsync(personId, abbruch);
        // Als Änderung ab Datum (neuer Rollenstand ohne diese Rolle), nicht als Endemarke – die übrigen Rollen gelten weiter.
        person.RolleBeendenAb(rolle, letzterTag.AddDays(1), Erfassung.Aenderung(uhr.GetLocalNow(), $"Rolle beendet: {grund.Trim()}", wer.Anzeige));
        await PortalkontoAbgleichenAsync(person, abbruch);
        await protokoll.ProtokollierenAsync("Person.RolleBeendet", $"{Pseudonym.Person(personId)}: Rolle {PersonMerkmal.RollenAnzeige(rolle)} endet am {letzterTag:dd.MM.yyyy} ({grund.Trim()})",
            nameof(Person), personId, abbruch: abbruch);
        await personen.SpeichernAsync(abbruch);
    }

    /// <summary>Kann die Rolle zum <paramref name="letzterTag"/> enden? Sonst mit Grund (für Tooltip und Prüfung).</summary>
    public async Task<Rollenpruefung> PruefenAsync(Guid personId, Personenrolle rolle, DateOnly letzterTag, CancellationToken abbruch = default)
    {
        var nach = letzterTag.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (rolle == Personenrolle.Dozierende)
        {
            var einsaetze = (await programm.TermineAsync(nach, nach.AddYears(3), abbruch))
                .Where(t => t.Termin.DozentId == personId && !t.Termin.Ausgefallen).OrderBy(t => t.Termin.Beginn).ToList();
            if (einsaetze.Count > 0)
            {
                return new(rolle, $"Noch {einsaetze.Count} Einsatz/Einsätze nach dem {letzterTag:dd.MM.yyyy} (zuerst {einsaetze[0].Kursnummer} am {einsaetze[0].Termin.Beginn:dd.MM.yyyy}). Bitte erst eine andere Kursleitung einplanen.");
            }

            var entwuerfe = (await finanzen.BelegeAsync(personId, abbruch)).Count(b => b.Art == Belegart.Honorarabrechnung && !b.Festgeschrieben && !b.Storniert);
            if (entwuerfe > 0)
            {
                return new(rolle, $"Noch {entwuerfe} offene Honorarabrechnung(en). Bitte erst freigeben bzw. verwerfen (Finanzen → Honorare).");
            }
        }
        else if (rolle == Personenrolle.Teilnehmende)
        {
            var offen = (await anmeldungen.BelegungenDerPersonAsync(personId, abbruch))
                .Count(b => b.Belegung.Offen && !b.KursAbgesagt && (b.Beginn is null || b.Beginn >= nach));
            if (offen > 0)
            {
                return new(rolle, $"Noch {offen} Anmeldung(en) bzw. Warteliste für Kurse nach dem {letzterTag:dd.MM.yyyy}. Bitte erst abmelden oder umbuchen.");
            }
        }

        return new(rolle, null);
    }

    /// <summary>
    /// Ein Portal-Konto der Person bekommt genau die Portalrollen ihrer aktuellen Personenrollen (beide Portale bei beiden Rollen,
    /// Umschalter im Portal). Konten von Mitarbeitenden bleiben unberührt.
    /// </summary>
    private async Task PortalkontoAbgleichenAsync(Person person, CancellationToken abbruch)
    {
        if (await portal.KontoZurPersonAsync(person.Id, abbruch) is not { IstPortalkonto: true } konto)
        {
            return;
        }

        var aktuell = person.RollenAm(DateOnly.FromDateTime(uhr.GetLocalNow().Date), uhr.GetLocalNow());
        var soll = new List<Rolle>();
        if (aktuell.HasFlag(Personenrolle.Teilnehmende))
        {
            soll.Add(Rolle.Teilnehmende);
        }

        if (aktuell.HasFlag(Personenrolle.Dozierende))
        {
            soll.Add(Rolle.Dozierende);
        }

        if (soll.Count == 0)
        {
            konto.Deaktivieren();
            return;
        }

        // Bisherige Hauptrolle bleibt vorne (sie ist beim Anmelden aktiv), die neue kommt dazu.
        konto.Aendern(konto.Name, konto.Funktion, soll.OrderBy(r => konto.Rollen.Contains(r) ? konto.Rollen.IndexOf(r) : 99), konto.Fachbereiche);
        konto.Aktivieren();
    }

    private static void Pruefen(Personenrolle rolle, string grund)
    {
        if (!Pflegbar.Contains(rolle))
        {
            throw new ArgumentException("Hier lassen sich die Rollen Teilnehmende und Dozierende pflegen; Mitarbeitende entstehen über ein Benutzerkonto (Einstellungen → Benutzer).");
        }

        if (string.IsNullOrWhiteSpace(grund))
        {
            throw new ArgumentException("Bitte einen Grund angeben (steht in der Datenhistorie).");
        }
    }

    private async Task<Person> PersonAsync(Guid id, CancellationToken abbruch)
    {
        var p = await personen.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Person nicht gefunden.");
        return p.Datenschutz == Datenschutzstatus.Aktiv ? p : throw new InvalidOperationException("Die Person ist gesperrt oder anonymisiert.");
    }

    private async Task<Benutzer> RechtAsync(CancellationToken abbruch)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        return wer.Rolle is { } r && Rollen.Contains(r) ? wer : throw new InvalidOperationException("Rollen von Personen pflegen Teilnehmerservice, Fachbereichsleitung, Verwaltungsleitung und Leitung.");
    }
}
