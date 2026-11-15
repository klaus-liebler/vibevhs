using VibeVHS.Application.Oberflaeche;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Aufgaben;

namespace VibeVHS.Application.Aufgaben;

/// <summary>
/// Aufgabenmodul: Regeln gleichen ihre Anlässe ab (neu anlegen, aktualisieren, automatisch schließen), Mitarbeitende
/// übernehmen Aufgaben aus dem Gruppenkorb, weisen sie zu oder erledigen sie. Überfälliges geht an die Leitung.
/// Jeder Schritt steht im Protokoll.
/// </summary>
public sealed class AufgabenDienst(
    IAufgabenSpeicher speicher,
    IBenutzerkonten konten,
    IAuditProtokoll protokoll,
    IAktuellerBenutzer benutzer,
    TimeProvider uhr)
{
    /// <summary>Wer die Aufgabenregeln (Zuständigkeit, Eskalation, Freigabe) einstellt.</summary>
    public static readonly Rolle[] RegelRollen = [Rolle.Leitung, Rolle.Systemverwaltung];

    private const string Objekt = nameof(Aufgabe);

    private DateOnly Heute => DateOnly.FromDateTime(uhr.GetLocalNow().Date);

    // ---------- Regeln ----------

    /// <summary>Alle Aufgabentypen; fehlende Standardtypen werden angelegt.</summary>
    public async Task<IReadOnlyList<Aufgabentyp>> TypenAsync(CancellationToken abbruch = default)
    {
        var typen = (await speicher.TypenAsync(abbruch)).ToList();
        var fehlend = Aufgabentypen.Vorschlaege().Where(v => typen.All(t => t.Schluessel != v.Schluessel)).ToList();
        if (fehlend.Count > 0)
        {
            foreach (var t in fehlend)
            {
                speicher.Hinzufuegen(t);
                typen.Add(t);
            }

            await speicher.SpeichernAsync(abbruch);
        }

        return typen.OrderBy(t => t.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task<Aufgabentyp> TypAsync(string schluessel, CancellationToken abbruch = default)
        => (await TypenAsync(abbruch)).FirstOrDefault(t => t.Schluessel == schluessel)
           ?? throw new InvalidOperationException($"Unbekannter Aufgabentyp „{schluessel}“.");

    public async Task TypAendernAsync(string schluessel, Rolle rolle, bool jeFachbereich, string? festePerson, int eskalationNachTagen, int? vorlaufTage,
        bool? menschGibtFrei, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var freigabe = Freigabe.NurFuer(wer.Rolle, RegelRollen);
        if (!freigabe.Erlaubt)
        {
            throw new InvalidOperationException(freigabe.Begruendung);
        }

        if (festePerson is { Length: > 0 } && await konten.FindenAsync(festePerson, abbruch) is null)
        {
            throw new InvalidOperationException($"Es gibt kein aktives Benutzerkonto „{festePerson}“.");
        }

        var typ = await TypAsync(schluessel, abbruch);
        if (typ.MenschGibtFrei is null && menschGibtFrei is not null)
        {
            throw new InvalidOperationException("Diese Regel löst keinen kritischen Automatismus aus; eine Freigabeeinstellung ist nicht vorgesehen.");
        }

        typ.Aendern(rolle.ToString(), jeFachbereich, festePerson, eskalationNachTagen, vorlaufTage, menschGibtFrei);
        await protokoll.ProtokollierenAsync("Aufgabenregel.Geaendert",
            $"Regel „{typ.Name}“: {rolle.Anzeigename()}{(jeFachbereich ? " je Fachbereich" : "")}{(typ.FestePerson is null ? "" : $", fest: {typ.FestePerson}")}, Eskalation nach {eskalationNachTagen} Tag(en)"
            + (vorlaufTage is { } v ? $", Vorlauf {v} Tage" : "") + (menschGibtFrei is { } m ? (m ? ", Mensch gibt frei" : ", Automatik handelt selbst") : ""),
            abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>
    /// Gleicht die aktuellen Anlässe einer Regel mit den Aufgaben ab: neue anlegen, bestehende aktualisieren,
    /// automatisch geschlossene wieder öffnen und offene schließen, deren Anlass entfallen ist.
    /// </summary>
    /// <param name="nochAktuell">Für offene Aufgaben, die nicht mehr unter den Anlässen sind: true = trotzdem offen lassen
    /// (z. B. Frist abgelaufen, aber nichts geändert).</param>
    /// <param name="grund">Begründung beim automatischen Schließen (je Aufgabe).</param>
    public async Task<Abgleichergebnis> AbgleichenAsync(string typSchluessel, IReadOnlyCollection<AufgabenAnlass> anlaesse,
        Func<Aufgabe, Task<bool>>? nochAktuell = null, Func<Aufgabe, string>? grund = null, CancellationToken abbruch = default)
    {
        var typ = await TypAsync(typSchluessel, abbruch);
        var jetzt = uhr.GetLocalNow();
        var vorhanden = (await speicher.ZumTypAsync(typ.Schluessel, abbruch)).ToDictionary(a => a.Schluessel);
        Benutzerkonto? fest = typ.FestePerson is { } login ? await konten.FindenAsync(login, abbruch) : null;
        if (fest is not null && (await AktiveAbwesenheitenAsync(abbruch)).FirstOrDefault(x => x.Login == fest.Login) is { } weg)
        {
            // QA-44: Die feste Person ist abwesend – neue Aufgaben gehen an die Vertretung, sonst in den Gruppenkorb.
            fest = weg.VertretungLogin is { } v ? await konten.FindenAsync(v, abbruch) : null;
        }

        int neu = 0, geschlossen = 0, wieder = 0;

        foreach (var anlass in anlaesse.DistinctBy(a => a.Schluessel))
        {
            if (vorhanden.TryGetValue(anlass.Schluessel, out var a))
            {
                if (a.Status == Aufgabenstatus.Geschlossen)
                {
                    a.Wiedereroeffnen();
                    wieder++;
                    await protokoll.ProtokollierenAsync("Aufgabe.WiederGeoeffnet", $"Aufgabe wieder geöffnet, Anlass besteht erneut: {Bezug(a, typ.Name)}", Objekt, a.Id,
                        erfolgreich: false, abbruch: abbruch);
                }

                if (a.Offen)
                {
                    a.Aktualisieren(anlass.Titel, anlass.Beschreibung, anlass.Faellig, anlass.Ziel);
                }

                continue;
            }

            var aufgabe = new Aufgabe(typ.Schluessel, anlass.Schluessel, anlass.Titel, anlass.Beschreibung, anlass.Faellig, typ.Fuer(anlass.Fachbereich),
                anlass.Ziel, jetzt, anlass.Dringlichkeit);
            if (fest is not null)
            {
                aufgabe.Uebernehmen(fest.Login, fest.Name, jetzt);
            }

            speicher.Hinzufuegen(aufgabe);
            neu++;
            await protokoll.ProtokollierenAsync("Aufgabe.Angelegt",
                $"Aufgabe angelegt ({Korb(aufgabe)}{(fest is null ? "" : $", fest zugewiesen an {fest.Name}")}): {Bezug(aufgabe, typ.Name)}", Objekt, aufgabe.Id,
                erfolgreich: false, abbruch: abbruch);
        }

        var aktuell = anlaesse.Select(a => a.Schluessel).ToHashSet();
        foreach (var a in vorhanden.Values.Where(a => a.Offen && !aktuell.Contains(a.Schluessel)))
        {
            if (nochAktuell is not null && await nochAktuell(a))
            {
                continue;
            }

            var text = grund?.Invoke(a) ?? "Anlass entfallen";
            a.AutomatischSchliessen(text, jetzt);
            geschlossen++;
            await protokoll.ProtokollierenAsync("Aufgabe.AutomatischGeschlossen", $"Aufgabe automatisch geschlossen – {text}: {Bezug(a, typ.Name)}", Objekt, a.Id, abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return new(neu, geschlossen, wieder);
    }

    /// <summary>Überfällige Aufgaben nach der eingestellten Zeit an die Leitung eskalieren.</summary>
    public async Task<int> EskalierenAsync(CancellationToken abbruch = default)
    {
        var typen = (await TypenAsync(abbruch)).ToDictionary(t => t.Schluessel);
        var jetzt = uhr.GetLocalNow();
        var anzahl = 0;
        foreach (var a in await speicher.OffeneAsync(verfolgen: true, abbruch))
        {
            var tage = typen.GetValueOrDefault(a.Typ)?.EskalationNachTagen ?? 7;
            if (a.EskaliertAm is not null || a.Faellig.AddDays(tage) >= Heute)
            {
                continue;
            }

            a.Eskalieren(jetzt);
            anzahl++;
            await protokoll.ProtokollierenAsync("Aufgabe.Eskaliert",
                $"An die Leitung eskaliert: seit {a.Faellig:dd.MM.yyyy} fällig ({Korb(a)}{(a.BearbeiterName is null ? ", nicht übernommen" : $", bei {a.BearbeiterName}")}): {Bezug(a, typen.GetValueOrDefault(a.Typ)?.Name ?? a.Typ)}",
                Objekt, a.Id, erfolgreich: false, abbruch: abbruch);
        }

        await speicher.SpeichernAsync(abbruch);
        return anzahl;
    }

    // ---------- Übersicht ----------

    public async Task<Aufgabenuebersicht> UebersichtAsync(CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var offene = await speicher.OffeneAsync(abbruch: abbruch);
        var heuteBeginn = new DateTimeOffset(uhr.GetLocalNow().Date, uhr.GetLocalNow().Offset);
        var leitung = wer.Rolle == Rolle.Leitung;

        static IReadOnlyList<Aufgabe> Sortiert(IEnumerable<Aufgabe> a)
            => a.OrderByDescending(x => x.Dringlichkeit).ThenBy(x => x.Faellig).ThenBy(x => x.Titel, StringComparer.CurrentCulture).ToList();

        var meine = offene.Where(a => a.BearbeiterKennung == wer.Kennung);
        var korb = offene.Where(a => a.BearbeiterKennung is null && ImKorb(wer, a));
        var kollegen = offene.Where(a => a.BearbeiterKennung is not null && a.BearbeiterKennung != wer.Kennung && ImKorb(wer, a));
        var eskaliert = leitung ? offene.Where(a => a.EskaliertAm is not null && a.BearbeiterKennung != wer.Kennung) : [];
        var geschlossen = (await speicher.AbgeschlossenSeitAsync(heuteBeginn, abbruch))
            .Where(a => a.Status == Aufgabenstatus.Geschlossen && (leitung || ImKorb(wer, a) || a.BearbeiterKennung == wer.Kennung))
            .OrderByDescending(a => a.ErledigtAm).ToList();
        // Alle automatischen Einträge seit 0 Uhr. Früher: Ausschnitt der neuesten 400 Protokolleinträge – die Zahl schrumpfte,
        // sobald die Zustellungen des Postausgangs (alle 15 s) ältere Einträge aus dem Fenster schoben.
        var automatisch = (await protokoll.SeitAsync(heuteBeginn.ToUniversalTime(), Benutzer.Automatik.Kennung, abbruch))
            .Where(e => !e.Aktion.StartsWith("Aufgabe.", StringComparison.Ordinal) && !e.Aktion.StartsWith("Postausgang.", StringComparison.Ordinal))
            .ToList();
        var ueberfaellig = leitung
            ? offene.GroupBy(a => (a.Gruppe, a.Fachbereich))
                .Select(g => new UeberfaelligeGruppe(g.Key.Gruppe, g.Key.Fachbereich, g.Count(), g.Count(a => a.Ueberfaellig(Heute)), g.Count(a => a.EskaliertAm is not null)))
                .Where(g => g.Ueberfaellig > 0)
                .OrderByDescending(g => g.Ueberfaellig).ToList()
            : [];

        var abwesenheiten = await speicher.AbwesenheitenAsync(abbruch);
        var vertrete = abwesenheiten.Where(x => x.Aktiv(Heute) && x.VertretungLogin == wer.Kennung).Select(x => x.Login).ToHashSet();
        var inVertretung = offene.Where(a => a.BearbeiterKennung is { } k && vertrete.Contains(k));
        var eigene = abwesenheiten.Where(x => x.Login == wer.Kennung && x.BeendetAm is null && x.Bis >= Heute).OrderBy(x => x.Von).FirstOrDefault();
        return new(Sortiert(meine), Sortiert(korb), Sortiert(kollegen), Sortiert(eskaliert), geschlossen, automatisch, ueberfaellig, KorbName(wer),
            Sortiert(inVertretung), eigene);
    }

    // ---------- Abwesenheit und Vertretung (QA-44) ----------

    public async Task<IReadOnlyList<Abwesenheit>> AktiveAbwesenheitenAsync(CancellationToken abbruch = default)
        => (await speicher.AbwesenheitenAsync(abbruch)).Where(a => a.Aktiv(Heute)).ToList();

    /// <summary>Laufende und künftige Abwesenheiten (für die Übersicht und die Auswahl beim Zuweisen).</summary>
    public async Task<IReadOnlyList<Abwesenheit>> AbwesenheitenAsync(CancellationToken abbruch = default)
        => (await speicher.AbwesenheitenAsync(abbruch)).Where(a => a.BeendetAm is null && a.Bis >= Heute).ToList();

    /// <summary>Abwesenheit eintragen: für sich selbst, die Leitung auch für andere. Die Vertretung braucht eine gemeinsame Rolle und darf nicht selbst abwesend sein.</summary>
    public async Task<Abwesenheit> AbwesenheitEintragenAsync(string? login, DateOnly von, DateOnly bis, string? vertretungLogin, string? grund, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var fuer = string.IsNullOrWhiteSpace(login) ? wer.Kennung : login;
        if (fuer != wer.Kennung && wer.Rolle != Rolle.Leitung)
        {
            throw new InvalidOperationException("Abwesenheiten anderer trägt die Leitung ein.");
        }

        var konto = await konten.FindenAsync(fuer, abbruch) ?? throw new InvalidOperationException("Benutzerkonto nicht gefunden.");
        if (bis < Heute)
        {
            throw new InvalidOperationException("Die Abwesenheit liegt in der Vergangenheit.");
        }

        var alle = await speicher.AbwesenheitenAsync(abbruch);
        if (alle.Any(a => a.Login == konto.Login && a.Ueberschneidet(von, bis)))
        {
            throw new InvalidOperationException("In diesem Zeitraum ist bereits eine Abwesenheit eingetragen.");
        }

        Benutzerkonto? vertretung = null;
        if (!string.IsNullOrWhiteSpace(vertretungLogin))
        {
            vertretung = await konten.FindenAsync(vertretungLogin, abbruch) ?? throw new InvalidOperationException("Die Vertretung hat kein aktives Benutzerkonto.");
            if (!vertretung.Rollen.Intersect(konto.Rollen).Any())
            {
                throw new InvalidOperationException($"{vertretung.Name} hat keine der Rollen von {konto.Name} und kann die Aufgaben nicht bearbeiten.");
            }

            if (alle.Any(a => a.Login == vertretung.Login && a.Ueberschneidet(von, bis)))
            {
                throw new InvalidOperationException($"{vertretung.Name} ist in dieser Zeit selbst abwesend.");
            }
        }

        var abwesenheit = new Abwesenheit(konto.Login, konto.Name, von, bis, vertretung?.Login, vertretung?.Name, grund, uhr.GetLocalNow(), wer.Anzeige);
        speicher.Hinzufuegen(abwesenheit);
        await protokoll.ProtokollierenAsync("Abwesenheit.Eingetragen",
            $"Abwesenheit {konto.Name} {von:dd.MM.yyyy}–{bis:dd.MM.yyyy}, Vertretung: {vertretung?.Name ?? "keine (Aufgaben bleiben im Gruppenkorb)"}", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
        return abwesenheit;
    }

    public async Task AbwesenheitBeendenAsync(Guid id, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await speicher.AbwesenheitAsync(id, abbruch) ?? throw new InvalidOperationException("Abwesenheit nicht gefunden.");
        if (a.Login != wer.Kennung && wer.Rolle != Rolle.Leitung)
        {
            throw new InvalidOperationException("Nur die Person selbst oder die Leitung kann die Abwesenheit beenden.");
        }

        a.Beenden(uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Abwesenheit.Beendet", $"Abwesenheit {a.Name} beendet (wieder da)", abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Liegt die Aufgabe im Gruppenkorb dieses Benutzers (aktive Rolle, ggf. eigener Fachbereich)?</summary>
    public static bool ImKorb(Benutzer wer, Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(wer);
        ArgumentNullException.ThrowIfNull(a);
        if (wer.Rolle is not { } rolle)
        {
            return false;
        }

        if (rolle == Rolle.Leitung && a.Gruppe == nameof(Rolle.Leitung))
        {
            return true;
        }

        if (a.Gruppe != rolle.ToString())
        {
            return false;
        }

        // Fachbereichsleitungen sehen nur ihre Fachbereiche; andere Rollen ohne Fachbereichszuordnung sehen alle.
        return a.Fachbereich is not { } fb
               || wer.ZustaendigeFachbereiche.Contains(fb)
               || (wer.ZustaendigeFachbereiche.Count == 0 && rolle != Rolle.Fachbereichsleitung);
    }

    public static string KorbName(Benutzer wer)
    {
        ArgumentNullException.ThrowIfNull(wer);
        if (wer.Rolle is not { } r)
        {
            return "kein Korb";
        }

        return r == Rolle.Fachbereichsleitung && wer.ZustaendigeFachbereiche.Count > 0
            ? $"{r.Anzeigename()} FB {string.Join(", ", wer.ZustaendigeFachbereiche)}"
            : r.Anzeigename();
    }

    public static string Korb(Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(a);
        var rolle = RollenExtensions.TryParse(a.Gruppe, out var r) ? r.Anzeigename() : a.Gruppe;
        return a.Fachbereich is { } fb ? $"{rolle} FB {fb}" : rolle;
    }

    // ---------- Freigaben (für Tooltips) ----------

    public static Freigabe KannUebernehmen(Benutzer wer, Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(wer);
        ArgumentNullException.ThrowIfNull(a);
        if (!a.Offen)
        {
            return Freigabe.Nein("Die Aufgabe ist bereits abgeschlossen.");
        }

        if (a.BearbeiterKennung == wer.Kennung)
        {
            return Freigabe.Nein("Sie bearbeiten diese Aufgabe bereits.");
        }

        return ImKorb(wer, a) || wer.Rolle == Rolle.Leitung
            ? Freigabe.Ja
            : Freigabe.Nein($"Die Aufgabe liegt im Korb „{Korb(a)}“. Wechseln Sie oben rechts in diese Rolle oder lassen Sie sie sich zuweisen.");
    }

    public static Freigabe KannZuweisen(Benutzer wer, Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(wer);
        ArgumentNullException.ThrowIfNull(a);
        if (!a.Offen)
        {
            return Freigabe.Nein("Die Aufgabe ist bereits abgeschlossen.");
        }

        return ImKorb(wer, a) || wer.Rolle == Rolle.Leitung || a.BearbeiterKennung == wer.Kennung
            ? Freigabe.Ja
            : Freigabe.Nein($"Zuweisen können nur Mitarbeitende des Korbs „{Korb(a)}“ und die Leitung.");
    }

    public static Freigabe KannErledigen(Benutzer wer, Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(wer);
        ArgumentNullException.ThrowIfNull(a);
        if (!a.Offen)
        {
            return Freigabe.Nein("Die Aufgabe ist bereits abgeschlossen.");
        }

        if (a.BearbeiterKennung == wer.Kennung || wer.Rolle == Rolle.Leitung)
        {
            return Freigabe.Ja;
        }

        return a.BearbeiterKennung is null
            ? Freigabe.Nein("Bitte zuerst übernehmen – so sehen die Kolleg:innen, wer sich kümmert.")
            : Freigabe.Nein($"{a.BearbeiterName} bearbeitet diese Aufgabe. Übernehmen Sie sie, wenn Sie sie selbst abschließen möchten.");
    }

    public static Freigabe KannZurueckgeben(Benutzer wer, Aufgabe a)
    {
        ArgumentNullException.ThrowIfNull(wer);
        ArgumentNullException.ThrowIfNull(a);
        return !a.Offen ? Freigabe.Nein("Die Aufgabe ist bereits abgeschlossen.")
            : a.BearbeiterKennung is null ? Freigabe.Nein("Die Aufgabe liegt bereits im Gruppenkorb.")
            : a.BearbeiterKennung == wer.Kennung || wer.Rolle == Rolle.Leitung ? Freigabe.Ja
            : Freigabe.Nein($"Nur {a.BearbeiterName} oder die Leitung kann die Aufgabe zurück in den Korb legen.");
    }

    // ---------- Bearbeiten ----------

    public async Task<IReadOnlyList<Kollegin>> KandidatenAsync(Guid aufgabeId, CancellationToken abbruch = default)
    {
        var a = await LadenAsync(aufgabeId, abbruch);
        if (!RollenExtensions.TryParse(a.Gruppe, out var rolle))
        {
            return [];
        }

        var weg = (await AktiveAbwesenheitenAsync(abbruch)).ToDictionary(x => x.Login);
        return (await konten.AlleAsync(abbruch))
            .Where(k => k.Rollen.Contains(rolle) && ImKorb(k.AlsBenutzer(rolle), a))
            .Select(k => new Kollegin(k.Login, k.Name, weg.TryGetValue(k.Login, out var x)
                ? $"abwesend bis {x.Bis:dd.MM.yyyy}{(x.VertretungName is null ? ", keine Vertretung" : $" → geht an {x.VertretungName}")}"
                : k.Funktion))
            .ToList();
    }

    public async Task UebernehmenAsync(Guid aufgabeId, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await LadenAsync(aufgabeId, abbruch);
        Pruefen(KannUebernehmen(wer, a));
        var vorher = a.BearbeiterName;
        a.Uebernehmen(wer.Kennung, wer.Name, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Aufgabe.Uebernommen",
            $"Aufgabe übernommen{(vorher is null ? " aus dem Korb „" + Korb(a) + "“" : $" (bisher bei {vorher})")}: {await BezugAsync(a, abbruch)}", Objekt, a.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task ZuweisenAsync(Guid aufgabeId, string login, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await LadenAsync(aufgabeId, abbruch);
        Pruefen(KannZuweisen(wer, a));
        var konto = await konten.FindenAsync(login, abbruch) ?? throw new InvalidOperationException($"Es gibt kein aktives Benutzerkonto „{login}“.");
        if (!RollenExtensions.TryParse(a.Gruppe, out var rolle) || !konto.Rollen.Contains(rolle))
        {
            throw new InvalidOperationException($"{konto.Name} hat nicht die zuständige Rolle „{Korb(a)}“.");
        }

        var hinweis = "";
        if ((await AktiveAbwesenheitenAsync(abbruch)).FirstOrDefault(x => x.Login == konto.Login) is { } weg)
        {
            // QA-44: Zuweisung an eine abwesende Person geht an die Vertretung.
            var vertretung = weg.VertretungLogin is { } v ? await konten.FindenAsync(v, abbruch) : null;
            if (vertretung is null || !vertretung.Rollen.Contains(rolle))
            {
                throw new InvalidOperationException($"{konto.Name} ist bis {weg.Bis:dd.MM.yyyy} abwesend und hat keine passende Vertretung. Bitte jemand anderem zuweisen oder im Korb lassen.");
            }

            hinweis = $" (in Vertretung für {konto.Name}, abwesend bis {weg.Bis:dd.MM.yyyy})";
            konto = vertretung;
        }

        a.Uebernehmen(konto.Login, konto.Name, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Aufgabe.Zugewiesen", $"Aufgabe {konto.Name} zugewiesen{hinweis}: {await BezugAsync(a, abbruch)}", Objekt, a.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task ZurueckgebenAsync(Guid aufgabeId, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await LadenAsync(aufgabeId, abbruch);
        Pruefen(KannZurueckgeben(wer, a));
        a.Zurueckgeben();
        await protokoll.ProtokollierenAsync("Aufgabe.Zurueckgegeben", $"Aufgabe zurück in den Korb „{Korb(a)}“: {await BezugAsync(a, abbruch)}", Objekt, a.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    public async Task ErledigenAsync(Guid aufgabeId, string? vermerk, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await LadenAsync(aufgabeId, abbruch);
        if (!wer.IstAutomatik)
        {
            Pruefen(KannErledigen(wer, a));
        }

        a.Erledigen(wer.Name, vermerk, uhr.GetLocalNow());
        await protokoll.ProtokollierenAsync("Aufgabe.Erledigt", $"Aufgabe erledigt{(a.Status == Aufgabenstatus.Erledigt && a.Vermerk is not null ? " mit Vermerk" : "")}: {await BezugAsync(a, abbruch)}", Objekt, a.Id, abbruch: abbruch);
        await speicher.SpeichernAsync(abbruch);
    }

    /// <summary>Lädt die Aufgabe und prüft vorab, ob der aktuelle Benutzer sie erledigen darf (vor Folgeaktionen wie einer Absage).</summary>
    public async Task<Aufgabe> ZumErledigenLadenAsync(Guid id, CancellationToken abbruch = default)
    {
        var wer = await benutzer.ErmittelnAsync(abbruch);
        var a = await LadenAsync(id, abbruch);
        if (!wer.IstAutomatik)
        {
            Pruefen(KannErledigen(wer, a));
        }

        return a;
    }

    public async Task<Aufgabe> LadenAsync(Guid id, CancellationToken abbruch = default)
        => await speicher.LadenAsync(id, abbruch) ?? throw new InvalidOperationException("Aufgabe nicht gefunden.");

    /// <summary>
    /// Bezug im Protokoll: Aufgabentyp und Kurzkennung statt Titel – Titel können Namen von Personen enthalten,
    /// das Protokoll arbeitet nur mit Pseudonymen.
    /// </summary>
    public static string Bezug(Aufgabe a, string typName)
    {
        ArgumentNullException.ThrowIfNull(a);
        return $"„{typName}“ [A-{a.Id.ToString("N")[..8]}]";
    }

    private async Task<string> BezugAsync(Aufgabe a, CancellationToken abbruch)
        => Bezug(a, (await TypenAsync(abbruch)).FirstOrDefault(t => t.Schluessel == a.Typ)?.Name ?? a.Typ);

    private static void Pruefen(Freigabe f)
    {
        if (!f.Erlaubt)
        {
            throw new InvalidOperationException(f.Begruendung);
        }
    }
}
