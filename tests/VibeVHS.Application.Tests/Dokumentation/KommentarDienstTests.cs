using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Dokumentation;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Dokumentation;

namespace VibeVHS.Application.Tests.Dokumentation;

public class KommentarDienstTests
{
    private static readonly Benutzer Leitung = new("sschulte", "S. Schulte", Rolle.Leitung);
    private static readonly Benutzer Systemverwaltung = new("admin", "A. Admin", Rolle.Systemverwaltung);
    private static readonly Benutzer Teilnehmerservice = new("kfriesen", "Katja Friedsen", Rolle.Teilnehmerservice);

    private readonly SpeicherKommentare _speicher = new();
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));

    private KommentarDienst Dienst(Benutzer wer, out SpeicherProtokoll protokoll)
    {
        var b = new FesterBenutzer(wer);
        protokoll = new SpeicherProtokoll(b, _uhr);
        return new KommentarDienst(_speicher, b, protokoll, _uhr);
    }

    [Theory]
    [InlineData(Rolle.Leitung, true)]
    [InlineData(Rolle.Verwaltungsleitung, true)]
    [InlineData(Rolle.Systemverwaltung, true)]
    [InlineData(Rolle.Teilnehmerservice, false)]
    [InlineData(Rolle.Fachbereichsleitung, false)]
    [InlineData(Rolle.Pruefung, false)]
    [InlineData(Rolle.Dozierende, false)]
    public async Task Nur_Leitung_Verwaltungsleitung_und_Systemverwaltung_duerfen_lesen_und_kommentieren(Rolle rolle, bool erlaubt)
    {
        var dienst = Dienst(new Benutzer("x", "X", rolle), out _);

        (await dienst.RechtAsync(abbruch: TestContext.Current.CancellationToken)).Erlaubt.ShouldBe(erlaubt);
        if (erlaubt)
        {
            await dienst.SchreibenAsync("hilfe", "bedienung", "Bedienung", "Passt.", abbruch: TestContext.Current.CancellationToken);
            (await dienst.AlleAsync(abbruch: TestContext.Current.CancellationToken)).Count.ShouldBe(1);
        }
        else
        {
            // Rechte im Dienst, nicht nur in der Oberfläche
            (await Should.ThrowAsync<InvalidOperationException>(() => dienst.SchreibenAsync("hilfe", "x", "X", "Text", abbruch: TestContext.Current.CancellationToken))).Message.ShouldContain("Nur für die Rolle");
            await Should.ThrowAsync<InvalidOperationException>(() => dienst.AlleAsync(abbruch: TestContext.Current.CancellationToken));
            await Should.ThrowAsync<InvalidOperationException>(() => dienst.ZuDokumentAsync("hilfe", abbruch: TestContext.Current.CancellationToken));
            await Should.ThrowAsync<InvalidOperationException>(() => dienst.AlsMarkdownAsync(abbruch: TestContext.Current.CancellationToken));
            _speicher.Kommentare.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Antworten_haengen_am_ersten_Kommentar_und_uebernehmen_dessen_Abschnitt()
    {
        var leitung = Dienst(Leitung, out var protokoll);
        var start = await leitung.SchreibenAsync("spezifikation/anforderungen", "bedienung", "Bedienung", "QA-11 fehlt ein Beispiel.", abbruch: TestContext.Current.CancellationToken);
        _uhr.Advance(TimeSpan.FromMinutes(5));
        var antwort = await Dienst(Systemverwaltung, out _).SchreibenAsync("egal", "egal", "egal", "Ergänzt.", start.Id, abbruch: TestContext.Current.CancellationToken);
        _uhr.Advance(TimeSpan.FromMinutes(5));
        // Antwort auf die Antwort landet trotzdem am Start (nur eine Ebene)
        var zweite = await leitung.SchreibenAsync("egal", "egal", "egal", "Danke!", antwort.Id, abbruch: TestContext.Current.CancellationToken);

        antwort.Dokument.ShouldBe("spezifikation/anforderungen");
        antwort.Abschnitt.ShouldBe("bedienung");
        zweite.AntwortAuf.ShouldBe(start.Id);
        var faeden = await leitung.ZuDokumentAsync("spezifikation/anforderungen", abbruch: TestContext.Current.CancellationToken);
        faeden["bedienung"].Single().Antworten.Select(a => a.Text).ShouldBe(["Ergänzt.", "Danke!"]);
        protokoll.Eintraege.ShouldContain(e => e.Aktion == "Dokumentation.Kommentar" && e.Beschreibung.Contains("anforderungen#bedienung"));
    }

    [Fact]
    public async Task Erledigt_markieren_und_wieder_oeffnen_mit_Name_und_Zeit()
    {
        var dienst = Dienst(Leitung, out _);
        var k = await dienst.SchreibenAsync("hilfe", "x", "X", "Bitte prüfen.", abbruch: TestContext.Current.CancellationToken);

        await dienst.ErledigtSetzenAsync(k.Id, true, abbruch: TestContext.Current.CancellationToken);
        k.Erledigt.ShouldBeTrue();
        k.ErledigtVonName.ShouldBe("S. Schulte");
        k.ErledigtAm.ShouldBe(_uhr.GetUtcNow());

        await dienst.ErledigtSetzenAsync(k.Id, false, abbruch: TestContext.Current.CancellationToken);
        k.Erledigt.ShouldBeFalse();
        k.ErledigtVonName.ShouldBeNull();
    }

    [Fact]
    public async Task Leere_oder_zu_lange_Kommentare_werden_abgelehnt()
    {
        var dienst = Dienst(Leitung, out _);

        await Should.ThrowAsync<ArgumentException>(() => dienst.SchreibenAsync("hilfe", "x", "X", "   ", abbruch: TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentException>(() => dienst.SchreibenAsync("hilfe", "x", "X", new string('a', Kommentar.TextMaxZeichen + 1), abbruch: TestContext.Current.CancellationToken));
        _speicher.Kommentare.ShouldBeEmpty();
    }

    [Fact]
    public async Task Markdown_Export_listet_offene_vor_erledigten_mit_Abschnitt_und_Antworten()
    {
        var dienst = Dienst(Leitung, out _);
        var a = await dienst.SchreibenAsync("spezifikation/fahrplan", "i10", "I10 Bereitstellung", "Termin fehlt.", abbruch: TestContext.Current.CancellationToken);
        await dienst.SchreibenAsync("hilfe", "", "", "Zeile eins\nZeile zwei", abbruch: TestContext.Current.CancellationToken);
        await dienst.SchreibenAsync("x", "x", "x", "Wird nachgetragen.", a.Id, abbruch: TestContext.Current.CancellationToken);
        await dienst.ErledigtSetzenAsync(a.Id, true, abbruch: TestContext.Current.CancellationToken);

        var md = await dienst.AlsMarkdownAsync(abbruch: TestContext.Current.CancellationToken);

        md.ShouldContain("1 offen, 1 erledigt");
        md.IndexOf("## Offen", StringComparison.Ordinal).ShouldBeLessThan(md.IndexOf("## Erledigt", StringComparison.Ordinal));
        md.ShouldContain("**„I10 Bereitstellung“ (#i10)**");
        md.ShouldContain("**ganzes Dokument**");
        md.ShouldContain("  > Zeile eins\n  > Zeile zwei\n");
        md.ShouldContain("  - Antwort: S. Schulte (Leitung)");
    }

    [Fact]
    public async Task Erledigt_gilt_nur_fuer_den_ersten_Kommentar_eines_Fadens()
    {
        var dienst = Dienst(Leitung, out _);
        var start = await dienst.SchreibenAsync("hilfe", "x", "X", "Frage", abbruch: TestContext.Current.CancellationToken);
        var antwort = await dienst.SchreibenAsync("hilfe", "x", "X", "Antwort", start.Id, abbruch: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() => dienst.ErledigtSetzenAsync(antwort.Id, true, abbruch: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Teilnehmerservice_kann_nicht_erledigen()
    {
        var k = await Dienst(Leitung, out _).SchreibenAsync("hilfe", "x", "X", "Frage", abbruch: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() => Dienst(Teilnehmerservice, out _).ErledigtSetzenAsync(k.Id, true, abbruch: TestContext.Current.CancellationToken));
        k.Erledigt.ShouldBeFalse();
    }

    private sealed class SpeicherKommentare : IKommentarSpeicher
    {
        public List<Kommentar> Kommentare { get; } = [];

        public Task<IReadOnlyList<Kommentar>> ZuDokumentAsync(string dokument, CancellationToken abbruch = default)
            => Task.FromResult<IReadOnlyList<Kommentar>>(Kommentare.Where(k => k.Dokument == dokument).ToList());

        public Task<IReadOnlyList<Kommentar>> AlleAsync(CancellationToken abbruch = default)
            => Task.FromResult<IReadOnlyList<Kommentar>>(Kommentare.ToList());

        public Task<Kommentar?> LadenAsync(Guid id, CancellationToken abbruch = default)
            => Task.FromResult(Kommentare.FirstOrDefault(k => k.Id == id));

        public void Hinzufuegen(Kommentar kommentar) => Kommentare.Add(kommentar);

        public Task SpeichernAsync(CancellationToken abbruch = default) => Task.CompletedTask;
    }
}
