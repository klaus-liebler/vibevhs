# Datenbank – Tabellenstrukturen

Automatisch erzeugt aus dem EF-Modell (`DatenbankDokuTests`), nicht von Hand ändern. SQLite. Speicherformate: Ids als 16-Byte-BLOB
(UUIDv7, Big Endian), Kalendertage als Ganzzahl JJJJMMTT, Zeitpunkte (`DateTimeOffset`) und Termine als UTC-Millisekunden seit 1970,
Aufzählungen als Text. Namen sortieren per Collation `DIN5007_2` (siehe unten).

**Bitemporale Versionstabellen** (Namen, Merkmale, Adressen, … – Spalten `GueltigVon`/`GueltigBis` = fachliche Zeit, halboffen;
`ErfasstAm`/`ErsetztAm` = Wissenszeit, `ErsetztAm` = durch neueres Wissen ersetzt (Änderung ab Datum oder Korrektur); `Erfassungsart`, `ErfasstVon`, `Grund` = Änderungsvorgang).
„Einstufung“: F = führend, C = Lese-Cache (Kopie), A = nur nach Anonymisierung (siehe `Spalteneinstufung.cs`). Spalten mit „JSON“ enthalten eine
serialisierte Liste bzw. Struktur (Kandidaten für eigene Tabellen).

## Bitemporale Datenhaltung

Alle Personenangaben (Namen, Merkmale wie Titel, Rollen, Sprache, Geburtsdatum, Anrede, Adressen, Adresskennzeichen, Rufnummern,
E-Mail-Adressen, Organisationen, Vollmachten, Einwilligungen, Qualifikationen) und weitere Stammdaten (Entgelte, Ortsmerkmale) werden
**bitemporal** gespeichert. Es gibt zwei Zeitachsen:

| Achse | Spalten | Bedeutung |
|---|---|---|
| **Gültigkeitszeit** (valid time) | `GueltigVon`, `GueltigBis` | Wann galt die Angabe in der Welt? Intervall **halboffen**: `GueltigVon` einschließlich, `GueltigBis` ausschließlich (`NULL` = offen). |
| **Erfassungszeit** (transaction/system time) | `ErfasstAm`, `ErsetztAm` | Wann wusste das System das? Ebenfalls halboffen: ab `ErfasstAm`, bis `ErsetztAm` (`NULL` = aktueller Wissensstand). |

**Versionen werden nie geändert.** Jede Änderung – auch das Beenden einer Gültigkeit – erzeugt neue Zeilen und setzt `ErsetztAm` an der
alten Zeile. Nur so bleibt beantwortbar, was das System zu einem früheren Zeitpunkt wusste (z. B. warum ein Brief an die alte Adresse ging).

**Stabile Identität vs. Version:** Die Spalte `Id` ist die Zeilen-Id einer *Version*. Gegenstände, von denen eine Person mehrere haben kann,
tragen zusätzlich eine stabile Identität, die über alle Versionen gleich bleibt: `AdressId` (eine Adresse), `NummerId` (eine Rufnummer) usw.
Darauf verweisen andere Versionen, z. B. das Kennzeichen „üblicher Aufenthaltsort“ (`PersonAdressKennzeichen.AdressId`).
`AdressId` ist also **kein Adresstyp** – der Typ ergibt sich aus den Kennzeichen.

### Beispiel: Umzug mit nachträglicher Korrektur

Frau Muster wohnt seit 2020 in der Lindenstraße. Am 15.03.2026 meldet sie den Umzug zum 01.08. in den Bergweg. Am 10.07. geht die
Kursbestätigung raus. Am 20.09. kommt der Brief zurück, und sie sagt, sie sei schon zum 01.07. umgezogen.

| Zeile | Adresse | gültig von | gültig bis (einschl.) | erfasst | ersetzt | Erfassungsart |
|---|---|---|---|---|---|---|
| A | Lindenstr. 5 | 2020-01-01 | offen | 2020-01-10 | 2026-03-15 | Ersterfassung |
| B | Lindenstr. 5 | 2020-01-01 | 2026-07-31 | 2026-03-15 | 2026-09-20 | Ende wegen gemeldetem Umzug (Abgrenzung) |
| C | Bergweg 2 | 2026-08-01 | offen | 2026-03-15 | 2026-09-20 | Umzug ab 01.08. |
| D | Lindenstr. 5 | 2020-01-01 | 2026-06-30 | 2026-09-20 | – | Korrektur |
| E | Bergweg 2 | 2026-07-01 | offen | 2026-09-20 | – | Korrektur |

(In der Datenbank steht `GueltigBis` ausschließlich, also bei B `2026-08-01`, bei D `2026-07-01`.)

- **„Wo wohnte sie am 10.07.?“** (heutiger Wissensstand) → nur nicht ersetzte Zeilen D, E → **Bergweg**.
- **„Wohin hätten wir am 10.07. geschrieben?“** (Wissensstand 10.07.) → Zeilen, die am 10.07. bekannt und nicht ersetzt waren: B, C → **Lindenstraße**.

Beide Fragen beantwortet dieselbe Abfrage mit zwei Parametern – Stichtag `:tag` und Wissensstand `:bekannt`:

```sql
SELECT a.*
FROM PersonAdressen a
WHERE a.PersonId = :person
  AND a.GueltigVon <= :tag AND (a.GueltigBis IS NULL OR a.GueltigBis > :tag)          -- galt am Stichtag
  AND a.ErfasstAm <= :bekannt AND (a.ErsetztAm IS NULL OR a.ErsetztAm > :bekannt);   -- war zu diesem Zeitpunkt bekannt
-- „Wo wohnte sie am 10.07.?“            :tag = 20260710, :bekannt = jetzt (UTC-Millisekunden)
-- „Wohin hätten wir am 10.07. geschrieben?“ :tag = 20260710, :bekannt = 10.07.2026 24:00 Ortszeit als UTC-Millisekunden
```

Im Code übernimmt das `ZeitlicheHistorie.AlsStichtag(gueltigAm, bekanntAm)`; `Person.StandAm(tag, bekannt)` liefert den gesamten Stand.
Speicherformate: Tage (`GueltigVon`/`GueltigBis`) als Ganzzahl JJJJMMTT, Zeitpunkte (`ErfasstAm`/`ErsetztAm`) als UTC-Millisekunden seit 1970;
Ids als 16 Byte (UUIDv7, Big Endian). Die Parameter rechnet EF Core passend um; zum Lesen von Hand dienen die Sichten `v_…` (siehe unten).

### Sortierung nach DIN 5007-2 (Collation `DIN5007_2`)

Personen werden nach Nachname, dann Vorname sortiert, wie im Telefonbuch (DIN 5007-2): ä→ae, ö→oe, ü→ue, ß→ss, Akzente ohne Zeichen,
Groß-/Kleinschreibung egal; der Namensvorsatz („von“) steht in einer eigenen Spalte und zählt für die Sortierung nicht. Dafür tragen die
Spalten `PersonNamen.Nachname` und `PersonNamen.Vorname` die SQLite-Collation `DIN5007_2`; der Index `IX_PersonNamen_Sortierung`
(Nachname, Vorname) liegt auf ihr, die Personenliste blättert per Keyset (Nachname, Vorname, Id) über diesen Index (geprüft mit
`EXPLAIN QUERY PLAN`). Es gibt keine gespeicherte Sortierspalte.

Die Collation registriert die Anwendung bei jeder Verbindung (`SqliteErweiterungen`). **Werkzeuge ohne diese Collation** (z. B. „DB
Browser for SQLite“, die sqlite3-Kommandozeile) können die Datenbank lesen, aber
- nicht nach diesen Spalten sortieren oder vergleichen (`ORDER BY Nachname` → Fehler „no such collation sequence: DIN5007_2“),
- keine Namensversionen schreiben (der Index braucht die Collation) und kein `PRAGMA integrity_check` ausführen.
Zum Prüfen deshalb die lesbaren Sichten bzw. die Anwendung verwenden; Änderungen an den Daten nur über die Anwendung.

## Übersicht

- [Abwesenheiten](#abwesenheiten) – Abwesenheit
- [Anwesenheiten](#anwesenheiten) – Anwesenheit
- [Aufbewahrungsfristen](#aufbewahrungsfristen) – Aufbewahrungsfrist
- [Aufgaben](#aufgaben) – Aufgabe
- [Aufgabentypen](#aufgabentypen) – Aufgabentyp
- [Bankverbindungen](#bankverbindungen) – Bankverbindung
- [Belege](#belege) – Beleg
- [Belegungen](#belegungen) – Belegung
- [Benutzerkonten](#benutzerkonten) – Benutzerkonto
- [Dateien](#dateien) – Datei
- [Dokumente](#dokumente) – Dokument
- [DublettenAusschluesse](#dublettenausschluesse) – DublettenAusschluss
- [Einrichtung](#einrichtung) – Einrichtung
- [Ermaessigungen](#ermaessigungen) – Ermaessigung
- [Fachbereiche](#fachbereiche) – Fachbereich
- [Ferien](#ferien) – Ferienzeitraum
- [Finanzeinstellungen](#finanzeinstellungen) – Finanzeinstellungen
- [Finanzprotokoll](#finanzprotokoll) – Finanzprotokolleintrag
- [Honorarvertraege](#honorarvertraege) – Honorarvertrag
- [Identitaetspruefungen](#identitaetspruefungen) – Identitaetspruefung
- [IkFahrtkosten](#ikfahrtkosten) – IkFahrtkosten
- [Importlaeufe](#importlaeufe) – Importlauf
- [Integrationskurse](#integrationskurse) – Integrationskursdaten
- [KontingentEreignisse](#kontingentereignisse) – KontingentEreignis
- [Kontoauszuege](#kontoauszuege) – Kontoauszug
- [KursEntgelte](#kursentgelte) – KursEntgelt
- [Kursarten](#kursarten) – Kursart
- [Kurse](#kurse) – Kurs
- [Kursstaende](#kursstaende) – Kursstand
- [Kursunterlagen](#kursunterlagen) – Kursunterlage
- [Lichtbilder](#lichtbilder) – Lichtbild
- [Listenabschluesse](#listenabschluesse) – Listenabschluss
- [Loeschlaeufe](#loeschlaeufe) – Loeschlauf
- [Mahnlaeufe](#mahnlaeufe) – Mahnlauf
- [Merkmale](#merkmale) – Merkmal
- [Nummernkreise](#nummernkreise) – Nummernkreis
- [Organisationen](#organisationen) – Organisation
- [OrtMerkmale](#ortmerkmale) – OrtMerkmal
- [Orte](#orte) – Ort
- [PersonAdressKennzeichen](#personadresskennzeichen) – AdressKennzeichen
- [PersonAdressen](#personadressen) – Postadresse
- [PersonEMails](#personemails) – EMailAdresse
- [PersonEinwilligungen](#personeinwilligungen) – Einwilligung
- [PersonMerkmale](#personmerkmale) – PersonMerkmal
- [PersonNamen](#personnamen) – PersonName
- [PersonOrganisationen](#personorganisationen) – Organisationszugehoerigkeit
- [PersonQualifikationen](#personqualifikationen) – PersonQualifikation
- [PersonRufnummern](#personrufnummern) – Rufnummer
- [PersonVollmachten](#personvollmachten) – Buchungsvollmacht
- [Personen](#personen) – Person
- [Portalanliegen](#portalanliegen) – Portalantrag
- [Postausgang](#postausgang) – AusgehendeNachricht
- [Protokoll](#protokoll) – AuditEintrag
- [Pruefungsanmeldungen](#pruefungsanmeldungen) – Pruefungsanmeldung
- [Pruefungstermine](#pruefungstermine) – Pruefungstermin
- [Qualifikationsausnahmen](#qualifikationsausnahmen) – Qualifikationsausnahme
- [Schnellanmeldungen](#schnellanmeldungen) – Schnellanmeldung
- [Semester](#semester) – Semester
- [SepaMandate](#sepamandate) – SepaMandat
- [Shopbuchungen](#shopbuchungen) – Shopbuchung
- [Shopkonten](#shopkonten) – Shopkonto
- [Teilnahmeberechtigungen](#teilnahmeberechtigungen) – Teilnahmeberechtigung
- [Termincodes](#termincodes) – Termincode
- [Uebergaben](#uebergaben) – Uebergabe
- [Umsaetze](#umsaetze) – Umsatz
- [Veranstaltungen](#veranstaltungen) – Veranstaltung
- [Vorlagen](#vorlagen) – Textvorlage
- [Warenkoerbe](#warenkoerbe) – Warenkorb
- [Zahlungslaeufe](#zahlungslaeufe) – Zahlungslauf
- [Zugangstokens](#zugangstokens) – Zugangstoken

## Abwesenheiten

Klasse: `VibeVHS.Domain.Aufgaben.Abwesenheit`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BeendetAm | INTEGER |  |  |  |
| Bis | INTEGER | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT | ja |  |  |
| Grund | TEXT |  |  |  |
| Login | TEXT | ja | Index |  |
| Name | TEXT | ja |  |  |
| VertretungLogin | TEXT |  |  |  |
| VertretungName | TEXT |  |  |  |
| Von | INTEGER | ja |  |  |

## Anwesenheiten

Klasse: `VibeVHS.Domain.Durchfuehrung.Anwesenheit`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BelegungId | BLOB | ja | eindeutig |  |
| Bemerkung | TEXT(500) |  |  |  |
| Datum | INTEGER | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| ErstErfasstAm | INTEGER | ja |  |  |
| KursId | BLOB | ja | Index |  |
| Minuten | INTEGER | ja |  |  |
| OrtId | BLOB |  |  |  |
| PersonId | BLOB | ja | Index |  |
| Status | TEXT(40) | ja |  |  |
| VeranstaltungId | BLOB | ja | eindeutig |  |
| Vorlaeufig | INTEGER | ja |  |  |
| VorlaeufigGrund | TEXT |  |  |  |
| Weg | TEXT(40) | ja |  |  |

## Aufbewahrungsfristen

Klasse: `VibeVHS.Domain.Datenschutz.Aufbewahrungsfrist`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Schluessel | TEXT(40) | ja | PK |  |
| AbJahresende | INTEGER | ja |  |  |
| Bezeichnung | TEXT(200) | ja |  |  |
| Hinweis | TEXT(500) |  |  |  |
| Jahre | INTEGER | ja |  |  |
| Rechtsgrundlage | TEXT(300) | ja |  |  |

## Aufgaben

Klasse: `VibeVHS.Domain.Aufgaben.Aufgabe`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AngelegtAm | INTEGER | ja |  |  |
| BearbeiterKennung | TEXT(100) |  |  |  |
| BearbeiterName | TEXT(200) |  |  |  |
| Beschreibung | TEXT |  |  |  |
| Dringlichkeit | TEXT(40) | ja |  |  |
| ErledigtAm | INTEGER |  |  |  |
| ErledigtVon | TEXT(200) |  |  |  |
| EskaliertAm | INTEGER |  |  |  |
| Fachbereich | INTEGER |  |  |  |
| Faellig | INTEGER | ja |  |  |
| Gruppe | TEXT(60) | ja | Index |  |
| Schluessel | TEXT(200) | ja | eindeutig |  |
| Status | TEXT(40) | ja | Index |  |
| Titel | TEXT(300) | ja |  |  |
| Typ | TEXT(60) | ja |  |  |
| UebernommenAm | INTEGER |  |  |  |
| Vermerk | TEXT(1000) |  |  |  |
| Ziel | TEXT(300) |  |  |  |

## Aufgabentypen

Klasse: `VibeVHS.Domain.Aufgaben.Aufgabentyp`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Schluessel | TEXT(60) | ja | PK |  |
| Beschreibung | TEXT |  |  |  |
| EskalationNachTagen | INTEGER | ja |  |  |
| FestePerson | TEXT(100) |  |  |  |
| JeFachbereich | INTEGER | ja |  |  |
| MenschGibtFrei | INTEGER |  |  |  |
| Name | TEXT(200) | ja |  |  |
| Rolle | TEXT(60) | ja |  |  |
| VorlaufTage | INTEGER |  |  |  |

## Bankverbindungen

Klasse: `VibeVHS.Domain.Finanzen.Bankverbindung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| PersonId | BLOB | ja | PK |  |
| Bic | TEXT(11) |  |  |  |
| Iban | TEXT(34) | ja |  |  |
| Kontoinhaber | TEXT(200) | ja |  |  |

## Belege

Klasse: `VibeVHS.Domain.Finanzen.Beleg`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AnonymisiertAm | INTEGER |  |  |  |
| Art | TEXT(40) | ja |  |  |
| Belegdatum | INTEGER | ja |  |  |
| BetragCent | INTEGER | ja |  |  |
| BezugBelegId | BLOB |  | Index |  |
| DokumentId | BLOB |  |  |  |
| EmpfaengerAnschrift | TEXT(1000) |  |  |  |
| EmpfaengerName | TEXT(300) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Faellig | INTEGER |  |  |  |
| FestgeschriebenAm | INTEGER |  |  |  |
| FestgeschriebenVon | TEXT(200) |  |  |  |
| Grundlage | TEXT |  |  |  |
| Jahr | INTEGER |  |  |  |
| Laufnummer | INTEGER |  |  |  |
| Nummer | TEXT(30) |  | eindeutig |  |
| OrganisationId | BLOB |  | Index |  |
| PersonId | BLOB |  | Index |  |
| Sha256 | TEXT(64) |  |  |  |
| StorniertDurchId | BLOB |  |  |  |
| Text | TEXT(2000) |  |  |  |
| UebergabeId | BLOB |  |  |  |
| Xml | TEXT |  |  |  |

## Belegungen

Klasse: `VibeVHS.Domain.Anmeldung.Belegung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AngebotBis | INTEGER |  | Index |  |
| Belegungsart | TEXT(40) | ja |  |  |
| BevollmaechtigterId | BLOB |  | Index |  |
| BevollmaechtigterName | TEXT(300) |  |  |  |
| EntgeltCent | INTEGER | ja |  |  |
| ErinnertAm | INTEGER |  |  |  |
| Ermaessigt | INTEGER | ja |  |  |
| ErmaessigungNachweis | TEXT(500) |  |  |  |
| GebuchtAm | INTEGER | ja |  |  |
| GebuchtInRolle | TEXT(50) |  |  |  |
| GebuchtVon | TEXT(200) | ja |  |  |
| GebuchtVonKennung | TEXT(100) |  |  |  |
| GebuehrCent | INTEGER |  |  |  |
| KontingentAnzahl | INTEGER |  |  |  |
| KontingentGueltigBis | INTEGER |  |  |  |
| KursId | BLOB | ja | Index |  |
| Kursnummer | TEXT(30) | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| Status | TEXT(40) | ja | Index |  |
| StorniertAm | INTEGER |  |  |  |
| StorniertVon | TEXT(200) |  |  |  |
| StornoGrund | TEXT(500) |  |  |  |
| UmgebuchtNachId | BLOB |  |  |  |
| UmgebuchtVonId | BLOB |  |  |  |
| VollmachtId | BLOB |  |  |  |
| WartelisteSeit | INTEGER |  |  |  |
| Weg | TEXT(40) | ja |  |  |
| Zahlart | TEXT(40) | ja |  |  |

## Benutzerkonten

Klasse: `VibeVHS.Application.Sicherheit.Benutzerkonto`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK | F: Schlüssel |
| Aktiv | INTEGER | ja |  | F: Prozesszustand |
| Fachbereiche | TEXT | ja |  | F: Zuständigkeit; JSON |
| Funktion | TEXT(300) |  |  | F: Funktionsbezeichnung |
| Hinweis | TEXT(500) |  |  | F: Notiz |
| Login | TEXT(100) | ja | eindeutig | F: Anmeldename |
| Name | TEXT(200) |  |  | F: Name nur bei Funktionskonten ohne Person (sonst leer, der Name kommt aus der Person) |
| PersonId | BLOB |  |  | F: einzige Verknüpfung Konto ↔ Person |
| Rollen | TEXT | ja |  | F: Berechtigungsrollen des Kontos; JSON |

## Dateien

Klasse: `VibeVHS.Domain.Portal.Datei`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErstelltAm | INTEGER | ja |  |  |
| Groesse | INTEGER | ja |  |  |
| Inhalt | BLOB | ja |  |  |
| Name | TEXT | ja |  |  |
| Sha256 | TEXT | ja |  |  |
| Typ | TEXT | ja |  |  |

## Dokumente

Klasse: `VibeVHS.Domain.Dokumente.Dokument`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AnonymisiertAm | INTEGER |  |  |  |
| Art | TEXT(40) | ja |  |  |
| BelegungId | BLOB |  | Index |  |
| DatenJson | TEXT | ja |  |  |
| ErsetztDurch | BLOB |  |  |  |
| ErstelltAm | INTEGER | ja |  |  |
| ErstelltVon | TEXT(200) | ja |  |  |
| Klartext | TEXT | ja |  |  |
| KursId | BLOB |  | Index |  |
| NeuausstellungGrund | TEXT(500) |  |  |  |
| NeuausstellungVon | BLOB |  |  |  |
| Pdf | BLOB | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| Sha256 | TEXT(64) | ja |  |  |
| Titel | TEXT(300) | ja |  |  |
| UebersetzbarBis | INTEGER | ja |  |  |
| UebersetzungsToken | TEXT(64) | ja | eindeutig |  |

## DublettenAusschluesse

Klasse: `VibeVHS.Application.Personen.DublettenAusschluss`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| PersonA | BLOB | ja | PK |  |
| PersonB | BLOB | ja | PK |  |
| Am | INTEGER | ja |  |  |
| Von | TEXT(200) | ja |  |  |

## Einrichtung

Klasse: `VibeVHS.Domain.Stammdaten.Einrichtung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | INTEGER | ja | PK |  |
| Bundesland | TEXT(50) | ja |  |  |
| Kurzname | TEXT(50) | ja |  |  |
| Logo | BLOB |  |  |  |
| Name | TEXT(200) | ja |  |  |
| Orte | TEXT | ja |  | JSON |
| Website | TEXT(200) |  |  |  |

## Ermaessigungen

Klasse: `VibeVHS.Domain.Anmeldung.Ermaessigung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(100) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| GueltigBis | INTEGER | ja |  |  |
| GueltigVon | INTEGER | ja |  |  |
| Nachweis | TEXT(500) | ja |  |  |
| PersonId | BLOB | ja | Index |  |

## Fachbereiche

Klasse: `VibeVHS.Domain.Stammdaten.Fachbereich`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Nr | INTEGER | ja | PK |  |
| Name | TEXT(200) | ja |  |  |

## Ferien

Klasse: `VibeVHS.Domain.Programm.Ferienzeitraum`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Bis | INTEGER | ja |  |  |
| Bundesland | TEXT(10) | ja |  |  |
| Name | TEXT(100) | ja |  |  |
| Quelle | TEXT |  |  |  |
| Von | INTEGER | ja |  |  |

## Finanzeinstellungen

Klasse: `VibeVHS.Domain.Finanzen.Finanzeinstellungen`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | INTEGER | ja | PK |  |
| Anschrift | TEXT | ja |  |  |
| Bic | TEXT |  |  |  |
| GlaeubigerId | TEXT | ja |  |  |
| Iban | TEXT | ja |  |  |
| Kontoinhaber | TEXT | ja |  |  |
| Mahnstufen | TEXT | ja |  | JSON |
| RuecklastschriftGebuehrCent | INTEGER | ja |  |  |
| Steuerhinweis | TEXT | ja |  |  |
| Steuernummer | TEXT |  |  |  |
| UstId | TEXT |  |  |  |
| VorabankuendigungTage | INTEGER | ja |  |  |
| ZahlungszielTage | INTEGER | ja |  |  |

## Finanzprotokoll

Klasse: `VibeVHS.Domain.Finanzen.Finanzprotokolleintrag`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Nr | INTEGER | ja | PK |  |
| Aktion | TEXT(60) | ja |  |  |
| BenutzerKennung | TEXT(100) | ja |  |  |
| Hash | TEXT(64) | ja |  |  |
| Nachher | TEXT |  |  |  |
| ObjektId | BLOB |  |  |  |
| ObjektTyp | TEXT(60) | ja |  |  |
| Vorher | TEXT |  |  |  |
| VorigerHash | TEXT(64) | ja |  |  |
| Zeitpunkt | INTEGER | ja |  |  |

## Honorarvertraege

Klasse: `VibeVHS.Domain.Finanzen.Honorarvertrag`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Bezeichnung | TEXT | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT |  |  |  |
| GueltigAb | INTEGER | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| KursId | BLOB |  |  |  |
| Modell | TEXT(60) | ja |  |  |
| Parameter | TEXT | ja |  | JSON |
| PersonId | BLOB | ja | Index |  |

## Identitaetspruefungen

Klasse: `VibeVHS.Domain.Integration.Identitaetspruefung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Bemerkung | TEXT(500) |  |  |  |
| Dokument | TEXT(40) | ja |  |  |
| GeprueftAm | INTEGER | ja |  |  |
| GeprueftVon | TEXT | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| StimmtUeberein | INTEGER | ja |  |  |

## IkFahrtkosten

Klasse: `VibeVHS.Domain.Integration.IkFahrtkosten`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BeantragtAm | INTEGER | ja |  |  |
| Begruendung | TEXT | ja |  |  |
| BetragCent | INTEGER | ja |  |  |
| EntfernungKm | TEXT | ja |  |  |
| GutschriftId | BLOB |  |  |  |
| Jahr | INTEGER | ja |  |  |
| KursId | BLOB | ja | Index |  |
| Monat | INTEGER | ja |  |  |
| PersonId | BLOB | ja |  |  |
| Status | TEXT(40) | ja |  |  |

## Importlaeufe

Klasse: `VibeVHS.Domain.Personen.Importlauf`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Am | INTEGER | ja |  |  |
| Angelegt | INTEGER | ja |  |  |
| Art | TEXT | ja |  |  |
| Datei | TEXT | ja |  |  |
| Dubletten | INTEGER | ja |  |  |
| Fehler | INTEGER | ja |  |  |
| Sha256 | TEXT | ja | Index |  |
| Von | TEXT | ja |  |  |
| Zeilen | INTEGER | ja |  |  |
| Zuordnungen | TEXT | ja |  | JSON |

## Integrationskurse

Klasse: `VibeVHS.Domain.Integration.Integrationskursdaten`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| KursId | BLOB | ja | PK |  |
| Abrechnungen | TEXT | ja |  | JSON |
| Abschnitte | TEXT | ja |  | JSON |
| Art | TEXT(40) | ja |  |  |
| BamfKursnummer | TEXT(40) | ja |  |  |
| KostenbeitragCentJeUe | INTEGER | ja |  |  |
| KostenerstattungCentJeUe | INTEGER | ja |  |  |
| KursbeginnGemeldetAm | INTEGER |  |  |  |
| KursbeginnVorgang | TEXT |  |  |  |

## KontingentEreignisse

Klasse: `VibeVHS.Domain.Anmeldung.KontingentEreignis`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Anzahl | INTEGER | ja |  |  |
| Art | TEXT(40) | ja |  |  |
| BelegungId | BLOB | ja | → Belegungen, eindeutig |  |
| Bemerkung | TEXT(300) |  |  |  |
| Laufnummer | INTEGER | ja | eindeutig |  |
| OrtId | BLOB |  |  |  |
| VeranstaltungId | BLOB |  | Index |  |
| Von | TEXT(200) | ja |  |  |
| Zeitpunkt | INTEGER | ja |  |  |

## Kontoauszuege

Klasse: `VibeVHS.Domain.Finanzen.Kontoauszug`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AuszugId | TEXT | ja | Index |  |
| Bis | INTEGER |  |  |  |
| Datei | TEXT | ja |  |  |
| EingelesenAm | INTEGER | ja |  |  |
| EingelesenVon | TEXT | ja |  |  |
| EndsaldoCent | INTEGER |  |  |  |
| Iban | TEXT |  |  |  |
| Sha256 | TEXT | ja | eindeutig |  |
| Von | INTEGER |  |  |  |

## KursEntgelte

Klasse: `VibeVHS.Domain.Programm.KursEntgelt`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Cent | INTEGER | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErmaessigtCent | INTEGER |  |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| KursId | BLOB | ja | → Kurse, Index |  |

## Kursarten

Klasse: `VibeVHS.Domain.Programm.Kursart`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BenoetigteDozierendenmerkmale | TEXT | ja |  | JSON |
| BenoetigteMerkmale | TEXT | ja |  | JSON |
| FerienAuslassen | INTEGER | ja |  |  |
| Name | TEXT(200) | ja |  |  |
| Nutzungsart | TEXT(40) | ja |  |  |
| Richtlinie | TEXT | ja |  | JSON |
| Sichtbarkeit | TEXT(40) | ja |  |  |

## Kurse

Klasse: `VibeVHS.Domain.Programm.Kurs`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK | F: Schlüssel |
| ArchiviertAm | INTEGER |  |  | F: Archivierung (Prozess) |
| AuftraggeberId | BLOB |  |  | F: Verweis auf die Organisation |
| BenoetigteDozierendenmerkmale | TEXT | ja |  | F: Anforderungen an Lehrkräfte (z. B. sprachfreigabe=Englisch); JSON |
| BenoetigteMerkmale | TEXT | ja |  | F: Raumanforderungen (aus der Kursart übernommen, kursbezogen änderbar); JSON |
| Beschreibung | TEXT |  |  | F: Planungsdatum |
| Bildungsurlaub | TEXT(100) |  |  | F: Anerkennung |
| ErzeugtBis | INTEGER |  |  | F: Prozesszustand: wie weit die Termine einer unbefristeten Serie erzeugt sind |
| Fachbereich | INTEGER | ja |  | F: Planungsdatum |
| KursartId | BLOB | ja |  | F: Planungsdatum |
| Kursnummer | TEXT(30) | ja | eindeutig | F: Planungsdatum |
| MaxTeilnehmende | INTEGER | ja |  | F: Planungsdatum |
| MinTeilnehmende | INTEGER | ja |  | F: Planungsdatum |
| Nutzungsart | TEXT(40) | ja |  | F: Planungsdatum: bestimmt die Raumkapazität (Vorlage aus der Kursart, wie Richtlinie und Raumanforderungen) |
| Richtlinie | TEXT | ja |  | F: Buchungsrichtlinie (aus der Kursart übernommen, kursbezogen änderbar); JSON |
| SemesterId | BLOB | ja | Index | F: Planungsdatum |
| Serie | TEXT |  |  | F: Serienregel; JSON |
| Sichtbarkeit | TEXT(40) | ja |  | F: Planungsdatum |
| Status | TEXT(40) | ja |  | F: Prozesszustand |
| Titel | TEXT(300) | ja |  | F: Planungsdatum |
| Umfang | TEXT(40) | ja |  | F: Planungsdatum |

## Kursstaende

Klasse: `VibeVHS.Domain.Shop.Kursstand`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| KursId | BLOB | ja | PK |  |
| Entfernt | INTEGER | ja |  |  |
| GeaendertAm | INTEGER | ja |  |  |
| Hash | TEXT | ja |  |  |
| Kursnummer | TEXT | ja |  |  |

## Kursunterlagen

Klasse: `VibeVHS.Domain.Portal.Kursunterlage`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Abrufe | INTEGER | ja |  |  |
| DateiId | BLOB | ja |  |  |
| GueltigBis | INTEGER | ja |  |  |
| HochgeladenAm | INTEGER | ja |  |  |
| HochgeladenVon | BLOB | ja |  |  |
| KursId | BLOB | ja | Index |  |
| Titel | TEXT | ja |  |  |
| TokenHash | TEXT | ja | eindeutig |  |
| ZurueckgezogenAm | INTEGER |  |  |  |

## Lichtbilder

Klasse: `VibeVHS.Domain.Integration.Lichtbild`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AufgenommenAm | INTEGER | ja |  |  |
| AufgenommenVon | TEXT | ja |  |  |
| GeloeschtAm | INTEGER |  |  |  |
| Inhalt | BLOB | ja |  |  |
| KursId | BLOB | ja | Index |  |
| LoeschenAb | INTEGER | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| Typ | TEXT | ja |  |  |

## Listenabschluesse

Klasse: `VibeVHS.Domain.Durchfuehrung.Listenabschluss`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| VeranstaltungId | BLOB | ja | PK |  |
| Am | INTEGER | ja |  |  |
| KursId | BLOB | ja |  |  |
| Von | TEXT(200) | ja |  |  |

## Loeschlaeufe

Klasse: `VibeVHS.Domain.Datenschutz.Loeschlauf`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Anonymisiert | INTEGER | ja |  |  |
| AusgefuehrtAm | INTEGER |  |  |  |
| ErstelltAm | INTEGER | ja |  |  |
| ErstelltVon | TEXT(200) | ja |  |  |
| Freigabe1Am | INTEGER |  |  |  |
| Freigabe1Kennung | TEXT(100) |  |  |  |
| Freigabe1Name | TEXT(200) |  |  |  |
| Freigabe2Am | INTEGER |  |  |  |
| Freigabe2Kennung | TEXT(100) |  |  |  |
| Freigabe2Name | TEXT(200) |  |  |  |
| Gesperrt | INTEGER | ja |  |  |
| PersonIds | TEXT | ja |  | JSON |
| Regel | TEXT(300) | ja |  |  |
| Status | TEXT(40) | ja |  |  |
| Uebersprungen | INTEGER | ja |  |  |
| ZuAnonymisieren | INTEGER | ja |  |  |
| ZuSperren | INTEGER | ja |  |  |

## Mahnlaeufe

Klasse: `VibeVHS.Domain.Finanzen.Mahnlauf`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErstelltAm | INTEGER | ja |  |  |
| ErstelltVon | TEXT | ja |  |  |
| FreigegebenAm | INTEGER |  |  |  |
| FreigegebenVon | TEXT |  |  |  |
| Posten | TEXT | ja |  | JSON |
| Status | TEXT(40) | ja |  |  |

## Merkmale

Klasse: `VibeVHS.Domain.Programm.Merkmal`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Schluessel | TEXT(40) | ja | PK |  |
| Ausschlussgruppe | TEXT(60) |  |  |  |
| Bezug | TEXT(20) | ja |  |  |
| Einheit | TEXT(30) |  |  |  |
| Gesetzlich | INTEGER | ja |  |  |
| Gruppe | TEXT(60) | ja |  |  |
| InaktiveWerte | TEXT | ja |  | JSON |
| Max | INTEGER |  |  |  |
| Mehrfach | INTEGER | ja |  |  |
| Min | INTEGER |  |  |  |
| NachweisPflicht | INTEGER | ja |  |  |
| Name | TEXT(200) | ja |  |  |
| Typ | TEXT(20) | ja |  |  |
| Vererbbar | INTEGER | ja |  |  |
| VorlaufTage | INTEGER | ja |  |  |
| Werte | TEXT | ja |  | JSON |

## Nummernkreise

Klasse: `VibeVHS.Domain.Finanzen.Nummernkreis`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Art | TEXT(40) | ja | PK |  |
| Jahr | INTEGER | ja | PK |  |
| Letzte | INTEGER | ja |  |  |
| Version | BLOB | ja |  |  |

## Organisationen

Klasse: `VibeVHS.Domain.Personen.Organisation`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK | F: Schlüssel |
| Art | TEXT(40) | ja |  | F: Stammdatum Organisation |
| LeitwegId | TEXT |  |  | F: XRechnung |
| Name | TEXT(300) | ja |  | F: Stammdatum Organisation |
| Ort | TEXT(200) |  |  | F: Stammdatum Organisation |
| RechnungsEMail | TEXT |  |  | F: Stammdatum Organisation |
| Rechnungsanschrift | TEXT |  |  | F: Belege frieren ihre Anschrift selbst ein (GoBD) |

## OrtMerkmale

Klasse: `VibeVHS.Domain.Programm.OrtMerkmal`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Nachweis | TEXT |  |  |  |
| OrtId | BLOB | ja | → Orte, Index |  |
| Schluessel | TEXT(40) | ja |  |  |
| WertText | TEXT |  |  |  |

## Orte

Klasse: `VibeVHS.Domain.Programm.Ort`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK | F: Schlüssel |
| Aktiv | INTEGER | ja |  | F: Prozesszustand |
| Art | TEXT(40) | ja |  | F: Stammdatum Ort |
| Breitengrad | REAL |  |  | F: Geo |
| ElternId | BLOB |  | Index | F: Hierarchie |
| Kapazitaeten | TEXT | ja |  | F: Kapazität je Nutzungsart; JSON |
| Kurzname | TEXT(40) |  |  | F: Stammdatum Ort |
| Laengengrad | REAL |  |  | F: Geo |
| Lage | TEXT(1000) |  |  | F: Wegbeschreibung |
| Name | TEXT(200) | ja |  | F: Stammdatum Ort |
| Postleitzahl | TEXT(10) |  |  | F: Adresse des Orts |
| Stadt | TEXT(100) |  |  | F: Adresse des Orts |
| Strasse | TEXT(200) |  |  | F: Adresse des Orts (nicht personenbezogen) |
| Zugang | TEXT(500) |  |  | F: Zugangshinweis |

## PersonAdressKennzeichen

Klasse: `VibeVHS.Domain.Personen.AdressKennzeichen`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AdressAnzeige | TEXT(500) | ja |  |  |
| AdressId | BLOB | ja |  |  |
| Art | TEXT(40) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| PersonId | BLOB | ja | → Personen, Index |  |

## PersonAdressen

Klasse: `VibeVHS.Domain.Personen.Postadresse`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AdressId | BLOB | ja | Index |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Land | TEXT(2) | ja |  |  |
| Ort | TEXT(200) | ja |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |
| Postleitzahl | TEXT(20) | ja |  |  |
| Strasse | TEXT(200) | ja |  |  |
| Zusatz | TEXT(200) |  |  |  |

## PersonEMails

Klasse: `VibeVHS.Domain.Personen.EMailAdresse`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AdressId | BLOB | ja | Index |  |
| Adresse | TEXT(320) | ja |  |  |
| Art | TEXT(40) | ja |  |  |
| Bevorzugt | INTEGER | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| PersonId | BLOB | ja | → Personen, Index |  |

## PersonEinwilligungen

Klasse: `VibeVHS.Domain.Personen.Einwilligung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Erteilt | INTEGER | ja |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Kanal | TEXT(40) | ja |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |
| Zweck | TEXT(40) | ja |  |  |

## PersonMerkmale

Klasse: `VibeVHS.Domain.Personen.PersonMerkmal`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Inhalt | TEXT(200) | ja |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |

## PersonNamen

Klasse: `VibeVHS.Domain.Personen.PersonName`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Nachname | TEXT(200) | ja | Index |  |
| PersonId | BLOB | ja | → Personen, Index |  |
| Vorname | TEXT(200) | ja | Index |  |
| Vorsatz | TEXT(30) | ja |  |  |
| Zusatz | TEXT(20) | ja |  |  |

## PersonOrganisationen

Klasse: `VibeVHS.Domain.Personen.Organisationszugehoerigkeit`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Funktion | TEXT(200) |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| OrganisationId | BLOB | ja | Index |  |
| OrganisationName | TEXT(300) | ja |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |

## PersonQualifikationen

Klasse: `VibeVHS.Domain.Personen.PersonQualifikation`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AusstellendeStelle | TEXT(200) |  |  |  |
| Bezeichnung | TEXT(200) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Nachweis | TEXT(500) |  |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |
| Schluessel | TEXT(40) | ja |  |  |
| WertText | TEXT(200) |  |  |  |

## PersonRufnummern

Klasse: `VibeVHS.Domain.Personen.Rufnummer`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| Bevorzugt | INTEGER | ja |  |  |
| Dienste | INTEGER(40) | ja |  |  |
| Einwilligung | INTEGER | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Nummer | TEXT(50) | ja |  |  |
| NummerId | BLOB | ja | Index |  |
| PersonId | BLOB | ja | → Personen, Index |  |

## PersonVollmachten

Klasse: `VibeVHS.Domain.Personen.Buchungsvollmacht`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| BevollmaechtigterId | BLOB | ja | Index |  |
| BevollmaechtigterName | TEXT(300) | ja |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT(200) | ja |  |  |
| Erfassungsart | TEXT(40) | ja |  |  |
| ErsetztAm | INTEGER |  |  |  |
| Grund | TEXT(500) | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| GueltigVon | INTEGER | ja | Index |  |
| Nachweis | TEXT(500) | ja |  |  |
| PersonId | BLOB | ja | → Personen, Index |  |
| VollmachtId | BLOB | ja |  |  |

## Personen

Klasse: `VibeVHS.Domain.Personen.Person`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK | F: Schlüssel |
| AngelegtAm | INTEGER | ja |  | F: Zeitpunkt der Ersterfassung |
| AnonymisiertAm | INTEGER |  |  | F: Anonymisierung (Prozess) |
| AnonymisiertGebiet | TEXT(100) |  |  | A: Kreis/PLZ-Gebiet statt Adresse |
| AnonymisiertGeburtskohorte | TEXT(20) |  |  | A: Geburtsjahr-Kohorte statt Geburtsdatum |
| ArchiviertAm | INTEGER |  |  | F: Archivierung (Prozess) |
| Datenschutz | TEXT(20) | ja | Index | F: Datenschutzstatus (aktiv, gesperrt, archiviert, anonymisiert) |
| Fachbereiche | TEXT | ja |  | F: Zuordnung zu Fachbereichen (Organisation, nicht personenbezogen); JSON |
| GesperrtAm | INTEGER |  |  | F: Sperre (Prozess) |
| GesperrtBis | INTEGER |  |  | F: Sperre (Prozess) |
| Sperrgrund | TEXT(2000) |  |  | F: Sperre (Prozess) |
| Status | TEXT(30) | ja | Index | F: Prozesszustand (aktiv, ruhend, zusammengeführt …), Änderungen im Protokoll |
| ZusammengefuehrtIn | BLOB |  |  | F: Dublettenzusammenführung (Prozess) |

## Portalanliegen

Klasse: `VibeVHS.Domain.Portal.Portalantrag`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| BezugId | BLOB |  |  |  |
| DateiId | BLOB |  |  |  |
| Daten | TEXT | ja |  | JSON |
| EingereichtAm | INTEGER | ja |  |  |
| EntschiedenAm | INTEGER |  |  |  |
| EntschiedenVon | TEXT |  |  |  |
| Fachbereich | INTEGER |  |  |  |
| PersonId | BLOB | ja | Index |  |
| Status | TEXT(40) | ja | Index |  |
| Text | TEXT | ja |  |  |
| Titel | TEXT | ja |  |  |
| Vermerk | TEXT |  |  |  |

## Postausgang

Klasse: `VibeVHS.Application.Postausgang.AusgehendeNachricht`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AnhangInhalt | TEXT |  |  |  |
| AnhangName | TEXT(200) |  |  |  |
| AnhangTyp | TEXT(100) |  |  |  |
| AusgeloestVon | TEXT(200) | ja |  |  |
| Betreff | TEXT(500) | ja |  |  |
| Empfaenger | TEXT(320) | ja |  |  |
| EmpfaengerAnzeige | TEXT(200) |  |  |  |
| ErstelltAm | INTEGER | ja | Index |  |
| Fehler | TEXT |  |  |  |
| Inhalt | TEXT | ja |  |  |
| Kanal | TEXT(40) | ja |  |  |
| PersonId | BLOB |  | Index |  |
| Rueckmeldung | TEXT |  |  |  |
| Sprache | TEXT(10) | ja |  |  |
| Status | TEXT(40) | ja | Index |  |
| Uebersetzungslink | TEXT |  |  |  |
| Versuche | INTEGER | ja |  |  |
| WeitereAnhaenge | TEXT | ja |  | JSON |
| ZugestelltAm | INTEGER |  |  |  |

## Protokoll

Klasse: `VibeVHS.Application.Protokoll.AuditEintrag`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Aktion | TEXT(100) | ja |  |  |
| Benutzer | TEXT(200) | ja |  |  |
| BenutzerKennung | TEXT(100) | ja |  |  |
| Beschreibung | TEXT | ja |  |  |
| Erfolgreich | INTEGER | ja |  |  |
| ObjektId | BLOB |  | Index |  |
| ObjektTyp | TEXT(100) |  | Index |  |
| Rolle | TEXT(50) |  |  |  |
| Zeitpunkt | INTEGER | ja | Index |  |

## Pruefungsanmeldungen

Klasse: `VibeVHS.Domain.Integration.Pruefungsanmeldung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AngemeldetAm | INTEGER | ja |  |  |
| Ergebnis | TEXT(40) | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| PruefungId | BLOB | ja | Index |  |
| Punkte | INTEGER |  |  |  |

## Pruefungstermine

Klasse: `VibeVHS.Domain.Integration.Pruefungstermin`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| Beginn | INTEGER | ja |  |  |
| GemeldetAm | INTEGER |  |  |  |
| KursId | BLOB |  |  |  |
| Ort | TEXT | ja |  |  |

## Qualifikationsausnahmen

Klasse: `VibeVHS.Domain.Programm.Qualifikationsausnahme`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Begruendung | TEXT(1000) | ja |  |  |
| ErteiltAm | INTEGER | ja |  |  |
| ErteiltVon | TEXT(200) | ja |  |  |
| KursId | BLOB | ja | Index |  |
| PersonId | BLOB | ja |  |  |
| Schluessel | TEXT(40) | ja |  |  |

## Schnellanmeldungen

Klasse: `VibeVHS.Domain.Shop.Schnellanmeldung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BelegungId | BLOB | ja |  |  |
| Einwilligungen | TEXT | ja |  | JSON |
| KostenfreiBis | INTEGER | ja |  |  |
| Kursnummer | TEXT | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| StorniertAm | INTEGER |  |  |  |
| StornoHash | TEXT | ja | eindeutig |  |
| Weg | TEXT | ja |  |  |
| Zeitpunkt | INTEGER | ja |  |  |

## Semester

Klasse: `VibeVHS.Domain.Programm.Semester`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| ArchiviertAm | INTEGER |  |  |  |
| Beginn | INTEGER | ja |  |  |
| Ende | INTEGER | ja |  |  |
| Kuerzel | TEXT(10) | ja |  |  |
| Name | TEXT(100) | ja |  |  |

## SepaMandate

Klasse: `VibeVHS.Domain.Finanzen.SepaMandat`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Bic | TEXT(11) |  |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErsteNutzungAm | INTEGER |  |  |  |
| Iban | TEXT(34) | ja |  |  |
| Kontoinhaber | TEXT(200) | ja |  |  |
| LetzteNutzungAm | INTEGER |  |  |  |
| PersonId | BLOB | ja | Index |  |
| Referenz | TEXT(35) | ja | eindeutig |  |
| Status | TEXT(40) | ja |  |  |
| UnterschriebenAm | INTEGER | ja |  |  |
| WiderrufenAm | INTEGER |  |  |  |

## Shopbuchungen

Klasse: `VibeVHS.Domain.Shop.Shopbuchung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AnfrageHash | TEXT | ja |  |  |
| Einwilligungen | TEXT | ja |  | JSON |
| Idempotenzschluessel | TEXT | ja | eindeutig |  |
| KontoId | BLOB |  |  |  |
| MandatId | BLOB |  |  |  |
| PersonId | BLOB | ja |  |  |
| Positionen | TEXT | ja |  | JSON |
| WarenkorbId | BLOB | ja | Index |  |
| Zahlart | TEXT | ja |  |  |
| Zeitpunkt | INTEGER | ja |  |  |

## Shopkonten

Klasse: `VibeVHS.Domain.Shop.Shopkonto`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BestaetigtAm | INTEGER |  |  |  |
| EMail | TEXT | ja | eindeutig |  |
| ErstelltAm | INTEGER | ja |  |  |
| Geburtsdatum | INTEGER |  |  |  |
| Nachname | TEXT | ja |  |  |
| PersonId | BLOB |  |  |  |
| Sprache | TEXT | ja |  |  |
| Status | TEXT(40) | ja |  |  |
| TokenGueltigBis | INTEGER | ja |  |  |
| TokenHash | TEXT | ja | Index |  |
| Vorname | TEXT | ja |  |  |

## Teilnahmeberechtigungen

Klasse: `VibeVHS.Domain.Integration.Teilnahmeberechtigung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Aktenzeichen | TEXT(60) | ja |  |  |
| Art | TEXT(40) | ja |  |  |
| Ausgestellt | INTEGER | ja |  |  |
| Befreiungsgrund | TEXT |  |  |  |
| ErfasstAm | INTEGER | ja |  |  |
| ErfasstVon | TEXT | ja |  |  |
| Fahrtkosten | INTEGER | ja |  |  |
| GueltigBis | INTEGER |  |  |  |
| Kostenbefreit | INTEGER | ja |  |  |
| PersonId | BLOB | ja | Index |  |
| Stelle | TEXT(200) | ja |  |  |
| UeUmfang | INTEGER | ja |  |  |

## Termincodes

Klasse: `VibeVHS.Domain.Durchfuehrung.Termincode`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Code | TEXT(40) | ja | PK |  |
| ErstelltAm | INTEGER | ja |  |  |
| GueltigBis | INTEGER | ja |  |  |
| GueltigVon | INTEGER | ja |  |  |
| OrtId | BLOB | ja | Index |  |
| VeranstaltungId | BLOB | ja | Index |  |

## Uebergaben

Klasse: `VibeVHS.Domain.Finanzen.Uebergabe`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Am | INTEGER | ja |  |  |
| Anzahl | INTEGER | ja |  |  |
| Datei | TEXT | ja |  |  |
| Inhalt | TEXT | ja |  |  |
| Sha256 | TEXT | ja |  |  |
| SummeCent | INTEGER | ja |  |  |
| Von | TEXT | ja |  |  |

## Umsaetze

Klasse: `VibeVHS.Domain.Finanzen.Umsatz`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| BetragCent | INTEGER | ja |  |  |
| Buchungstag | INTEGER | ja |  |  |
| EndToEndId | TEXT |  | Index |  |
| ErzeugterBelegId | BLOB |  |  |  |
| GegenIban | TEXT |  |  |  |
| GegenName | TEXT |  |  |  |
| KontoauszugId | BLOB | ja | Index |  |
| Nr | INTEGER | ja |  |  |
| Rueckgabegrund | TEXT |  |  |  |
| Sicherheit | TEXT(40) | ja |  |  |
| Status | TEXT(40) | ja |  |  |
| Valuta | INTEGER |  |  |  |
| Verwendungszweck | TEXT(500) | ja |  |  |
| VorschlagBelegId | BLOB |  |  |  |
| VorschlagText | TEXT |  |  |  |
| ZugeordnetAm | INTEGER |  |  |  |
| ZugeordnetBelegId | BLOB |  |  |  |
| ZugeordnetVon | TEXT |  |  |  |

## Veranstaltungen

Klasse: `VibeVHS.Domain.Programm.Veranstaltung`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AusSerie | INTEGER | ja |  |  |
| Ausgefallen | INTEGER | ja |  |  |
| AusgefallenAm | INTEGER |  |  |  |
| Ausnahme | INTEGER | ja |  |  |
| Beginn | INTEGER | ja | Index |  |
| Bemerkung | TEXT(500) |  |  |  |
| DozentId | BLOB |  | Index |  |
| Ende | INTEGER | ja |  |  |
| KursId | BLOB | ja | → Kurse, Index |  |
| Nummer | INTEGER | ja |  |  |

## Vorlagen

Klasse: `VibeVHS.Domain.Vorlagen.Textvorlage`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Schluessel | TEXT(60) | ja | PK |  |
| Anredestil | TEXT(20) | ja |  |  |
| Betreff | TEXT(300) | ja |  |  |
| Bezeichnung | TEXT(200) | ja |  |  |
| GeaendertAm | INTEGER | ja |  |  |
| GeaendertVon | TEXT(200) | ja |  |  |
| Text | TEXT(8000) | ja |  |  |

## Warenkoerbe

Klasse: `VibeVHS.Domain.Shop.Warenkorb`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| AbgeschlossenAm | INTEGER |  |  |  |
| ErstelltAm | INTEGER | ja |  |  |
| KontoId | BLOB |  |  |  |
| Positionen | TEXT | ja |  | JSON |
| ReserviertBis | INTEGER | ja |  |  |
| Status | TEXT(40) | ja | Index |  |

## Zahlungslaeufe

Klasse: `VibeVHS.Domain.Finanzen.Zahlungslauf`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| Ausfuehrung | INTEGER | ja |  |  |
| ErstelltAm | INTEGER | ja |  |  |
| ErstelltVon | TEXT | ja |  |  |
| ExportiertAm | INTEGER |  |  |  |
| FreigegebenAm | INTEGER |  |  |  |
| FreigegebenVon | TEXT |  |  |  |
| NachrichtId | TEXT(35) | ja |  |  |
| Posten | TEXT | ja |  | JSON |
| Status | TEXT(40) | ja |  |  |
| Xml | TEXT |  |  |  |

## Zugangstokens

Klasse: `VibeVHS.Domain.Durchfuehrung.Zugangstoken`

| Spalte | Typ | Pflicht | Schlüssel/Verweis | Einstufung / Hinweis |
|---|---|---|---|---|
| Id | BLOB | ja | PK |  |
| Art | TEXT(40) | ja |  |  |
| ErstelltAm | INTEGER | ja |  |  |
| Hash | TEXT(64) | ja | eindeutig |  |
| PersonId | BLOB | ja | Index |  |
| Ursprung | BLOB |  |  |  |
| WiderrufGrund | TEXT(300) |  |  |  |
| WiderrufenAm | INTEGER |  |  |  |
| ZuletztGenutzt | INTEGER |  |  |  |
