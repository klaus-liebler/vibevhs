using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VibeVHS.Application.Aufgaben;
using VibeVHS.Application.Finanzen;
using VibeVHS.Application.Integration;
using VibeVHS.Application.Programm;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Entwicklung;
using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Tests;

/// <summary>
/// Demodaten werden relativ zum Tagesdatum angelegt: Eine Demo in zwei Monaten sieht aus wie heute – gleich viele überfällige
/// Aufgaben und Rechnungen, laufende und künftige Kurse, ein aktuelles Semester mit passendem Namen und Kürzel.
/// </summary>
public sealed class DemodatenZeitbezugTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Kennzahlen(
        string Semester, string Kuerzel, int Kurse, int Laufend, int Kuenftig, int Belegungen, int UeberfaelligeAufgaben, int OffeneAufgaben,
        int Rechnungen, int UeberfaelligeRechnungen, int AblaufendeMerkmale, DateOnly Heute);

    private sealed class Warnungen : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Meldungen { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Meldungen.Add($"{formatter(state, exception)}: {exception?.Message}");
            }
        }
    }

    private static async Task<Kennzahlen> SeedenAsync(DateTimeOffset jetzt)
    {
        await using var h = await ShopTestHost.StartenAsync(jetzt: jetzt);
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var d = bereich.ServiceProvider;
            d.GetRequiredService<BenutzerKontext>().Gesetzt = Benutzer.Automatik;
            await d.GetRequiredService<VibeVHS.Application.Datenschutz.DatenschutzDienst>().GrundeinstellungenAsync(Ct);
            await d.GetRequiredService<VibeVHS.Application.Anmeldung.VorlagenDienst>().GrundeinstellungenAsync(Ct);
            await d.GetRequiredService<QualifikationsDienst>().GrundeinstellungenAsync(Ct);
        }

        var log = new Warnungen();
        await Demodaten.AllesAnlegenAsync(h.App.Services, 200, log);
        log.Meldungen.ShouldBeEmpty("Alle Demodaten-Teile müssen sich ohne Fehler anlegen lassen");

        // Routinen wie nach dem Start (Startabgleich), damit Aufgaben, Rechnungen und Fristen entstehen.
        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var d = bereich.ServiceProvider;
            d.GetRequiredService<BenutzerKontext>().Gesetzt = Benutzer.Automatik;
            await d.GetRequiredService<ProgrammDienst>().AblaufendeMerkmalePruefenAsync(abbruch: Ct);
            await d.GetRequiredService<QualifikationsDienst>().AufgabenErzeugenAsync(abbruch: Ct);
            await d.GetRequiredService<FinanzRoutinen>().TaeglichAsync(false, Ct);
            await d.GetRequiredService<IntegrationRoutinen>().TaeglichAsync(Ct);
            await d.GetRequiredService<Routinen>().StuendlichAsync(Ct);
        }

        await using var b = h.App.Services.CreateAsyncScope();
        var db = b.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var heute = DateOnly.FromDateTime(h.Uhr.GetLocalNow().Date);
        var jetztLokal = h.Uhr.GetLocalNow().DateTime;
        var semester = (await db.Semester.AsNoTracking().ToListAsync(Ct)).Where(s => s.Beginn <= heute && heute <= s.Ende).OrderBy(s => s.Beginn).First();
        var kurse = await db.Kurse.AsNoTracking().Include(k => k.Veranstaltungen).Where(k => k.Status == Kursstatus.Veroeffentlicht).ToListAsync(Ct);
        var aufgaben = (await db.Set<VibeVHS.Domain.Aufgaben.Aufgabe>().AsNoTracking().ToListAsync(Ct)).Where(a => a.Offen).ToList();
        var rechnungen = (await db.Set<Beleg>().AsNoTracking().ToListAsync(Ct)).Where(r => r.Art == Belegart.Rechnung && r.Festgeschrieben && !r.Storniert).ToList();
        return new Kennzahlen(
            semester.Name, semester.Kuerzel, kurse.Count,
            kurse.Count(k => k.Veranstaltungen.Any(v => v.Beginn < jetztLokal) && k.Veranstaltungen.Any(v => v.Beginn > jetztLokal)),
            kurse.Count(k => k.Veranstaltungen.Count > 0 && k.Veranstaltungen.All(v => v.Beginn > jetztLokal)),
            await db.Belegungen.CountAsync(Ct),
            aufgaben.Count(a => a.Ueberfaellig(heute)), aufgaben.Count,
            rechnungen.Count, rechnungen.Count(r => r.Faellig is { } f && f < heute),
            aufgaben.Count(a => a.Titel.Contains("läuft", StringComparison.OrdinalIgnoreCase) || a.Titel.Contains("Ablauf", StringComparison.OrdinalIgnoreCase)),
            heute);
    }

    /// <summary>Feste Uhrzeit (10:00 in Emsdetten) an einem Tag – der Test hängt nie vom echten Datum ab.</summary>
    private static DateTimeOffset Um10(string tag)
    {
        var d = DateOnly.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var lokal = d.ToDateTime(new TimeOnly(10, 0));
        return new DateTimeOffset(lokal, zone.GetUtcOffset(lokal));
    }

    [Theory]
    [InlineData("2027-01-15")] // 16.11.2026 + 60 Tage (Freitag, über die Weihnachtsferien hinweg)
    [InlineData("2027-01-16")] // Samstag danach
    [InlineData("2026-12-30")] // mitten in den Weihnachtsferien NRW (23.12.2026–06.01.2027)
    public async Task Demodaten_sind_relativ_zum_Tagesdatum(string tag)
    {
        // Die Demodaten werden um ganze Wochen verschoben; verglichen wird deshalb mit demselben Wochentag in der Bezugswoche
        // (Montag 16.11.2026 bis Sonntag 22.11.2026) – dort sieht die Demo genauso aus.
        var datum = DateOnly.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        var bezugstag = Demozeit.Bezugstag.AddDays(datum.DayNumber - Demozeit.Bezugstag.DayNumber - Demozeit.Fuer(datum).Tage);
        var bezug = await SeedenAsync(Um10(bezugstag.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        var spaeter = await SeedenAsync(Um10(tag));
        TestContext.Current.TestOutputHelper?.WriteLine($"Bezug {bezugstag:dd.MM.yyyy}: {bezug} | {datum:dd.MM.yyyy}: {spaeter}");
        bezug.Heute.ShouldBe(bezugstag);
        spaeter.Heute.ShouldBe(datum);
        VergleichPruefen(bezug, spaeter);
    }

    private static void VergleichPruefen(Kennzahlen heute, Kennzahlen spaeter)
    {
        // Es gibt jeweils ein aktuelles Semester; Name und Kürzel folgen der Verschiebung (z. B. 16.11.2026 → „Herbst 2026“, „26H“).
        heute.Kuerzel.ShouldBe(Demozeit.Fuer(heute.Heute).Kuerzel("26H"));
        spaeter.Kuerzel.ShouldBe(Demozeit.Fuer(spaeter.Heute).Kuerzel("26H"));
        heute.Semester.ShouldBe(Demozeit.Semestername(heute.Kuerzel));
        spaeter.Semester.ShouldBe(Demozeit.Semestername(spaeter.Kuerzel));

        spaeter.Kurse.ShouldBe(heute.Kurse);
        spaeter.Laufend.ShouldBeInRange(heute.Laufend - 3, heute.Laufend + 3);
        spaeter.Kuenftig.ShouldBeInRange(heute.Kuenftig - 3, heute.Kuenftig + 3);
        spaeter.Belegungen.ShouldBeInRange(heute.Belegungen * 8 / 10, heute.Belegungen * 12 / 10); // Füllbuchungen hängen von Zufall und Uhrzeit ab
        spaeter.Rechnungen.ShouldBeInRange(heute.Rechnungen * 8 / 10, heute.Rechnungen * 12 / 10 + 2);
        spaeter.UeberfaelligeRechnungen.ShouldBeInRange(heute.UeberfaelligeRechnungen - 3, heute.UeberfaelligeRechnungen + 3);
        spaeter.UeberfaelligeAufgaben.ShouldBeInRange(heute.UeberfaelligeAufgaben - 3, heute.UeberfaelligeAufgaben + 3);
        spaeter.OffeneAufgaben.ShouldBeInRange(heute.OffeneAufgaben - 5, heute.OffeneAufgaben + 5);
    }

    [Theory]
    [InlineData("2026-11-16", 0, "26H")]
    [InlineData("2026-11-22", 0, "26H")] // Sonntag: noch dieselbe Woche
    [InlineData("2026-11-23", 7, "26H")]
    [InlineData("2027-01-15", 56, "26H")]
    [InlineData("2027-05-10", 175, "27F")]
    [InlineData("2027-11-15", 364, "27H")]
    [InlineData("2026-08-01", -112, "26F")]
    public void Verschiebung_in_ganzen_Wochen_und_Semesterkuerzel(string heute, int tage, string kuerzel)
    {
        var z = Demozeit.Fuer(DateOnly.Parse(heute, System.Globalization.CultureInfo.InvariantCulture));
        z.Tage.ShouldBe(tage);
        z.Kuerzel("26H").ShouldBe(kuerzel);
        z.D(2026, 11, 16).DayOfWeek.ShouldBe(DayOfWeek.Monday);
        z.Alt(z.Nr("26H-501")).ShouldBe("26H-501");
        z.Alt(z.Nr("27F-205")).ShouldBe("27F-205");
        z.Kuerzel("27F").ShouldNotBe(z.Kuerzel("26H"));
    }
}
