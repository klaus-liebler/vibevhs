using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Programm;

namespace VibeVHS.Application.Integration;

/// <summary>Täglich: Lichtbilder nach Kursende plus Frist löschen, Aufgaben „Ausweiskontrolle“ und „Kursbeginn vorbereiten“ abgleichen.</summary>
public sealed class IntegrationRoutinen(
    IntegrationDienst integration,
    IIntegrationSpeicher speicher,
    IProgrammSpeicher programm,
    IPersonenSpeicher personen,
    AufgabenDienst aufgaben,
    TimeProvider uhr)
{
    public async Task<(int Geloescht, Abgleichergebnis Aufgaben)> TaeglichAsync(CancellationToken abbruch = default)
        => (await integration.LichtbilderLoeschenAsync(abbruch), await AufgabenAbgleichenAsync(abbruch));

    public async Task<Abgleichergebnis> AufgabenAbgleichenAsync(CancellationToken abbruch = default)
    {
        var heute = DateOnly.FromDateTime(uhr.GetLocalNow().Date);
        var zweifel = (await speicher.VorlaeufigeAnwesenheitenAsync(abbruch)).Where(a => a.VorlaeufigGrund == IntegrationDienst.GrundZweifel).GroupBy(a => a.PersonId).ToList();
        var namen = await personen.AnzeigenamenAsync(zweifel.Select(g => g.Key), abbruch);
        var kontrolle = zweifel.Select(g => new AufgabenAnlass($"{Aufgabentypen.Ausweiskontrolle}:{g.Key:N}",
            $"Ausweiskontrolle {namen.GetValueOrDefault(g.Key)}: {g.Count()} vorläufige Anwesenheit(en)",
            "Ausweis im Original mit der Berechtigung vergleichen (keine Kopie) und unter Integrationskurse das Ergebnis eintragen.",
            DateOnly.FromDateTime(g.Min(a => a.ErfasstAm).Date).AddDays(7), $"integration?kurs={g.First().KursId}")).ToList();

        var kursbeginn = new List<AufgabenAnlass>();
        foreach (var d in (await speicher.AlleKursdatenAsync(abbruch)).Where(d => d.KursbeginnGemeldetAm is null))
        {
            var kurs = await programm.KursLadenAsync(d.KursId, abbruch);
            if (kurs?.ErsterTermin is not { } beginn || DateOnly.FromDateTime(beginn) > heute.AddDays(14) || DateOnly.FromDateTime(beginn) < heute.AddDays(-30))
            {
                continue;
            }

            var u = await integration.UebersichtAsync(d.KursId, abbruch);
            kursbeginn.Add(new AufgabenAnlass($"{Aufgabentypen.IkKursbeginn}:{d.KursId:N}",
                $"{kurs.Kursnummer} (BAMF {d.BamfKursnummer}): Kursbeginn melden – {(u?.Kursbeginnhindernisse.Count is > 0 and var n ? $"{n} offene Prüfung(en)" : "bereit")}",
                "Berechtigungen und Ausweise prüfen; ohne Prüfung ist der Kursstart gesperrt.", DateOnly.FromDateTime(beginn).AddDays(-3), $"integration?kurs={d.KursId}"));
        }

        return await aufgaben.AbgleichenAsync(Aufgabentypen.Ausweiskontrolle, kontrolle, grund: _ => "Ausweis kontrolliert", abbruch: abbruch)
               + await aufgaben.AbgleichenAsync(Aufgabentypen.IkKursbeginn, kursbeginn, grund: _ => "Kursbeginn gemeldet", abbruch: abbruch);
    }
}
