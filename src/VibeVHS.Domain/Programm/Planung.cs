namespace VibeVHS.Domain.Programm;

/// <summary>Schlanke Sicht auf eine Veranstaltung für Konfliktprüfung und Raumplan.</summary>
public sealed record Termin(Guid VeranstaltungId, Guid KursId, string Kurs, DateTime Beginn, DateTime Ende, IReadOnlyList<Guid> OrtIds,
    Guid? DozentId, bool Ausgefallen)
{
    public bool UeberschneidetSich(Termin anderer) => Beginn < anderer.Ende && anderer.Beginn < Ende;

    public static Termin Von(Veranstaltung v, string kurs)
        => new(v.Id, v.KursId, kurs, v.Beginn, v.Ende, v.Orte.Select(o => o.OrtId).ToList(), v.DozentId, v.Ausgefallen);
}

public enum Konfliktart
{
    Raum,
    Dozent,
}

public sealed record Konflikt(Konfliktart Art, Termin A, Termin B, Guid? OrtId)
{
    public string Beschreibung(Func<Guid, string> ortName) => Art == Konfliktart.Raum
        ? $"Raumkonflikt in {ortName(OrtId!.Value)}: {A.Kurs} und {B.Kurs} ({Ueberschneidung})"
        : $"Dozentenkonflikt: {A.Kurs} und {B.Kurs} ({Ueberschneidung})";

    private string Ueberschneidung
    {
        get
        {
            var von = A.Beginn > B.Beginn ? A.Beginn : B.Beginn;
            var bis = A.Ende < B.Ende ? A.Ende : B.Ende;
            return $"{von:ddd dd.MM. HH:mm}–{bis:HH:mm}";
        }
    }
}

public static class Konfliktpruefung
{
    /// <summary>
    /// Überschneidungen in exklusiven Orten (Räume, virtuelle Räume) und bei Dozierenden. Ausgefallene Termine zählen nicht,
    /// Termine desselben Kurses nicht untereinander.
    /// </summary>
    public static IReadOnlyList<Konflikt> Finden(IEnumerable<Termin> termine, Func<Guid, bool> istExklusiv)
    {
        var liste = termine.Where(t => !t.Ausgefallen).OrderBy(t => t.Beginn).ToList();
        var konflikte = new List<Konflikt>();
        for (var i = 0; i < liste.Count; i++)
        {
            for (var j = i + 1; j < liste.Count && liste[j].Beginn < liste[i].Ende; j++)
            {
                var a = liste[i];
                var b = liste[j];
                if (a.KursId == b.KursId || !a.UeberschneidetSich(b))
                {
                    continue;
                }

                foreach (var ort in a.OrtIds.Intersect(b.OrtIds).Where(istExklusiv))
                {
                    konflikte.Add(new Konflikt(Konfliktart.Raum, a, b, ort));
                }

                if (a.DozentId is { } d && d == b.DozentId)
                {
                    konflikte.Add(new Konflikt(Konfliktart.Dozent, a, b, null));
                }
            }
        }

        return konflikte;
    }
}

/// <summary>Eignung eines Orts für einen Kurs an einem Tag. Nicht geeignet heißt immer: mit Begründung.</summary>
public sealed record Ortseignung(Ort Ort, bool Geeignet, IReadOnlyList<string> Gruende, int? Kapazitaet)
{
    public string? Begruendung => Gruende.Count == 0 ? null : string.Join(" ", Gruende);
}

public static class Ortsplanung
{
    public static Ortseignung Pruefen(Ortsverzeichnis verzeichnis, Ort ort, Nutzungsart nutzungsart, IEnumerable<string> benoetigteMerkmale,
        IEnumerable<DateOnly> tage, DateTimeOffset bekanntAm)
    {
        var gruende = new List<string>();
        if (!ort.Buchbar)
        {
            gruende.Add($"{(ort.Art == Ortsart.Liegenschaft ? "Liegenschaften" : "Gebäude")} sind nicht buchbar; bitte einen Raum darin wählen.");
        }

        if (!ort.Aktiv)
        {
            gruende.Add("Der Ort ist stillgelegt.");
        }

        var kapazitaet = ort.KapazitaetFuer(nutzungsart);
        if (ort.Buchbar && kapazitaet is null)
        {
            gruende.Add($"Für die Nutzungsart „{NutzungsartAnzeige.Text(nutzungsart)}“ ist hier keine Kapazität hinterlegt.");
        }

        var tagesliste = tage.Distinct().Order().ToList();
        foreach (var schluessel in benoetigteMerkmale.Distinct())
        {
            var name = verzeichnis.Katalog.TryGetValue(schluessel, out var m) ? m.Name : schluessel;
            var fehlend = tagesliste.Where(t => !verzeichnis.WirksameMerkmale(ort, t, bekanntAm).Any(w => w.Merkmal.Schluessel == schluessel)).ToList();
            if (fehlend.Count == tagesliste.Count || tagesliste.Count == 0)
            {
                gruende.Add($"Merkmal „{name}“ fehlt.");
            }
            else if (fehlend.Count > 0)
            {
                gruende.Add($"Merkmal „{name}“ gilt nicht an allen Terminen (zuerst fehlend am {fehlend[0]:dd.MM.yyyy}, z. B. Zulassung abgelaufen).");
            }
        }

        return new Ortseignung(ort, gruende.Count == 0, gruende, kapazitaet);
    }

    /// <summary>
    /// Kapazität einer Veranstaltung: Summe über alle Orte (hybrid: Raum + Online), je Ort die eigene Höchstzahl oder die
    /// Kapazität des Orts für die Nutzungsart (online: <see cref="Nutzungsart.Online"/>), höchstens die Höchstzahl des Kurses.
    /// </summary>
    public static int Kapazitaet(Veranstaltung v, Kurs kurs, Ortsverzeichnis verzeichnis)
    {
        var summe = 0;
        foreach (var vo in v.Orte)
        {
            var ort = verzeichnis.Finden(vo.OrtId);
            var nutzung = ort?.Art == Ortsart.VirtuellerRaum ? Nutzungsart.Online : kurs.Nutzungsart;
            summe += vo.Hoechstzahl ?? ort?.KapazitaetFuer(nutzung) ?? ort?.KapazitaetFuer(kurs.Nutzungsart) ?? 0;
        }

        return Math.Min(summe, kurs.MaxTeilnehmende);
    }

    /// <summary>Plätze an einem Ort einer Veranstaltung (für die Vorab-Einbuchung bei hybriden Terminen).</summary>
    public static int KapazitaetAmOrt(VeranstaltungsOrt vo, Kurs kurs, Ortsverzeichnis verzeichnis)
    {
        var ort = verzeichnis.Finden(vo.OrtId);
        var nutzung = ort?.Art == Ortsart.VirtuellerRaum ? Nutzungsart.Online : kurs.Nutzungsart;
        return Math.Min(vo.Hoechstzahl ?? ort?.KapazitaetFuer(nutzung) ?? ort?.KapazitaetFuer(kurs.Nutzungsart) ?? 0, kurs.MaxTeilnehmende);
    }
}
