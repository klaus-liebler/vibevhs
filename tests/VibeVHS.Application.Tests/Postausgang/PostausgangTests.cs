using Microsoft.Extensions.Time.Testing;
using VibeVHS.Application.Postausgang;

namespace VibeVHS.Application.Tests.Postausgang;

public class PostausgangTests
{
    private readonly FakeTimeProvider _uhr = new(new DateTimeOffset(2026, 11, 14, 6, 0, 0, TimeSpan.Zero));
    private readonly SpeicherPostausgang _speicher = new();
    private readonly SpeicherProtokoll _protokoll;
    private readonly PostausgangDienst _dienst;

    public PostausgangTests()
    {
        _protokoll = new SpeicherProtokoll(FesterBenutzer.Krueger, _uhr);
        _dienst = new PostausgangDienst(_speicher, _protokoll, FesterBenutzer.Krueger,
            new Uebersetzungslinks(new Uri("https://vhs.example/")), _uhr);
    }

    [Fact]
    public async Task Kundenmail_enthaelt_immer_einen_Uebersetzungslink_zur_eigenen_Nachricht()
    {
        var mail = await _dienst.KundenmailEinreihenAsync("ayse@example.org", "Ayşe Demir", "Erinnerung",
            "Ihr Kurs findet am Dienstag statt.", "tr", abbruch: cancellationToken());

        mail.Uebersetzungslink.ShouldBe($"https://vhs.example/uebersetzen/{mail.Id:N}?sprache=tr");
        mail.Inhalt.ShouldContain(mail.Uebersetzungslink!);
        mail.Inhalt.ShouldContain("Bu mesajı Türkçe okuyun");
        mail.Inhalt.ShouldStartWith("Ihr Kurs findet am Dienstag statt.");
    }

    [Fact]
    public async Task Deutsche_Mail_bekommt_zweisprachigen_Hinweis()
    {
        var mail = await _dienst.KundenmailEinreihenAsync("a@example.org", null, "Test", "Hallo", "de", abbruch: cancellationToken());

        mail.Inhalt.ShouldContain("Diese Nachricht in Ihrer Sprache lesen / Translate this message into your language:");
    }

    [Fact]
    public async Task Einreihen_landet_im_Postausgang_wird_protokolliert_aber_nicht_selbst_gespeichert()
    {
        var mail = await _dienst.KundenmailEinreihenAsync("a@example.org", "Anna", "Test", "Hallo", "de", abbruch: cancellationToken());

        _speicher.Nachrichten.ShouldHaveSingleItem().ShouldBeSameAs(mail);
        mail.Status.ShouldBe(NachrichtenStatus.Wartend);
        mail.AusgeloestVon.ShouldBe("J. Krüger (Teilnehmerservice)");
        _protokoll.Eintraege.ShouldHaveSingleItem().Aktion.ShouldBe("Postausgang.MailEingereiht");

        // Gespeichert wird gemeinsam mit der fachlichen Änderung durch den Aufrufer.
        _speicher.Speichervorgaenge.ShouldBe(0);
    }

    [Fact]
    public async Task Zustellung_stellt_wartende_Nachrichten_ueber_den_passenden_Kanal_zu()
    {
        await _dienst.KundenmailEinreihenAsync("a@example.org", "Anna", "Mail", "Hallo", "de", abbruch: cancellationToken());
        await _dienst.SchnittstelleEinreihenAsync("BAMF", "Kursbeginnmeldung", "Kurs 26H-701", cancellationToken());
        var mailKanal = new TestKanal(Kanal.EMail, _ => "simuliert");
        var bamf = new TestKanal(Kanal.Schnittstelle, _ => "Vorgang 2026-ABC-0815");
        var zustellung = new Zustellung(_speicher, [mailKanal, bamf], _protokoll, _uhr);

        var ergebnis = await zustellung.WartendeZustellenAsync(abbruch: cancellationToken());

        ergebnis.ShouldBe(new ZustellErgebnis(2, 0));
        _speicher.Nachrichten.ShouldAllBe(n => n.Status == NachrichtenStatus.Zugestellt);
        _speicher.Nachrichten.Single(n => n.Kanal == Kanal.Schnittstelle).Rueckmeldung.ShouldBe("Vorgang 2026-ABC-0815");
        _speicher.Speichervorgaenge.ShouldBe(1);
        _protokoll.Eintraege.Count(e => e.Aktion == "Postausgang.Zugestellt").ShouldBe(2);
    }

    [Fact]
    public async Task Fehlversuche_werden_protokolliert_und_nach_fuenf_Versuchen_aufgegeben()
    {
        await _dienst.KundenmailEinreihenAsync("a@example.org", "Anna", "Mail", "Hallo", "de", abbruch: cancellationToken());
        var kaputt = new TestKanal(Kanal.EMail, _ => throw new InvalidOperationException("SMTP nicht erreichbar"));
        var zustellung = new Zustellung(_speicher, [kaputt], _protokoll, _uhr);

        for (var i = 0; i < AusgehendeNachricht.MaxVersuche; i++)
        {
            await zustellung.WartendeZustellenAsync(abbruch: cancellationToken());
        }

        var mail = _speicher.Nachrichten.Single();
        mail.Status.ShouldBe(NachrichtenStatus.Fehlgeschlagen);
        mail.Fehler.ShouldBe("SMTP nicht erreichbar");
        _protokoll.Eintraege.Count(e => !e.Erfolgreich).ShouldBe(AusgehendeNachricht.MaxVersuche);

        await _dienst.ErneutVersuchenAsync(mail.Id, cancellationToken());
        mail.Status.ShouldBe(NachrichtenStatus.Wartend);
    }

    [Fact]
    public async Task Ohne_passenden_Kanal_bleibt_die_Nachricht_wartend_mit_Fehlerhinweis()
    {
        await _dienst.SchnittstelleEinreihenAsync("Finanzsoftware", "Buchungen", "CSV", cancellationToken());
        var zustellung = new Zustellung(_speicher, [], _protokoll, _uhr);

        var ergebnis = await zustellung.WartendeZustellenAsync(abbruch: cancellationToken());

        ergebnis.Fehler.ShouldBe(1);
        _speicher.Nachrichten.Single().Fehler!.ShouldContain("Kein Zustellkanal");
    }

    private static CancellationToken cancellationToken() => TestContext.Current.CancellationToken;
}
