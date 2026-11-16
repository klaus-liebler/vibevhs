using Microsoft.EntityFrameworkCore;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Gemeinsam;
using VibeVHS.Domain.Programm;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Web.Entwicklung;

/// <summary>
/// Demodaten zu den Werttypen der Merkmale (idempotent, auch für bestehende Datenbanken): je ein Beispiel pro Typ –
/// Ja/Nein (Beamer), Auswahl (Tafel), Ganzzahl (PC-Arbeitsplätze, Fläche), Text (Zugangshinweis) – und die Bedingung
/// „mind. 10 PC-Arbeitsplätze“ für EDV-Kurse (Raum A 1.12 hat nur 8: die Raumauswahl nennt das konkret).
/// </summary>
public static class MerkmalDemodaten
{
    public static async Task AnlegenAsync(IServiceProvider dienste)
    {
        await using var bereich = dienste.CreateAsyncScope();
        var db = bereich.ServiceProvider.GetRequiredService<VibeVhsDbContext>();
        var katalog = await db.Merkmale.ToDictionaryAsync(m => m.Schluessel);
        if (!katalog.ContainsKey("pc") || katalog.ContainsKey("tafel"))
        {
            return; // ohne Programm-Demodaten oder schon angelegt
        }

        var jetzt = bereich.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow();
        var e = Erfassung.Erst(jetzt, "Demodaten Merkmal-Typen");

        // PC-Arbeitsplätze waren Ja/Nein mit Zahl als Bemerkung → Ganzzahl (Demodaten: alle Werte sind Zahlen)
        var pc = katalog["pc"];
        if (pc.Typ == Merkmalstyp.JaNein)
        {
            pc.TypFestlegen(Merkmalstyp.Ganzzahl, verwendet: false, einheit: "Plätze", min: 0, max: 200);
        }

        var neu = new[]
        {
            new Merkmal("tafel", "Tafel", "Ausstattung", vererbbar: false, werte: ["Kreide", "Whiteboard", "Smartboard"]),
            new Merkmal("flaeche", "Raumfläche", "Ausstattung", vererbbar: false, typ: Merkmalstyp.Ganzzahl, einheit: "m²", min: 5, max: 2000),
            new Merkmal("zugang", "Hinweise zum Zugang", "Zugänglichkeit", vererbbar: true, typ: Merkmalstyp.Text),
        };
        db.Merkmale.AddRange(neu);
        foreach (var m in neu)
        {
            katalog[m.Schluessel] = m;
        }

        var orte = await db.Orte.ToListAsync();
        Ort? Raum(string kurz) => orte.FirstOrDefault(o => o.Kurzname == kurz);
        void Setzen(Ort? o, string schluessel, string? wert)
        {
            if (o is not null && !o.Merkmale.Any(x => x.Schluessel == schluessel))
            {
                o.MerkmalSetzenAb(katalog[schluessel], wert, null, new DateOnly(2024, 1, 1), null, e);
            }
        }

        Setzen(Raum("A 2.01"), "tafel", "Smartboard");
        Setzen(Raum("A 2.01"), "flaeche", "60");
        Setzen(Raum("A 1.12"), "tafel", "Whiteboard");
        Setzen(Raum("A 1.12"), "flaeche", "45");
        Setzen(Raum("A 1.12"), "pc", "8");
        Setzen(Raum("A 0.01"), "flaeche", "120");
        Setzen(Raum("B 1.03"), "tafel", "Kreide");
        Setzen(orte.FirstOrDefault(o => o.Art == Ortsart.Gebaeude && o.Name.Contains("Haus A", StringComparison.Ordinal)), "zugang",
            "Haupteingang über die Rampe an der Nordseite; Klingel für den Aufzug neben der Tür.");

        // EDV-Kurse: mindestens 10 PC-Arbeitsplätze (statt nur „PC vorhanden“)
        foreach (var art in await db.Kursarten.Where(k => k.Name == "EDV-Kurs").ToListAsync())
        {
            art.MitOrtsanforderungen(art.BenoetigteMerkmale.Select(b => b == "pc" ? Anforderung.Mindestens("pc", 10) : b));
            foreach (var kurs in await db.Kurse.Where(k => k.KursartId == art.Id).ToListAsync())
            {
                kurs.MitOrtsanforderungen(art.BenoetigteMerkmale);
            }
        }

        await bereich.ServiceProvider.GetRequiredService<IAuditProtokoll>().ProtokollierenAsync("Demodaten.Merkmale",
            "Demodaten Merkmal-Typen: Tafel (Auswahl), Raumfläche und PC-Arbeitsplätze (Ganzzahl), Hinweise zum Zugang (Text); EDV-Kurse verlangen mind. 10 PC-Arbeitsplätze");
        await db.SaveChangesAsync();
    }
}
