using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibeVHS.Application.Shop;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Programm;
using VibeVHS.Domain.Shop;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Web.Shop;
using VibeVHS.Web.Sicherheit;
using static VibeVHS.Web.Tests.ShopTestHost;

namespace VibeVHS.Web.Tests;

/// <summary>Kurs-QR und Schnellanmeldung über den persönlichen Link / das gemerkte Gerät.</summary>
public sealed class SchnellanmeldungTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Guid> PersonAsync(ShopTestHost h)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var jetzt = h.Uhr.GetLocalNow();
        var p = Person.Anlegen("Olena", "Kovalenko", new DateOnly(2024, 1, 1), jetzt.AddYears(-2), new DateOnly(1990, 5, 1), "olena@example.org", "uk");
        p.RolleHinzufuegen(Personenrolle.Teilnehmende, new DateOnly(2024, 1, 1), Erfassung.Erst(jetzt.AddYears(-2)));
        db.Personen.Add(p);
        await db.SaveChangesAsync(Ct);
        return p.Id;
    }

    private static async Task<T> MitDienstAsync<T>(ShopTestHost h, Func<Schnellanmeldedienst, Task<T>> arbeit)
    {
        await using var bereich = h.App.Services.CreateAsyncScope();
        bereich.ServiceProvider.GetRequiredService<BenutzerKontext>().Gesetzt = KursQrAblauf.Selbst;
        return await arbeit(bereich.ServiceProvider.GetRequiredService<Schnellanmeldedienst>());
    }

    [Theory]
    [InlineData("https://vhs.example/vibevhs/k/26H-501", "26H-501")]
    [InlineData("http://localhost:5199/k/26H-501/", "26H-501")]
    [InlineData("/k/26H-501", "26H-501")]
    [InlineData("https://vhs.example/checkin/abc", null)]
    [InlineData("VHS-A:geheim", null)]
    [InlineData("https://evil.example/k/<script>", null)]
    public void Kursnummer_aus_dem_QR(string inhalt, string? nummer) => KursQrAblauf.KursnummerAusQr(inhalt).ShouldBe(nummer);

    [Fact]
    public async Task Schnellanmeldung_mit_Stornolink_24_Stunden_kostenlos()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-950", max: 10, Buchungsrichtlinie.Oeffentlich with { BearbeitungsgebuehrCent = 500 });
        var person = await PersonAsync(h);

        var v = await MitDienstAsync(h, d => d.VorschauAsync(person, "26H-950", Ct));
        v.Buchbar.ShouldBeTrue();
        v.Vorname.ShouldBe("Olena");
        v.Zahlart.ShouldBe(Zahlart.Ueberweisung); // kein Mandat → Rechnung
        v.EntgeltCent.ShouldBe(8400);

        (await Should.ThrowAsync<ShopFehler>(() => MitDienstAsync(h, d => d.AnmeldenAsync(person, "26H-950", "2020-01", "2026-08", "Link", t => new Uri("https://x/" + t), Ct))))
            .Code.ShouldBe("einwilligung-veraltet");

        string? token = null;
        var e = await MitDienstAsync(h, d => d.AnmeldenAsync(person, "26H-950", "2026-08", "2026-08", "Link", t => { token = t; return new Uri($"https://vhs.example/k/storno/{t}"); }, Ct));
        e.Status.ShouldBe("angemeldet");
        e.StornoLinkVerschickt.ShouldBeTrue();

        await using (var bereich = h.App.Services.CreateAsyncScope())
        {
            var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
            var b = await db.Belegungen.SingleAsync(Ct);
            b.Weg.ShouldBe(Buchungsweg.Online);
            b.PersonId.ShouldBe(person);
            var mails = await db.Postausgang.Where(m => m.PersonId == person).Select(m => m.Inhalt).ToListAsync(Ct);
            mails.ShouldContain(m => m.Contains("/k/storno/" + token, StringComparison.Ordinal) && m.Contains("/uebersetzen/", StringComparison.Ordinal));
            (await db.Set<Schnellanmeldung>().SingleAsync(Ct)).StornoHash.ShouldNotContain(token!);
            (await db.Protokoll.CountAsync(p => p.Aktion == "Shop.Schnellanmeldung", Ct)).ShouldBe(1);
        }

        // Zweiter Versuch: bereits angemeldet (gleiche Buchungsprüfung)
        (await MitDienstAsync(h, d => d.VorschauAsync(person, "26H-950", Ct))).Buchbar.ShouldBeFalse();

        // Innerhalb von 24 h kostenlos, obwohl die AGB eine Bearbeitungsgebühr vorsehen
        var vor = await MitDienstAsync(h, d => d.StornoVorschauAsync(token!, Ct));
        vor.Moeglich.ShouldBeTrue();
        vor.GebuehrCent.ShouldBe(0);
        var regel = await MitDienstAsync(h, d => d.StornierenAsync(token!, Ct));
        regel.GebuehrCent.ShouldBe(0);
        (await MitDienstAsync(h, d => d.StornoVorschauAsync(token!, Ct))).Moeglich.ShouldBeFalse();
        (await Should.ThrowAsync<ShopFehler>(() => MitDienstAsync(h, d => d.StornoVorschauAsync("erfunden", Ct)))).Status.ShouldBe(404);
    }

    [Fact]
    public async Task Nach_24_Stunden_gelten_die_AGB_und_nicht_oeffentliche_Kurse_sind_ausgeschlossen()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-951", max: 10, Buchungsrichtlinie.Oeffentlich with { BearbeitungsgebuehrCent = 500 });
        await h.KursAsync("26H-952", max: 10, sichtbarkeit: Sichtbarkeit.Intern);
        var person = await PersonAsync(h);
        string? token = null;
        await MitDienstAsync(h, d => d.AnmeldenAsync(person, "26H-951", "2026-08", "2026-08", "Gerät", t => { token = t; return new Uri("https://x/" + t); }, Ct));
        h.Uhr.Advance(TimeSpan.FromHours(30));
        (await MitDienstAsync(h, d => d.StornierenAsync(token!, Ct))).GebuehrCent.ShouldBe(500);
        (await Should.ThrowAsync<ShopFehler>(() => MitDienstAsync(h, d => d.VorschauAsync(person, "26H-952", Ct)))).Code.ShouldBe("kurs-unbekannt");
    }

    [Fact]
    public async Task Volle_Kurse_fuehren_auf_die_Warteliste_ohne_Stornomail()
    {
        await using var h = await StartenAsync();
        await h.KursAsync("26H-953", max: 1);
        var a = await PersonAsync(h);
        var b = await PersonAsync(h);
        await MitDienstAsync(h, d => d.AnmeldenAsync(a, "26H-953", "2026-08", "2026-08", "Link", t => new Uri("https://x/" + t), Ct));
        var v = await MitDienstAsync(h, d => d.VorschauAsync(b, "26H-953", Ct));
        v.Warteliste.ShouldBeTrue();
        var e = await MitDienstAsync(h, d => d.AnmeldenAsync(b, "26H-953", "2026-08", "2026-08", "Link", t => new Uri("https://x/" + t), Ct));
        e.Status.ShouldBe("warteliste");
        e.StornoLinkVerschickt.ShouldBeFalse();
    }

    [Fact]
    public async Task Qr_Bogen_ist_ein_PDF()
    {
        await using var h = await StartenAsync();
        var pdf = h.App.Services.GetRequiredService<IKursQrBogen>().Erzeugen("Kurs-QR-Codes Herbst 2026",
            [new("26H-501", "Englisch A2", "ab 29.09.2026", "https://vhs.example/k/26H-501"), new("26H-502", "Spanisch A1", null, "https://vhs.example/k/26H-502")]);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(2000);
        // Je Kurs ein eingebettetes QR-Bild (MigraDoc zeigt sonst graue Kästen „Image has no valid type.“)
        Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count.ShouldBe(2);
        Regex.IsMatch(KursQrAblauf.Kursseite(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), "26H-501"), "^shop/kurs.html#kurs=26H-501$").ShouldBeTrue();
    }
}
