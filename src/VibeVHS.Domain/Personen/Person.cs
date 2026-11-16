using Datenschutz_ = VibeVHS.Domain.Datenschutz;
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
    private readonly List<Organisationszugehoerigkeit> _organisationen = [];
    private readonly List<Buchungsvollmacht> _vollmachten = [];
    private readonly List<Einwilligung> _einwilligungen = [];
    private readonly List<PersonQualifikation> _qualifikationen = [];

    private Person()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Geburtsdatum nach heutigem Stand (aus dem Merkmal; keine Spalte).</summary>
    public DateOnly? Geburtsdatum => IstAnonymisiert ? null : Heute.Geburtsdatum;

    /// <summary>Bevorzugte E-Mail-Adresse (Zwischenstand).</summary>
    /// <summary>Bevorzugte E-Mail-Adresse nach heutigem Stand (aus den Versionen; keine Spalte).</summary>
    public string? EMail => IstAnonymisiert ? null
        : (Heute.EMails.Where(e => e.Adresse != "–").FirstOrDefault(e => e.Bevorzugt) ?? Heute.EMails.FirstOrDefault(e => e.Adresse != "–"))?.Adresse;

    /// <summary>Bevorzugte Sprache für Korrespondenz (ISO 639-1, z. B. "de", "tr", "uk").</summary>
    public string Sprache => IstAnonymisiert ? string.Empty : Heute.Sprache ?? "de";

    public DateTimeOffset AngelegtAm { get; private set; }

    /// <summary>Titel nach heutigem Stand (Merkmal <see cref="MerkmalArt.Titel"/>; keine Spalte).</summary>
    public string? Titel => IstAnonymisiert ? null : Heute.Titel;

    /// <summary>Rollen nach heutigem Stand (Merkmal <see cref="MerkmalArt.Rollen"/>; keine Spalte).</summary>
    public Personenrolle Rollen => Heute.Rollen;

    public IReadOnlyCollection<PersonName> Namen => _namen;

    public IReadOnlyCollection<PersonMerkmal> Merkmale => _merkmale;

    public IReadOnlyCollection<Postadresse> Adressen => _adressen;

    public IReadOnlyCollection<AdressKennzeichen> AdressKennzeichen
    {
        get
        {
            // Anzeige aus der Adresse selbst (gültig zum Beginn des Kennzeichens), keine gespeicherte Kopie.
            foreach (var k in _kennzeichen)
            {
                var adresse = _adressen.Where(a => a.AdressId == k.AdressId && a.IstAktuellerWissensstand && !a.IstEndemarke)
                    .OrderBy(a => a.GueltigVon > k.GueltigVon ? 1 : 0).ThenByDescending(a => a.GueltigVon).FirstOrDefault();
                if (adresse is not null)
                {
                    k.AnzeigeSetzen(adresse.Anzeige);
                }
            }

            return _kennzeichen;
        }
    }

    public IReadOnlyCollection<Rufnummer> Rufnummern => _rufnummern;

    public IReadOnlyCollection<EMailAdresse> EMailAdressen => _emails;

    public IReadOnlyCollection<Organisationszugehoerigkeit> Organisationen => _organisationen;

    public IReadOnlyCollection<Buchungsvollmacht> Vollmachten => _vollmachten;

    public IReadOnlyCollection<Einwilligung> Einwilligungen => _einwilligungen;

    public IReadOnlyCollection<PersonQualifikation> Qualifikationen => _qualifikationen;

    public Personenstatus Status { get; private set; }

    /// <summary>Fachbereiche, denen die Person zugeordnet ist (v. a. Dozierende).</summary>
    /// <remarks>Gespeichert als eigene Tabelle (je Eintrag eine Zeile, siehe <see cref="Gemeinsam.Listenwert{T}"/>).</remarks>
    public List<int> Fachbereiche
    {
        get => Gemeinsam.Listenwerte.Werte(_fachbereiche);
        private set => _fachbereiche = Gemeinsam.Listenwerte.Von(value);
    }

    private List<Gemeinsam.Listenwert<int>> _fachbereiche = [];

    /// <summary>„Titel Vorname Vorsatz Nachname Zusatz“ nach heutigem Stand (keine Spalte).</summary>
    public string Anzeigename => IstAnonymisiert
        ? $"Gelöschte Person {Datenschutz_.Pseudonym.Kurz(Id)} · anonymisiert am {AnonymisiertAm?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)}"
        : HeutigerName is { } n ? (string.IsNullOrEmpty(Titel) ? n.Anzeige : $"{Titel} {n.Anzeige}") : "(ohne Namen)";

    /// <summary>Sortierform des heutigen Namens (DIN 5007-2, normalisiert).</summary>
    public string Sortiername => HeutigerName?.Sortierschluessel ?? string.Empty;


    /// <summary>Ort des üblichen Aufenthalts nach heutigem Stand (nach der Anonymisierung: das grobe Gebiet).</summary>
    public string? Wohnort => IstAnonymisiert ? AnonymisiertGebiet : Heute.Aufenthaltsort?.Ort;

    private DateTimeOffset _stichzeit;
    private PersonenStand? _heute;

    /// <summary>
    /// Bezugszeitpunkt der Lese-Eigenschaften (Anzeigename, Rollen, E-Mail …): beim Laden aus der Datenbank „jetzt“ der Anwendungsuhr,
    /// nach jeder Änderung deren Zeitpunkt. Alles wird aus den Versionen berechnet – es gibt keine Kopien, die veralten könnten.
    /// </summary>
    public DateTimeOffset Stichzeit => _stichzeit == default ? DateTimeOffset.Now : _stichzeit;

    /// <summary>Bezugszeitpunkt setzen (z. B. beim Laden oder für einen Stichtag).</summary>
    public void StichzeitSetzen(DateTimeOffset jetzt)
    {
        _stichzeit = jetzt;
        _heute = null;
    }

    private PersonenStand Heute
    {
        get
        {
            if (_heute is null)
            {
                // Rückdatierte Erfassungen (Datenübernahme) zählen mit: maßgeblich ist der jüngste bekannte Stand.
                var juengste = AlleVersionen.Select(v => v.ErfasstAm).DefaultIfEmpty(Stichzeit).Max();
                var bekannt = juengste > Stichzeit ? juengste : Stichzeit;
                _heute = StandAm(DateOnly.FromDateTime(Stichzeit.Date), bekannt);
            }

            return _heute;
        }
    }

    private PersonName? HeutigerName => Heute.Name ?? _namen.AktuellerVerlauf().LastOrDefault();

    /// <summary>Gesetzt, wenn dieser Datensatz als Dublette in einen anderen überführt wurde.</summary>
    public Guid? ZusammengefuehrtIn { get; private set; }

    /// <summary>Alle Versionen aller Stammdaten für die Datenhistorie.</summary>
    public IEnumerable<IHistorienwert> AlleVersionen =>
        _namen.Cast<IHistorienwert>().Concat(_merkmale).Concat(_adressen).Concat(_kennzeichen).Concat(_rufnummern).Concat(_emails)
            .Concat(_organisationen).Concat(_vollmachten).Concat(_einwilligungen).Concat(_qualifikationen);

    public static Person Anlegen(string vorname, string nachname, DateOnly nameGueltigAb, DateTimeOffset jetzt,
        DateOnly? geburtsdatum = null, string? eMail = null, string sprache = "de", string? von = null, string? vorsatz = null, string? zusatz = null)
    {
        var person = new Person { Id = Ids.Neu(), AngelegtAm = jetzt };
        var erst = Erfassung.Erst(jetzt, von);
        person._namen.Add(new PersonName(vorname, nachname, nameGueltigAb, null, erst, vorsatz, zusatz));
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
            Leer(Merkmal(MerkmalArt.Titel)?.Inhalt),
            Merkmal(MerkmalArt.Geburtsdatum)?.AlsDatum(),
            Merkmal(MerkmalArt.Sprache)?.Inhalt,
            Merkmal(MerkmalArt.Rollen)?.AlsRollen() ?? Personenrolle.Keine,
            _adressen.AlleAmStichtag(gueltigAm, bekanntAm),
            Kennzeichen(AdressKennzeichenArt.UeblicherAufenthaltsort)?.AdressId,
            Kennzeichen(AdressKennzeichenArt.Korrespondenzadresse)?.AdressId,
            _rufnummern.AlleAmStichtag(gueltigAm, bekanntAm),
            _emails.AlleAmStichtag(gueltigAm, bekanntAm),
            _organisationen.AlleAmStichtag(gueltigAm, bekanntAm),
            _vollmachten.AlleAmStichtag(gueltigAm, bekanntAm),
            _einwilligungen.AlleAmStichtag(gueltigAm, bekanntAm),
            Merkmal(MerkmalArt.Beschaeftigung)?.AlsBeschaeftigung(),
            _qualifikationen.AlleAmStichtag(gueltigAm, bekanntAm),
            Leer(Merkmal(MerkmalArt.Anrede)?.Inhalt),
            Leer(Merkmal(MerkmalArt.Geschlecht)?.Inhalt),
            Leer(Merkmal(MerkmalArt.Geburtsname)?.Inhalt),
            Personen.Ansprache.Lesen(Merkmal(MerkmalArt.Ansprache)?.Inhalt));

        static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }

    /// <summary>Briefanrede nach heutigem Stand (Anrede, Titel, Name), z. B. „Sehr geehrte Frau Dr. Muster“.</summary>
    public string Briefanrede(DateTimeOffset jetzt, Anredestil stil = Anredestil.Foermlich)
        => StandAm(DateOnly.FromDateTime(jetzt.Date), jetzt).Briefanrede(stil);

    // ---------- Dozierende: Beschäftigungsart und Qualifikationen ----------

    /// <summary>Beschäftigungsart ab einem Datum (z. B. Wechsel von freiberuflich zu festangestellt). Ältere Einsätze behalten die damalige Art.</summary>
    public void BeschaeftigungAendernAb(Beschaeftigungsart art, DateOnly ab, Erfassung erfassung)
    {
        var erste = !_merkmale.Any(m => m.Art == MerkmalArt.Beschaeftigung);
        ZeitlicheHistorie.AendernAb(_merkmale, new PersonMerkmal(MerkmalArt.Beschaeftigung, art.ToString(), ab, null,
            erste && erfassung.Art == Erfassungsart.AenderungAbDatum ? erfassung with { Art = Erfassungsart.Ersterfassung } : erfassung));
    }

    public Beschaeftigungsart? BeschaeftigungAm(DateOnly tag, DateTimeOffset bekanntAm)
        => _merkmale.Where(m => m.Art == MerkmalArt.Beschaeftigung).AlsStichtag(tag, bekanntAm)?.AlsBeschaeftigung();

    /// <summary>Qualifikation eintragen oder erneuern, gültig von <paramref name="ab"/> bis einschließlich <paramref name="bisEinschliesslich"/>.</summary>
    public PersonQualifikation QualifikationSetzenAb(Programm.Merkmal merkmal, string? wert, string? nachweis, string? ausstellendeStelle,
        DateOnly ab, DateOnly? bisEinschliesslich, Erfassung erfassung)
    {
        ArgumentNullException.ThrowIfNull(merkmal);
        if (merkmal.Bezug != Programm.MerkmalBezug.Person)
        {
            throw new ArgumentException($"„{merkmal.Name}“ ist ein Merkmal für Orte, nicht für Personen.");
        }

        if (merkmal.NachweisPflicht && string.IsNullOrWhiteSpace(nachweis))
        {
            throw new ArgumentException($"Für „{merkmal.Name}“ ist ein Nachweis Pflicht (Dokument oder Aktenzeichen).");
        }

        wert = merkmal.WertPruefen(wert);

        if (bisEinschliesslich is { } b && b < ab)
        {
            throw new ArgumentException("Das Ende der Gültigkeit liegt vor dem Beginn.");
        }

        var erste = !_qualifikationen.Any(q => q.Schluessel == merkmal.Schluessel);

        var neu = new PersonQualifikation(merkmal.Schluessel, merkmal.Name, wert, nachweis, ausstellendeStelle, ab, bisEinschliesslich?.AddDays(1),
            erste && erfassung.Art == Erfassungsart.AenderungAbDatum ? erfassung with { Art = Erfassungsart.Ersterfassung } : erfassung);
        ZeitlicheHistorie.AendernAb(_qualifikationen, neu);
        return neu;
    }

    /// <summary>Qualifikation ab einem Datum beenden (z. B. Zulassung widerrufen).</summary>
    public void QualifikationBeendenAb(string schluessel, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = _qualifikationen.Where(q => q.Schluessel == schluessel).AlsStichtag(ab, erfassung.Am)
            ?? throw new InvalidOperationException("Diese Qualifikation gilt am angegebenen Tag nicht.");
        ZeitlicheHistorie.BeendenAb(_qualifikationen, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }

    public PersonQualifikation? QualifikationAm(string schluessel, DateOnly tag, DateTimeOffset bekanntAm)
        => _qualifikationen.Where(q => q.Schluessel == schluessel).AlsStichtag(tag, bekanntAm);

    /// <summary>Name, der am <paramref name="gueltigAm"/> galt, nach dem Wissensstand vom <paramref name="bekanntAm"/>.</summary>
    public PersonName? NameAm(DateOnly gueltigAm, DateTimeOffset bekanntAm) => _namen.AlsStichtag(gueltigAm, bekanntAm);

    /// <summary>Heute gültiger Name nach aktuellem Wissensstand (falls heute keiner gilt: der zuletzt gültige).</summary>
    public PersonName AktuellerName(DateTimeOffset jetzt)
        => NameAm(DateOnly.FromDateTime(jetzt.Date), jetzt)
           ?? _namen.AktuellerVerlauf().LastOrDefault()
           ?? throw new InvalidOperationException("Person ohne Namen.");

    // ---------- Name ----------

    /// <summary>Namensänderung ab einem Datum (z. B. Heirat). Ältere Dokumente behalten den alten Namen.</summary>
    public PersonName NameAendernAb(string vorname, string nachname, DateOnly ab, string grund, DateTimeOffset jetzt, string? von = null,
        string? vorsatz = null, string? zusatz = null)
    {
        var neu = new PersonName(vorname, nachname, ab, null, Erfassung.Aenderung(jetzt, grund, von), vorsatz, zusatz);
        ZeitlicheHistorie.AendernAb(_namen, neu);
        ZwischenstandAktualisieren(jetzt);
        return neu;
    }

    /// <summary>Korrektur eines falsch erfassten Namens für dessen gesamten Gültigkeitszeitraum.</summary>
    public PersonName NameKorrigieren(string vorname, string nachname, DateOnly betroffenAm, string grund, DateTimeOffset jetzt, string? von = null,
        string? vorsatz = null, string? zusatz = null)
    {
        var neu = ZeitlicheHistorie.Korrigieren(_namen, betroffenAm,
            (gv, gb) => new PersonName(vorname, nachname, gv, gb, Erfassung.Korrektur(jetzt, grund, von), vorsatz, zusatz), jetzt);
        ZwischenstandAktualisieren(jetzt);
        return neu;
    }

    /// <summary>
    /// Titel bei der Ersterfassung (Anlegen, Import, Webshop): gilt ab Beginn des ersten Namens. Spätere Änderungen laufen bitemporal
    /// über <see cref="AngabeAendern"/> mit <see cref="MerkmalArt.Titel"/> („Änderung ab Datum“ oder „Korrektur“).
    /// </summary>
    public void TitelSetzen(string? titel)
    {
        if (string.IsNullOrWhiteSpace(titel))
        {
            return;
        }

        if (_merkmale.Any(m => m.Art == MerkmalArt.Titel))
        {
            throw new InvalidOperationException("Der Titel ist schon erfasst – Änderungen bitte als „Änderung ab Datum“ oder „Korrektur“.");
        }

        var n = _namen.OrderBy(x => x.GueltigVon).First();
        AngabeAendern(MerkmalArt.Titel, titel, n.GueltigVon, Erfassung.Erst(n.ErfasstAm, n.ErfasstVon), korrektur: false);
    }

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

    /// <summary>Anrede, Geschlecht oder Geburtsname: Änderung ab Datum oder Korrektur der am Datum gültigen Angabe (leer = keine Angabe).</summary>
    public void AngabeAendern(MerkmalArt art, string? wert, DateOnly datum, Erfassung erfassung, bool korrektur)
    {
        if (art is not (MerkmalArt.Anrede or MerkmalArt.Geschlecht or MerkmalArt.Geburtsname or MerkmalArt.Ansprache or MerkmalArt.Titel))
        {
            throw new ArgumentException("Nur Anrede, Geschlecht, Geburtsname, Ansprache und Titel sind freie Angaben.", nameof(art));
        }

        var inhalt = wert?.Trim() ?? string.Empty;
        if (!_merkmale.Any(m => m.Art == art))
        {
            _merkmale.Add(new PersonMerkmal(art, inhalt, korrektur ? DateOnly.MinValue : datum, null, erfassung with { Art = Erfassungsart.Ersterfassung }));
        }
        else if (korrektur)
        {
            ZeitlicheHistorie.Korrigieren(_merkmale, datum, (gv, gb) => new PersonMerkmal(art, inhalt, gv, gb, erfassung with { Art = Erfassungsart.Korrektur }),
                erfassung.Am, m => m.Art == art);
        }
        else
        {
            ZeitlicheHistorie.AendernAb(_merkmale, new PersonMerkmal(art, inhalt, datum, null, erfassung));
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

    /// <summary>Beendet eine Rolle: ab <paramref name="ab"/> (dem Tag nach dem letzten Tag) gilt sie nicht mehr; die Historie bleibt.</summary>
    public void RolleBeendenAb(Personenrolle rolle, DateOnly ab, Erfassung erfassung)
    {
        var bisher = RollenAm(ab, erfassung.Am);
        if (!bisher.HasFlag(rolle))
        {
            throw new InvalidOperationException($"Die Rolle „{PersonMerkmal.RollenAnzeige(rolle)}“ besteht am {ab:dd.MM.yyyy} nicht.");
        }

        ZeitlicheHistorie.AendernAb(_merkmale, new PersonMerkmal(MerkmalArt.Rollen, PersonMerkmal.RollenText(bisher & ~rolle), ab, null, erfassung));
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Rollen am Stichtag (Wissensstand <paramref name="bekanntAm"/>).</summary>
    public Personenrolle RollenAm(DateOnly tag, DateTimeOffset bekanntAm)
        => _merkmale.Where(m => m.Art == MerkmalArt.Rollen).AlsStichtag(tag, bekanntAm)?.AlsRollen() ?? Personenrolle.Keine;

    /// <summary>Macht die Person zur Mitarbeitenden (die Verknüpfung zum Benutzerkonto steht nur am Konto: Benutzerkonten.PersonId).</summary>
    public void AlsMitarbeitendeVerknuepfen(DateOnly ab, Erfassung erfassung) => RolleHinzufuegen(Personenrolle.Mitarbeitende, ab, erfassung);

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
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Umzug: neue Adresse ab Datum, alte Adresse aufgeben, Aufenthaltsort umstellen (Korrespondenz bleibt, wie sie ist).</summary>
    public Guid UmzugAb(Guid alteAdressId, string? zusatz, string strasse, string postleitzahl, string ort, DateOnly ab, Erfassung erfassung)
    {
        var neu = AdresseHinzufuegen(zusatz, strasse, postleitzahl, ort, ab, erfassung with { Art = Erfassungsart.AenderungAbDatum });
        AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, neu, ab, erfassung);
        AdresseAufgebenAb(alteAdressId, ab, erfassung);
        return neu;
    }

    // ---------- Gültigkeit korrigieren (Datum war falsch erfasst) ----------

    /// <summary>
    /// Umzugsdatum korrigieren: Die neue Adresse gilt ab <paramref name="neuesDatum"/> statt ab ihrem bisherigen Beginn; die vorherige
    /// Adresse (die zu diesem Datum aufgegeben wurde) und das Kennzeichen „üblicher Aufenthaltsort“ werden mit korrigiert.
    /// </summary>
    public void UmzugsdatumKorrigieren(Guid neueAdressId, DateOnly neuesDatum, Erfassung erfassung)
    {
        var neu = _adressen.Where(a => a.AdressId == neueAdressId && a.IstAktuellerWissensstand && !a.IstEndemarke).OrderBy(a => a.GueltigVon).FirstOrDefault()
                  ?? throw new InvalidOperationException("Die Adresse ist nicht bekannt.");
        var alt = neu.GueltigVon;
        ZeitlicheHistorie.GrenzeKorrigieren(_adressen, a => a.AdressId == neueAdressId, alt, neuesDatum, erfassung);
        foreach (var vorher in _adressen.Where(a => a.IstAktuellerWissensstand && a.IstEndemarke && a.GueltigVon == alt && a.AdressId != neueAdressId)
                     .Select(a => a.AdressId).Distinct().ToList())
        {
            ZeitlicheHistorie.GrenzeKorrigieren(_adressen, a => a.AdressId == vorher, alt, neuesDatum, erfassung);
        }

        foreach (var art in _kennzeichen.Where(k => k.IstAktuellerWissensstand && k.AdressId == neueAdressId && k.GueltigVon == alt).Select(k => k.Art).Distinct().ToList())
        {
            ZeitlicheHistorie.GrenzeKorrigieren(_kennzeichen, k => k.Art == art, alt, neuesDatum, erfassung);
        }

        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Datum einer Namensänderung korrigieren (z. B. Heirat war schon am 01.06.).</summary>
    public void NamensaenderungsdatumKorrigieren(DateOnly bisher, DateOnly neu, Erfassung erfassung)
    {
        ZeitlicheHistorie.GrenzeKorrigieren(_namen, _ => true, bisher, neu, erfassung);
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Datum einer Änderung einer Angabe (Merkmal, z. B. Titel, Rollen) korrigieren.</summary>
    public void AngabedatumKorrigieren(MerkmalArt art, DateOnly bisher, DateOnly neu, Erfassung erfassung)
    {
        ZeitlicheHistorie.GrenzeKorrigieren(_merkmale, m => m.Art == art, bisher, neu, erfassung);
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Datum eines Adresskennzeichens korrigieren (ab wann welche Adresse üblicher Aufenthaltsort bzw. Briefanschrift ist).</summary>
    public void KennzeichendatumKorrigieren(AdressKennzeichenArt art, DateOnly bisher, DateOnly neu, Erfassung erfassung)
    {
        ZeitlicheHistorie.GrenzeKorrigieren(_kennzeichen, k => k.Art == art, bisher, neu, erfassung);
        ZwischenstandAktualisieren(erfassung.Am);
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

    // ---------- Organisationen, Vollmachten, Einwilligungen ----------

    public void OrganisationZuordnenAb(Organisation organisation, string? funktion, DateOnly ab, Erfassung erfassung)
    {
        ArgumentNullException.ThrowIfNull(organisation);
        ZeitlicheHistorie.AendernAb(_organisationen, new Organisationszugehoerigkeit(organisation.Id, organisation.Name, funktion, ab, null, erfassung));
    }

    public void OrganisationVerlassenAb(Guid organisationId, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = AktuelleVersion(_organisationen, o => o.OrganisationId == organisationId, ab, erfassung.Am, "Zugehörigkeit");
        ZeitlicheHistorie.BeendenAb(_organisationen, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }

    /// <summary>Erteilt eine Buchungsvollmacht für den Zeitraum [von, bis).</summary>
    public Guid VollmachtErteilen(Bevollmaechtigtenart art, Guid bevollmaechtigterId, string bevollmaechtigterName, string nachweis,
        DateOnly von, DateOnly? bis, Erfassung erfassung)
    {
        if (art == Bevollmaechtigtenart.Person && bevollmaechtigterId == Id)
        {
            throw new InvalidOperationException("Eine Person braucht keine Vollmacht für sich selbst.");
        }

        var id = Ids.Neu();
        _vollmachten.Add(new Buchungsvollmacht(id, art, bevollmaechtigterId, bevollmaechtigterName, nachweis, von, bis, erfassung));
        return id;
    }

    public void VollmachtWiderrufenAb(Guid vollmachtId, DateOnly ab, Erfassung erfassung)
    {
        var aktuell = AktuelleVersion(_vollmachten, v => v.VollmachtId == vollmachtId, ab, erfassung.Am, "Vollmacht");
        ZeitlicheHistorie.BeendenAb(_vollmachten, aktuell.KopieMit(ab, null, erfassung with { Art = Erfassungsart.Beendet }));
    }

    /// <summary>Darf <paramref name="bevollmaechtigterId"/> diese Person am Stichtag buchen (nach Wissensstand <paramref name="bekanntAm"/>)?</summary>
    public bool DarfBuchen(Guid bevollmaechtigterId, DateOnly am, DateTimeOffset bekanntAm)
        => bevollmaechtigterId == Id
           || _vollmachten.AlleAmStichtag(am, bekanntAm).Any(v => v.BevollmaechtigterId == bevollmaechtigterId);

    public void EinwilligungSetzenAb(Kontaktkanal kanal, Kontaktzweck zweck, bool erteilt, DateOnly ab, Erfassung erfassung)
    {
        var erstes = !_einwilligungen.Any(e => e.Kanal == kanal && e.Zweck == zweck);
        ZeitlicheHistorie.AendernAb(_einwilligungen, new Einwilligung(kanal, zweck, erteilt, ab, null,
            erstes && erfassung.Art == Erfassungsart.AenderungAbDatum ? erfassung with { Art = Erfassungsart.Ersterfassung } : erfassung));
    }

    public bool EinwilligungAm(Kontaktkanal kanal, Kontaktzweck zweck, DateOnly am, DateTimeOffset bekanntAm)
        => _einwilligungen.Where(e => e.Kanal == kanal && e.Zweck == zweck).AlsStichtag(am, bekanntAm)?.Erteilt ?? false;

    // ---------- Datenschutz: archivieren, sperren, anonymisieren ----------

    public Datenschutz.Datenschutzstatus Datenschutz { get; private set; }

    public DateTimeOffset? ArchiviertAm { get; private set; }

    public DateTimeOffset? GesperrtAm { get; private set; }

    /// <summary>Ende der Aufbewahrungsfrist; danach wird anonymisiert.</summary>
    public DateOnly? GesperrtBis { get; private set; }

    public string? Sperrgrund { get; private set; }

    public DateTimeOffset? AnonymisiertAm { get; private set; }

    /// <summary>Statistikmerkmale, die nach der Anonymisierung bleiben.</summary>
    /// <summary>Nur nach der Anonymisierung: Geburtsjahr-Kohorte („1985–1989“) für die Altersstatistik zu jedem Stichtag.</summary>
    public string? AnonymisiertGeburtskohorte { get; private set; }

    /// <summary>Nur nach der Anonymisierung: grobes Gebiet (Kreis bzw. PLZ-Gebiet) statt der Adresse.</summary>
    public string? AnonymisiertGebiet { get; private set; }

    public bool IstAnonymisiert => Datenschutz == VibeVHS.Domain.Datenschutz.Datenschutzstatus.Anonymisiert;

    /// <summary>Archivieren: aus Standardlisten ausgeblendet, schreibgeschützt, rückgängig machbar.</summary>
    public void Archivieren(DateTimeOffset jetzt)
    {
        if (Datenschutz != VibeVHS.Domain.Datenschutz.Datenschutzstatus.Aktiv)
        {
            throw new InvalidOperationException($"Nur aktive Personen können archiviert werden (Status: {VibeVHS.Domain.Datenschutz.DatenschutzAnzeige.Text(Datenschutz)}).");
        }

        Datenschutz = VibeVHS.Domain.Datenschutz.Datenschutzstatus.Archiviert;
        ArchiviertAm = jetzt;
    }

    /// <summary>Archivierung oder Sperre aufheben (z. B. Person meldet sich wieder an, Sperrgrund entfallen).</summary>
    public void Reaktivieren()
    {
        if (IstAnonymisiert)
        {
            throw new InvalidOperationException("Anonymisierte Personen können nicht reaktiviert werden.");
        }

        Datenschutz = VibeVHS.Domain.Datenschutz.Datenschutzstatus.Aktiv;
        ArchiviertAm = null;
        GesperrtAm = null;
        GesperrtBis = null;
        Sperrgrund = null;
    }

    /// <summary>Sperre (Art. 18 DSGVO): Daten bleiben bis Fristende, Zugriff nur für berechtigte Rollen und protokolliert.</summary>
    public void Sperren(DateOnly bis, string grund, DateTimeOffset jetzt)
    {
        if (IstAnonymisiert)
        {
            throw new InvalidOperationException("Die Person ist bereits anonymisiert.");
        }

        Datenschutz = VibeVHS.Domain.Datenschutz.Datenschutzstatus.Gesperrt;
        GesperrtAm ??= jetzt;
        GesperrtBis = GesperrtBis is { } alt && alt > bis ? alt : bis;
        Sperrgrund = grund;
    }

    /// <summary>
    /// Kontaktdaten löschen, die für keine Aufbewahrungspflicht gebraucht werden (Rufnummern, E-Mail, Einwilligungsnachweise,
    /// Vollmachten). Name, Anschrift und Geburtsdatum bleiben für die laufende Frist.
    /// </summary>
    public void KontaktdatenLoeschen(DateTimeOffset jetzt)
    {
        foreach (var r in _rufnummern)
        {
            r.Anonymisieren();
            r.GrundAnonymisieren();
        }

        foreach (var e in _emails)
        {
            e.Anonymisieren();
            e.GrundAnonymisieren();
        }

        foreach (var v in _vollmachten)
        {
            v.Anonymisieren();
            v.GrundAnonymisieren();
        }

        foreach (var w in _einwilligungen)
        {
            w.GrundAnonymisieren();
        }

        ZwischenstandAktualisieren(jetzt);
    }

    /// <summary>
    /// Anonymisierung an Ort und Stelle: Die Zeile bleibt (andere Daten zeigen auf sie), alle Versionen werden überschrieben.
    /// Übrig bleiben ein eigener Platzhalter, Rollen und Statistikmerkmale (Geburtsjahr-Kohorte, Gebiet, letztes Geschlecht).
    /// </summary>
    public string Anonymisieren(DateTimeOffset jetzt)
    {
        if (IstAnonymisiert)
        {
            throw new InvalidOperationException("Die Person ist bereits anonymisiert.");
        }

        var heute = DateOnly.FromDateTime(jetzt.Date);
        var stand = StandAm(heute, jetzt);
        AnonymisiertGeburtskohorte = stand.Geburtsdatum is { } geb ? Datenschutz_.Statistik.Geburtskohorte(geb) : null;
        AnonymisiertGebiet = Datenschutz_.Statistik.Gebiet(stand.Aufenthaltsort?.Postleitzahl ?? _adressen.LastOrDefault()?.Postleitzahl);
        var kurz = Datenschutz_.Pseudonym.Kurz(Id);

        foreach (var n in _namen)
        {
            n.Anonymisieren("Gelöschte", $"Person {kurz}");
        }

        // Geschlecht bleibt für die Statistik, aber ohne Verlauf: alle Versionen tragen den letzten Wert (eine Änderung wäre identifizierend).
        var geschlecht = stand.Geschlecht;
        foreach (var m in _merkmale.Where(m => m.Art != MerkmalArt.Rollen))
        {
            if (m.Art == MerkmalArt.Geschlecht && geschlecht is not null)
            {
                m.InhaltErsetzen(geschlecht);
            }
            else
            {
                m.InhaltLeeren();
            }
        }

        foreach (var a in _adressen)
        {
            a.Anonymisieren();
        }

        foreach (var k in _kennzeichen)
        {
            k.Anonymisieren();
        }

        foreach (var o in _organisationen)
        {
            o.Anonymisieren();
        }

        foreach (var q in _qualifikationen)
        {
            q.Anonymisieren();
            q.GrundAnonymisieren();
        }

        KontaktdatenLoeschen(jetzt);
        foreach (var v in AlleVersionen.Cast<object>())
        {
            switch (v)
            {
                case PersonName x: x.GrundAnonymisieren(); break;
                case PersonMerkmal x: x.GrundAnonymisieren(); break;
                case Postadresse x: x.GrundAnonymisieren(); break;
                case AdressKennzeichen x: x.GrundAnonymisieren(); break;
                case Organisationszugehoerigkeit x: x.GrundAnonymisieren(); break;
            }
        }

        Datenschutz = VibeVHS.Domain.Datenschutz.Datenschutzstatus.Anonymisiert;
        AnonymisiertAm = jetzt;
        GesperrtBis = null;
        Sperrgrund = null;
        return Anzeigename;
    }

    /// <summary>Wurde ein Bevollmächtigter anonymisiert, erscheint auch in fremden Vollmachten nur noch sein Platzhalter.</summary>
    public void BevollmaechtigtenAnonymisieren(Guid bevollmaechtigterId, string platzhalter)
    {
        foreach (var v in _vollmachten.Where(v => v.BevollmaechtigterId == bevollmaechtigterId))
        {
            v.BevollmaechtigtenUmbenennen(platzhalter);
        }
    }

    // ---------- Status und Zuordnung ----------

    public void StatusSetzen(Personenstatus status) => Status = status;

    public void FachbereicheSetzen(IEnumerable<int> fachbereiche) => Fachbereiche = fachbereiche.Distinct().Order().ToList();

    /// <summary>
    /// Übernimmt die heute gültigen Kontaktdaten, Rollen, Organisationen und Vollmachten einer Dublette, soweit sie hier fehlen.
    /// Die Dublette selbst bleibt mit ihrer ganzen Historie erhalten und verweist auf diesen Datensatz.
    /// </summary>
    public void DubletteUebernehmen(Person dublette, Erfassung erfassung)
    {
        ArgumentNullException.ThrowIfNull(dublette);
        if (dublette.Id == Id)
        {
            throw new InvalidOperationException("Eine Person kann nicht mit sich selbst zusammengeführt werden.");
        }

        if (dublette.Status == Personenstatus.Zusammengefuehrt)
        {
            throw new InvalidOperationException("Diese Dublette wurde bereits zusammengeführt.");
        }

        var heute = DateOnly.FromDateTime(erfassung.Am.Date);
        var ihr = dublette.StandAm(heute, erfassung.Am);
        var mein = StandAm(heute, erfassung.Am);
        var e = erfassung with { Art = Erfassungsart.AenderungAbDatum };

        foreach (var a in ihr.Adressen.Where(a => !mein.Adressen.Any(m => Suchtext.Normalisieren(m.Anzeige) == Suchtext.Normalisieren(a.Anzeige))))
        {
            var neu = AdresseHinzufuegen(a.Zusatz, a.Strasse, a.Postleitzahl, a.Ort, heute, e, a.Land);
            if (mein.AufenthaltsortId is null && a.AdressId == ihr.AufenthaltsortId)
            {
                AdressKennzeichnenAb(AdressKennzeichenArt.UeblicherAufenthaltsort, neu, heute, e);
            }
        }

        foreach (var r in ihr.Rufnummern.Where(r => !mein.Rufnummern.Any(m => m.Nummer == r.Nummer)))
        {
            RufnummerHinzufuegen(r.Art, r.Nummer, r.Dienste, r.Einwilligung, false, heute, e);
        }

        foreach (var m in ihr.EMails.Where(m => !mein.EMails.Any(x => string.Equals(x.Adresse, m.Adresse, StringComparison.OrdinalIgnoreCase))))
        {
            EMailHinzufuegen(m.Art, m.Adresse, mein.EMails.Count == 0 && m.Bevorzugt, heute, e);
        }

        foreach (var v in ihr.Vollmachten)
        {
            _vollmachten.Add(new Buchungsvollmacht(Ids.Neu(), v.Art, v.BevollmaechtigterId, v.BevollmaechtigterName, v.Nachweis, heute, v.GueltigBis, e));
        }

        foreach (var o in ihr.Organisationen.Where(o => !mein.Organisationen.Any(m => m.OrganisationId == o.OrganisationId)))
        {
            _organisationen.Add(new Organisationszugehoerigkeit(o.OrganisationId, o.OrganisationName, o.Funktion, heute, o.GueltigBis, e));
        }

        foreach (Personenrolle rolle in Enum.GetValues<Personenrolle>().Where(r => r != Personenrolle.Keine && ihr.Rollen.HasFlag(r)))
        {
            RolleHinzufuegen(rolle, heute, e);
        }

        FachbereicheSetzen(Fachbereiche.Concat(dublette.Fachbereiche));
        if (Status != Personenstatus.Aktiv && dublette.Status == Personenstatus.Aktiv)
        {
            Status = Personenstatus.Aktiv;
        }

        dublette.Status = Personenstatus.Zusammengefuehrt;
        dublette.ZusammengefuehrtIn = Id;
        ZwischenstandAktualisieren(erfassung.Am);
    }

    /// <summary>Text für den Suchindex: alle (auch frühere) Namen, E-Mail-Adressen, Geburtsdatum, Wohnort – normalisiert.</summary>
    public string SuchtextFuerIndex()
    {
        // Alle Versionen (nicht nur der Stand von heute): der Index hängt so nicht vom Datum ab.
        var teile = new List<string?>();
        teile.AddRange(_namen.Select(n => n.Anzeige));
        teile.AddRange(_merkmale.Where(m => m.Art == MerkmalArt.Titel).Select(m => m.Inhalt));
        teile.AddRange(_merkmale.Where(m => m.Art == MerkmalArt.Geburtsdatum).Select(m => m.AlsDatum()?.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)));
        teile.AddRange(_adressen.Select(a => a.Ort));
        teile.AddRange(_emails.Select(e => e.Adresse));
        return string.Join(' ', teile.Where(t => !string.IsNullOrWhiteSpace(t)).Select(Suchtext.Normalisieren).Distinct());
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

    /// <summary>Nach einer Änderung: Bezugszeit nachziehen und den berechneten Stand verwerfen (er wird beim nächsten Lesen neu gebildet).</summary>
    private void ZwischenstandAktualisieren(DateTimeOffset jetzt)
    {
        if (jetzt > _stichzeit)
        {
            _stichzeit = jetzt;
        }

        _heute = null;
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
    IReadOnlyList<EMailAdresse> EMails,
    IReadOnlyList<Organisationszugehoerigkeit> Organisationen,
    IReadOnlyList<Buchungsvollmacht> Vollmachten,
    IReadOnlyList<Einwilligung> Einwilligungen,
    Beschaeftigungsart? Beschaeftigung = null,
    IReadOnlyList<PersonQualifikation>? Qualifikationen = null,
    string? Anrede = null,
    string? Geschlecht = null,
    string? Geburtsname = null,
    Ansprache? Ansprache = null)
{
    /// <summary>Liegt für Kanal und Zweck eine Einwilligung vor? Ohne Angabe: nein.</summary>
    public bool Erlaubt(Kontaktkanal kanal, Kontaktzweck zweck)
        => Einwilligungen.FirstOrDefault(e => e.Kanal == kanal && e.Zweck == zweck)?.Erteilt ?? false;

    public string NameMitTitel => Name is null ? "(unbekannt)" : string.IsNullOrEmpty(Titel) ? Name.Anzeige : $"{Titel} {Name.Anzeige}";

    /// <summary>
    /// Briefanrede ohne Komma, z. B. „Sehr geehrte Frau Dr. Muster“. Eine persönliche <see cref="Ansprache"/> der Person geht dem Stil der
    /// Vorlage vor („Hallo Kim“ bei Du).
    /// </summary>
    public string Briefanrede(Anredestil stil = Anredestil.Foermlich)
        => Name is null
            ? "Guten Tag"
            : Personen.Briefanrede.Bilden(Anrede, Titel, Name.Vorname, Name.NachnameMitVorsatz, Ansprache?.Stil ?? stil, Ansprache?.Du ?? false);

    public Postadresse? Aufenthaltsort => Adressen.FirstOrDefault(a => a.AdressId == AufenthaltsortId);

    public Postadresse? Korrespondenzadresse => Adressen.FirstOrDefault(a => a.AdressId == KorrespondenzadresseId);

    /// <summary>Wohin Briefe gehen: Korrespondenzadresse, sonst üblicher Aufenthaltsort.</summary>
    public Postadresse? BriefAn => Korrespondenzadresse ?? Aufenthaltsort;
}
