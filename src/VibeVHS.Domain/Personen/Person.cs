using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Personen;

/// <summary>
/// Eine natürliche Person (Teilnehmende, Dozierende, Mitarbeitende). Jede Person gibt es nur einmal.
/// Alle Stammdaten sind bitemporal versioniert (Name, Geburtsdatum, Sprache, Rollen, Adressen, Rufnummern, E-Mails).
/// <see cref="Geburtsdatum"/>, <see cref="Sprache"/>, <see cref="Rollen"/> und <see cref="EMail"/> sind ein
/// Zwischenstand für Listen und Suche (Stand bei der letzten Änderung); maßgeblich ist <see cref="StandAm"/>.
/// </summary>
public sealed class Person
{
    private readonly List<PersonName> _namen = [];
    private readonly List<PersonMerkmal> _merkmale = [];
    private readonly List<Postadresse> _adressen = [];
    private readonly List<AdressKennzeichen> _kennzeichen = [];
    private readonly List<Rufnummer> _rufnummern = [];
    private readonly List<EMailAdresse> _emails = [];

    private Person()
    {
    }

    public Guid Id { get; private set; }

    public DateOnly? Geburtsdatum { get; private set; }

    /// <summary>Bevorzugte E-Mail-Adresse (Zwischenstand).</summary>
    public string? EMail { get; private set; }

    /// <summary>Bevorzugte Sprache für Korrespondenz (ISO 639-1, z. B. "de", "tr", "uk").</summary>
    public string Sprache { get; private set; } = "de";

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Akademischer Grad oder Titel, z. B. "Dr." (nicht Teil des versionierten Namens).</summary>
    public string? Titel { get; private set; }

    public Personenrolle Rollen { get; private set; }

    /// <summary>Anmeldename, wenn die Person ein Benutzerkonto hat (Mitarbeitende).</summary>
    public string? Benutzerkennung { get; private set; }

    public IReadOnlyCollection<PersonName> Namen => _namen;

    public IReadOnlyCollection<PersonMerkmal> Merkmale => _merkmale;

    public IReadOnlyCollection<Postadresse> Adressen => _adressen;

    public IReadOnlyCollection<AdressKennzeichen> AdressKennzeichen => _kennzeichen;

    public IReadOnlyCollection<Rufnummer> Rufnummern => _rufnummern;

    public IReadOnlyCollection<EMailAdresse> EMailAdressen => _emails;

    /// <summary>Alle Versionen aller Stammdaten für die Datenhistorie.</summary>
    public IEnumerable<IHistorienwert> AlleVersionen =>
        _namen.Cast<IHistorienwert>().Concat(_merkmale).Concat(_adressen).Concat(_kennzeichen).Concat(_rufnummern).Concat(_emails);

    public static Person Anlegen(string vorname, string nachname, DateOnly nameGueltigAb, DateTimeOffset jetzt,
        DateOnly? geburtsdatum = null, string? eMail = null, string sprache = "de", string? von = null)
    {
        var person = new Person { Id = Ids.Neu(), AngelegtAm = jetzt };
        var erst = Erfassung.Erst(jetzt, von);
        person._namen.Add(new PersonName(vorname, nachname, nameGueltigAb, null, erst));
        person._merkmale.Add(new PersonMerkmal(MerkmalArt.Sprache, NormierteSprache(sprache), nameGueltigAb, null, erst));
        if (geburtsdatum is { } g)
        {
            person._merkmale.Add(new PersonMerkmal(MerkmalArt.Geburtsdatum, PersonMerkmal.DatumText(g), DateOnly.MinValue, null, erst));
        }

        if (!string.IsNullOrWhiteSpace(eMail))
        {
            person._emails.Add(new EMailAdresse(Ids.Neu(), EMailArt.Privat, eMail, true, nameGueltigAb, null, erst));
        }

        person.ZwischenstandAktualisieren(jetzt);
        return person;
    }

    // ---------- Stichtag ----------

    /// <summary>Der gesamte Datensatz, wie er am <paramref name="gueltigAm"/> galt, nach dem Wissensstand vom <paramref name="bekanntAm"/>.</summary>
    public PersonenStand StandAm(DateOnly gueltigAm, DateTimeOffset bekanntAm)
    {
        PersonMerkmal? Merkmal(MerkmalArt art) => _merkmale.Where(m => m.Art == art).AlsStichtag(gueltigAm, bekanntAm);
        AdressKennzeichen? Kennzeichen(AdressKennzeichenArt art) => _kennzeichen.Where(k => k.Art == art).AlsStichtag(gueltigAm, bekanntAm);

        return new PersonenStand(
            gueltigAm,
            bekanntAm,
            _namen.AlsStichtag(gueltigAm, bekanntAm),
            Titel,
            Merkmal(MerkmalArt.Geburtsdatum)?.AlsDatum(),
            Merkmal(MerkmalArt.Sprache)?.Inhalt,
            Merkmal(MerkmalArt.Rollen)?.AlsRollen() ?? Personenrolle.Keine,
            _adressen.AlleAmStichtag(gueltigAm, bekanntAm),
            Kennzeichen(AdressKennzeichenArt.UeblicherAufenthaltsort)?.AdressId,
            Kennzeichen(AdressKennzeichenArt.Korrespondenzadresse)?.AdressId,
            _rufnummern.AlleAmStichtag(gueltigAm, bekanntAm),
            _emails.AlleAmStichtag(gueltigAm, bekanntAm));
    }

    /// <summary>Name, der am <paramref name="gueltigAm"/> galt, nach dem Wissensstand vom <paramref name="bekanntAm"/>.</summary>
    public PersonName? NameAm(DateOnly gueltigAm, DateTimeOffset bekanntAm) => _namen.AlsStichtag(gueltigAm, bekanntAm);

    /// <summary>Heute gültiger Name nach aktuellem Wissensstand (falls heute keiner gilt: der zuletzt gültige).</summary>
    public PersonName AktuellerName(DateTimeOffset jetzt)
        => NameAm(DateOnly.FromDateTime(jetzt.Date), jetzt)
           ?? _namen.AktuellerVerlauf().LastOrDefault()
           ?? throw new InvalidOperationException("Person ohne Namen.");

    // ---------- Name ----------

    /// <summary>Namensänderung ab einem Datum (z. B. Heirat). Ältere Dokumente behalten den alten Namen.</summary>
    public PersonName NameAendernAb(string vorname, string nachname, DateOnly ab, string grund, DateTimeOffset jetzt, string? von = null)
    {
        var neu = new PersonName(vorname, nachname, ab, null, Erfassung.Aenderung(jetzt, grund, von));
        ZeitlicheHistorie.AendernAb(_namen, neu);
        return neu;
    }

    /// <summary>Korrektur eines falsch erfassten Namens für dessen gesamten Gültigkeitszeitraum.</summary>
    public PersonName NameKorrigieren(string vorname, string nachname, DateOnly betroffenAm, string grund, DateTimeOffset jetzt, string? von = null)
        => ZeitlicheHistorie.Korrigieren(_namen, betroffenAm,
            (gv, gb) => new PersonName(vorname, nachname, gv, gb, Erfassung.Korrektur(jetzt, grund, von)), jetzt);

    public void TitelSetzen(string? titel) => Titel = string.IsNullOrWhiteSpace(titel) ? null : titel.Trim();

    // ---------- Merkmale ----------

    public void GeburtsdatumKorrigieren(DateOnly geburtsdatum, Erfassung erfassung)
    {
        var inhalt = PersonMerkmal.DatumText(geburtsdatum);
        if (_merkmale.Any(m => m.Art == MerkmalArt.Geburtsdatum && m.IstAktuellerWissensstand))
        {
            ZeitlicheHistorie.Korrigieren(_merkmale, DateOnly.MinValue,
                (gv, gb) => new PersonMerkmal(MerkmalArt.Geburtsdatum, inhalt, gv, gb, erfassung with { Art = Erfassungsart.Korrektur }),
                erfassung.Am, m => m.Art == MerkmalArt.Geburtsdatum);
        }
        else
        {
            _merkmale.Add(new PersonMerkmal(MerkmalArt.Geburtsdatum, inhalt, DateOnly.MinValue, null, erfassung with { Art = Erfassungsart.Ersterfassung }));
        }

        ZwischenstandAktualisieren(erfassung.Am);
    }

    public void SpracheAendernAb(string sprache, DateOnly ab, Erfassung erfassung)
    {
        ZeitlicheHistorie.AendernAb(_merkmale, new PersonMerkmal(MerkmalArt.Sprache, NormierteSprache(sprache), ab, null, erfassung));
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Fügt ab einem Datum eine Rolle hinzu (z. B. Mitarbeitende).</summary>
    public void RolleHinzufuegen(Personenrolle rolle, DateOnly ab, Erfassung erfassung)
    {
        var bisher = _merkmale.Where(m => m.Art == MerkmalArt.Rollen).AlsStichtag(ab, erfassung.Am)?.AlsRollen() ?? Personenrolle.Keine;
        if (bisher.HasFlag(rolle))
        {
            return;
        }

        var art = _merkmale.Any(m => m.Art == MerkmalArt.Rollen) ? erfassung : erfassung with { Art = Erfassungsart.Ersterfassung };
        ZeitlicheHistorie.AendernAb(_merkmale, new PersonMerkmal(MerkmalArt.Rollen, PersonMerkmal.RollenText(bisher | rolle), ab, null, art));
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Verknüpft die Person mit einem Benutzerkonto und macht sie zur Mitarbeitenden.</summary>
    public void AlsMitarbeitendeVerknuepfen(string benutzerkennung, DateOnly ab, Erfassung erfassung)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benutzerkennung);
        Benutzerkennung = benutzerkennung.Trim();
        RolleHinzufuegen(Personenrolle.Mitarbeitende, ab, erfassung);
    }

    // ---------- Adressen ----------

    public Guid AdresseHinzufuegen(string? zusatz, string strasse, string postleitzahl, string ort, DateOnly ab, Erfassung erfassung,
        string land = "DE")
    {
        var id = Ids.Neu();
        _adressen.Add(new Postadresse(id, zusatz, strasse, postleitzahl, ort, land, ab, null, erfassung));
        return id;
    }

    public void AdresseKorrigieren(Guid adressId, string? zusatz, string strasse, string postleitzahl, string ort, DateOnly betroffenAm,
        Erfassung erfassung, string land = "DE")
    {
        ZeitlicheHistorie.Korrigieren(_adressen, betroffenAm,
            (gv, gb) => new Postadresse(adressId, zusatz, strasse, postleitzahl, ort, land, gv, gb, erfassung with { Art = Erfassungsart.Korrektur }),
            erfassung.Am, a => a.AdressId == adressId);
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Die Adresse gilt ab <paramref name="ab"/> nicht mehr (z. B. nach Umzug).</summary>
    public void AdresseAufgebenAb(Guid adressId, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = AktuelleVersion(_adressen, a => a.AdressId == adressId, ab, erfassung.Am, "Adresse");
        ZeitlicheHistorie.BeendenAb(_adressen, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }

    /// <summary>Setzt ab einem Datum, welche Adresse üblicher Aufenthaltsort bzw. Korrespondenzadresse ist.</summary>
    public void AdressKennzeichnenAb(AdressKennzeichenArt art, Guid adressId, DateOnly ab, Erfassung erfassung)
    {
        var adresse = AktuelleVersion(_adressen, a => a.AdressId == adressId, ab, erfassung.Am, "Adresse");
        var erstes = !_kennzeichen.Any(k => k.Art == art);
        ZeitlicheHistorie.AendernAb(_kennzeichen, new AdressKennzeichen(art, adressId, adresse.Anzeige, ab, null,
            erstes ? erfassung with { Art = Erfassungsart.Ersterfassung } : erfassung));
    }

    /// <summary>Umzug: neue Adresse ab Datum, alte Adresse aufgeben, Aufenthaltsort umstellen (Korrespondenz bleibt, wie sie ist).</summary>
    public Guid UmzugAb(Guid alteAdressId, string? zusatz, string strasse, string postleitzahl, string ort, DateOnly ab, Erfassung erfassung)
    {
        var neu = AdresseHinzufuegen(zusatz, strasse, postleitzahl, ort, ab, erfassung with { Art = Erfassungsart.AenderungAbDatum });
        AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, neu, ab, erfassung);
        AdresseAufgebenAb(alteAdressId, ab, erfassung);
        return neu;
    }

    // ---------- Rufnummern und E-Mail ----------

    public Guid RufnummerHinzufuegen(RufnummerArt art, string nummer, Messagingdienst dienste, bool einwilligung, bool bevorzugt,
        DateOnly ab, Erfassung erfassung)
    {
        var id = Ids.Neu();
        _rufnummern.Add(new Rufnummer(id, art, nummer, dienste, einwilligung, bevorzugt, ab, null, erfassung));
        return id;
    }

    /// <summary>Neue Angaben zu einer Nummer ab Datum (z. B. Einwilligung für Signal erteilt).</summary>
    public void RufnummerAendernAb(Guid nummerId, RufnummerArt art, string nummer, Messagingdienst dienste, bool einwilligung, bool bevorzugt,
        DateOnly ab, Erfassung erfassung)
        => ZeitlicheHistorie.AendernAb(_rufnummern, new Rufnummer(nummerId, art, nummer, dienste, einwilligung, bevorzugt, ab, null, erfassung));

    public void RufnummerAbmeldenAb(Guid nummerId, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = AktuelleVersion(_rufnummern, r => r.NummerId == nummerId, ab, erfassung.Am, "Rufnummer");
        ZeitlicheHistorie.BeendenAb(_rufnummern, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }

    public Guid EMailHinzufuegen(EMailArt art, string adresse, bool bevorzugt, DateOnly ab, Erfassung erfassung)
    {
        var id = Ids.Neu();
        _emails.Add(new EMailAdresse(id, art, adresse, bevorzugt, ab, null, erfassung));
        ZwischenstandAktualisieren(erfassung.Am);
        return id;
    }

    public void EMailAufgebenAb(Guid adressId, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = AktuelleVersion(_emails, e => e.AdressId == adressId, ab, erfassung.Am, "E-Mail-Adresse");
        ZeitlicheHistorie.BeendenAb(_emails, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
        ZwischenstandAktualisieren(erfassung.Am);
    }

    // ---------- intern ----------

    private static T AktuelleVersion<T>(IEnumerable<T> versionen, Func<T, bool> betrifft, DateOnly ab, DateTimeOffset jetzt, string was)
        where T : BitemporaleVersion<T>
    {
        var eigene = versionen.Where(betrifft).ToList();
        return eigene.AlsStichtag(ab, jetzt)
            ?? eigene.AlsStichtag(ab.AddDays(-1), jetzt)
            ?? throw new InvalidOperationException($"{was} ist am {ab:dd.MM.yyyy} nicht gültig.");
    }

    private static string NormierteSprache(string? sprache) => string.IsNullOrWhiteSpace(sprache) ? "de" : sprache.Trim().ToLowerInvariant();

    private void ZwischenstandAktualisieren(DateTimeOffset jetzt)
    {
        var stand = StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt);
        Geburtsdatum = stand.Geburtsdatum;
        Sprache = stand.Sprache ?? "de";
        Rollen = stand.Rollen;
        EMail = (stand.EMails.FirstOrDefault(e => e.Bevorzugt) ?? stand.EMails.FirstOrDefault())?.Adresse;
    }
}

/// <summary>Der gesamte Datensatz einer Person zu einem Stichtag und Wissensstand.</summary>
public sealed record PersonenStand(
    DateOnly GueltigAm,
    DateTimeOffset BekanntAm,
    PersonName? Name,
    string? Titel,
    DateOnly? Geburtsdatum,
    string? Sprache,
    Personenrolle Rollen,
    IReadOnlyList<Postadresse> Adressen,
    Guid? AufenthaltsortId,
    Guid? KorrespondenzadresseId,
    IReadOnlyList<Rufnummer> Rufnummern,
    IReadOnlyList<EMailAdresse> EMails)
{
    public string NameMitTitel => Name is null ? "(unbekannt)" : string.IsNullOrEmpty(Titel) ? Name.Anzeige : $"{Titel} {Name.Anzeige}";

    public Postadresse? Aufenthaltsort => Adressen.FirstOrDefault(a => a.AdressId == AufenthaltsortId);

    public Postadresse? Korrespondenzadresse => Adressen.FirstOrDefault(a => a.AdressId == KorrespondenzadresseId);

    /// <summary>Wohin Briefe gehen: Korrespondenzadresse, sonst üblicher Aufenthaltsort.</summary>
    public Postadresse? BriefAn => Korrespondenzadresse ?? Aufenthaltsort;
}
