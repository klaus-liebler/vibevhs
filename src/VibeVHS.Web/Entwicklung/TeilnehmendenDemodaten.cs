using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Personen;
using VibeVHS.Infrastructure.Datenbank;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Demokonto fürs Teilnehmendenportal: Olena Kovalenko (Rolle Teilnehmende) mit Buchungsvollmacht für ihren Sohn Taras,
/// damit „für andere buchen“ ausprobiert werden kann. Läuft einmal nach (Protokolleintrag "Demodaten.Teilnehmendenportal").
/// </summary>
internal static class TeilnehmendenDemodaten
{
    private const string Von = "Demodaten";

    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        if (await db.Protokoll.AnyAsync(e => e.Aktion == "Demodaten.Teilnehmendenportal"))
        {
            return;
        }

        var olenaIds = db.MitNamen("Olena", "Kovalenko", DateOnly.FromDateTime(bereich.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow().Date));
        var olena = await db.Personen.Where(p => olenaIds.Contains(p.Id)).OrderBy(p => p.Id).FirstOrDefaultAsync();
        if (olena is null)
        {
            return;
        }

        var uhr = bereich.ServiceProvider.GetRequiredService<TimeProvider>();
        var jetzt = uhr.GetLocalNow();
        var z = await Demozeit.LadenAsync(db, uhr);
        var heute = DateOnly.FromDateTime(jetzt.Date);
        var e = Erfassung.Erst(jetzt, Von);
        if (!await db.Benutzerkonten.AnyAsync(k => k.Login == "olena.kovalenko"))
        {
            db.Benutzerkonten.Add(new Benutzerkonto("olena.kovalenko", "Olena Kovalenko", "Teilnehmerin (Demo)", [Rolle.Teilnehmende], [], olena.Id,
                "Demokonto Teilnehmendenportal: eigene Buchungen, Termine, Unterlagen; darf für ihren Sohn buchen"));
        }

        var taras = Person.Anlegen("Taras", "Kovalenko", heute, jetzt, z.D(2014, 6, 3), null, "uk", Von);
        taras.RolleHinzufuegen(Personenrolle.Teilnehmende, heute, e);
        taras.VollmachtErteilen(Bevollmaechtigtenart.Person, olena.Id, olena.Anzeigename, "Geburtsurkunde vorgelegt (Mutter, sorgeberechtigt)", heute.AddDays(-30), null, e);
        db.Personen.Add(taras);
        db.Protokoll.Add(new AuditEintrag(uhr.GetUtcNow(), "automatik", "Automatik", null, "Demodaten.Teilnehmendenportal", null, null,
            "Demokonto olena.kovalenko (Teilnehmende) angelegt; Buchungsvollmacht für Taras Kovalenko"));
        await db.SaveChangesAsync();
    }
}
