# VibeVHS

Webbasierte Verwaltungssoftware für Volkshochschulen: einfach zu bedienen für Mitarbeitende, Dozierende und
Teilnehmende, eingebunden in die kommunale Infrastruktur, DSGVO-konform, mit Schnittstellen zu Abrechnungsstellen
(z. B. BAMF-Integrationskurse) und automatisierten, immer protokollierten Routineabläufen.

## Konzepte

| Dokument | Link |
|---|---|
| Anforderungen | https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG |
| Architektur und Datenmodell | https://claude.ai/artifact/XVL9kYS5XcAYwGsoDQ4CN3 |
| Umsetzungsfahrplan | https://claude.ai/artifact/FFRgE1BfKbphyCYxcVz4S9 |
| Klick-Prototyp (online) | https://claude.ai/artifact/KR88btCTssLuRhCTYFGnbx |
| Klick-Prototyp (lokal) | [`prototyp/prototyp-verwaltung.html`](../prototyp/prototyp-verwaltung.html) |

## Voraussetzungen

- .NET 11 SDK (`winget install --id Microsoft.DotNet.SDK.11 -e`)
- Git

Docker wird erst für die Bereitstellung benötigt (siehe `deploy/`).

## Starten

```powershell
cd C:\repos\vibevhs
dotnet run --project src\VibeVHS.Web --launch-profile http
```

Dann im Browser <http://localhost:5189> öffnen und bei der **Testanmeldung** ein Konto wählen. Wer mehrere Rollen hat,
wechselt die aktive Rolle oben rechts (Leitung, Verwaltungsleitung, Fachbereichsleitung, Teilnehmerservice,
Buchhaltung, Integrationskurs-Team, Systemverwaltung).

Beim ersten Start wird die SQLite-Datenbank `src/VibeVHS.Web/data/vibevhs.db` angelegt (WAL-Modus, Migrationen
werden automatisch eingespielt). Danach werden die **Startdaten** der Pilot-VHS geladen (idempotent, nur was fehlt):
Briefkopf, Fachbereiche 1 bis 8 und die Benutzerkonten samt zugehöriger Person (Rolle Mitarbeitende).
In der Entwicklungsumgebung kommen fünf fiktive Beispielpersonen dazu, darunter Anna Becker mit Namenshistorie.

In der Entwicklungsumgebung legt die Anwendung beim ersten Start außerdem **3.000 fiktive Testpersonen** an
(fester Startwert, `Demodaten:Massendaten` in `appsettings.Development.json`): Rollen, Status, Fachbereiche,
Adressen, Rufnummern, Einwilligungen, Organisationen, Vollmachten und einige absichtlich angelegte Dubletten.
Dasselbe für eine beliebige Datenbankdatei:

```powershell
dotnet run --project tools\Testdaten -- 3000
```

Zurücksetzen: Anwendung beenden und den Ordner `src/VibeVHS.Web/data` löschen.

## Bauen und testen

```powershell
dotnet build VibeVHS.slnx
dotnet test --solution VibeVHS.slnx
```

Tests laufen mit xUnit v3 auf Microsoft.Testing.Platform (eingestellt in `global.json`).

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/VibeVHS.Domain` | Fachkern ohne Abhängigkeiten: bitemporale Versionen (`BitemporaleVersion`, `ZeitlicheHistorie.AlsStichtag`), `Geld` in Cent, GUID v7, `Person` mit versioniertem Namen |
| `src/VibeVHS.Application` | Anwendungsfälle und Ports: Rollen, `Freigabe` (gesperrt immer mit Begründung), `NaechsterSchritt`, Postausgang (Outbox) mit Übersetzungslink, Audit-Protokoll, Personendienst |
| `src/VibeVHS.Infrastructure` | EF Core + SQLite (WAL), Migrationen, Speicher, Quartz-Job für die Zustellung |
| `src/VibeVHS.Adapters.Mocks` | Mock-Anmeldung (Konto + Rolle), simulierte Mail- und Schnittstellenkanäle |
| `src/VibeVHS.Web` | Blazor Web App (Interactive Server, Radzen), deutsche Oberfläche |
| `tests/*` | xUnit v3 + Shouldly |
| `tools/Testdaten` | Testdaten-Generator (Bogus, fester Startwert) |
| `deploy/` | Docker (folgt) |

## Leitlinien in der Oberfläche

- **Gesperrt heißt erklärt:** Nicht klickbare Schaltflächen (`BegruendeterButton`) und nicht editierbare Felder
  (`GesperrtesFeld`) zeigen bei Maus und Tastaturfokus einen Tooltip mit dem Grund.
- **Nächste sinnvolle Schritte:** Nach jeder Aktion bietet der Baustein `NaechsteSchritte` die naheliegenden Folgeschritte an.
- **Nichts geht unbemerkt raus:** Jede Mail und jeder Schnittstellenaufruf läuft über den Postausgang (Outbox),
  wird gemeinsam mit der fachlichen Änderung gespeichert, per Hintergrundjob zugestellt und protokolliert.
- **Übersetzungslink:** Jede Kundenmail enthält „Diese Nachricht in Ihrer Sprache lesen“ in der Wunschsprache.
- **Nichts wird überschrieben:** Zeitlich veränderliche Daten sind bitemporal (gültig von/bis, erfasst/ersetzt).
  Korrektur (rückwirkend) und Änderung ab Datum sind getrennte Vorgänge.

## Bibliotheken

Erlaubt: Radzen.Blazor, EF Core + SQLite, Serilog, Quartz.NET, xUnit, Shouldly, Bogus, Verify.
Nicht verwenden: QuestPDF, Nager.Date, MediatR, AutoMapper, FluentAssertions.
Versionen zentral in `Directory.Packages.props`.

## Personen: Stammdaten und Datenhistorie

Alle Stammdaten einer Person sind bitemporal versioniert: Name, Geburtsdatum, Rollen (Teilnehmende, Dozierende,
Mitarbeitende), bevorzugte Sprache, Postadressen, Telefon-/Messaging-Nummern (Art, Dienste wie SMS/Signal/WhatsApp,
Einwilligung, bevorzugt) und E-Mail-Adressen (Art, bevorzugt). Die Kennzeichen „üblicher Aufenthaltsort“ und
„Standard-Korrespondenzadresse“ sind selbst zeitlich gültig und wechseln unabhängig voneinander (z. B. c/o-Adresse).

- **Detailseite** `/personen/{id}`: kompakter Stand heute, je Feld ein Uhr-Symbol in die Historie, Namensänderung.
- **Datenhistorie** `/personen/{id}/historie`: Zeitleiste über alle Felder (neueste Erfassung oben) mit Filter-Chips je
  Datenart; je Eintrag alt → neu, gültig ab/bis, erfasst am/von, Art (Ersterfassung, Änderung ab Datum, Korrektur,
  Beendet) und Grund/Nachweis; ersetzte Erfassungen blass. Dazu die Stichtagsabfrage „Wie war der Stand am … / was war
  bekannt am …“ für den gesamten Datensatz.
- Demodaten: Olena Kovalenko (Umzug, c/o-Korrespondenzadresse, Signal-Einwilligung), Anna Becker (Korrektur, Heirat).

## Personenliste, Dubletten, Organisationen, Vollmachten, Einwilligungen (I1)

- **Personenliste** `/personen`: Suche zuerst (FTS5, normalisierte Schreibweisen), Filter-Chips mit Trefferzahlen,
  virtuelles Scrollen mit Keyset-Nachladen in 50er-Blöcken, „Alle n Treffer anschreiben“ und Excel-Export.
  Aufbau und gemessene Ladezeiten: [personenliste-ladezeiten.md](personenliste-ladezeiten.md).
- **Dubletten**: Prüfung beim Anlegen (gleiches Geburtsdatum + ähnlicher normalisierter Name), Hinweis in Liste und
  Personenseite, Vergleichsansicht `/personen/dubletten` mit Zusammenführen (beide Historien bleiben, protokolliert)
  oder „Keine Dublette“.
- **Organisationen** (Firma, Behörde, Jobcenter …) mit zeitlich gültiger Zugehörigkeit von Personen.
- **Buchungsvollmachten**: Person oder Organisation darf eine Person buchen – mit Zeitraum und Nachweis.
- **Einwilligungen** je Kanal (E-Mail, SMS, Messenger, Telefon, Brief) und Zweck (Kursorganisation, Werbung).
- Alles davon ist bitemporal und erscheint in der Datenhistorie der Person.

## Programm (I2)

- **Semester, Fachbereiche 1–8, Kursarten** (Nutzungsart, benötigte Merkmale, Vorlage der Buchungsrichtlinie) und
  **Kurse** mit Dozierendenteam, Sichtbarkeit (öffentlich/intern/geschlossen mit Auftraggeber), Buchungsrichtlinie je
  Buchungsweg (Tresen, Integrationskurs-Team, Online, Auftraggeber; Datenmodell für Kontingent und Vorab-Einbuchung)
  und zeitlich gültigem Entgelt.
- **Schnellweg** `/kurse/neu`: regelmäßig, gleicher Ort, gleiche Dozentin – mit Vorschau (ausgelassene Ferien- und
  Feiertage mit Grund, Konflikte). Ungeeignete Orte sind gesperrt, der Tooltip nennt den Grund (fehlende Kapazität für
  die Nutzungsart, fehlendes oder abgelaufenes Merkmal, nicht buchbare Ebene).
- **Kursumfang** begrenzt (Anzahl oder Enddatum) oder **unbefristet**: rollierend per Quartz-Job, Horizont 12 Wochen.
- **Änderungen** an Terminen: nur diese / alle folgenden (einzeln geänderte bleiben) / Einzelveranstaltung / Ausfall.
- **Feiertage NRW** werden selbst berechnet (Osterformel), **Schulferien** sind eine pflegbare Tabelle (`/ferien`;
  Demodaten bitte mit dem Ferienkalender des Schulministeriums NRW abgleichen).
- **Orte** `/orte`: Liegenschaft › Gebäude › Raum, Treffpunkte, virtuelle Räume; Kapazität je Nutzungsart; pflegbarer
  Merkmalskatalog mit Vererbung, Ausschlussgruppen (barrierefrei/nicht barrierefrei), Nachweis und Gültigkeit;
  automatische Wegbeschreibung mit Kartenlink. Läuft eine befristete Zulassung in 60 Tagen ab, legt ein täglicher Job
  eine Aufgabe an (Aufgabenmodul mit Körben folgt in I5).
- **Hybride Veranstaltungen**: mehrere Orte je Termin, Kapazität = Summe (Ort/Nutzungsart oder eigene Höchstzahl),
  höchstens die Höchstzahl des Kurses.
- **Raumplan** `/raumplan`: Wochenansicht je Standort, Konflikte rot mit Erklärung.
- **Globale Suche** in der Kopfzeile: Personen, Kurse, Orte.
- **Semesterkopie** mit Verschiebung um ganze Wochen; Kopien sind Entwürfe mit neuer Kursnummer.
- Demodaten: drei Standorte (Emsdetten, Greven, Saerbeck; echte Ortsnamen, fiktive Häuser und Adressen), zwei
  Online-Räume, ein Treffpunkt, 40 Kurse über alle Fachbereiche, darunter Integrationskurse, ein hybrider Vortrag und
  ein unbefristeter Kurs mit 10er-Karte.

## Datenschutz (I2a)

- **Zustände einer Person**: aktiv → archiviert (ausgeblendet, umkehrbar) → gesperrt (Art. 18, solange eine
  Aufbewahrungsfrist läuft; Zugriff nur Buchhaltung und Integrationskurs-Team, jeder Zugriff wird protokolliert) →
  anonymisiert. Referenzierte Datensätze werden nie gelöscht, sondern **an Ort und Stelle anonymisiert**: eigener
  Platzhalter je Person in allen bitemporalen Versionen, Kontaktdaten, Einwilligungen, Vollmachten, Suchindex,
  Postausgang und Dozierendenangaben in Kursen. Übrig bleiben nur Altersgruppe, Geschlecht und Kreis (Statistik).
- **Protokoll ohne Klarnamen**: Einträge nennen nur Pseudonyme („Person #XXXXXXXX“); die Anzeige löst den aktuellen
  Namen auf, nach Anonymisierung den Platzhalter.
- **Archivieren von Semestern und Kursen** (`/datenschutz`, Kursdetail): schreibgeschützt, ausgeblendet, Filter
  „archivierte Kurse zeigen“, wiederherstellbar. Kriteriensuche: Kursende vor Datum, Semester älter als n Jahre,
  Personen ohne Aktivität seit n Monaten.
- **Aufbewahrungsfristen** sind eine pflegbare Tabelle. Die mitgelieferten Werte (z. B. Buchhaltung 10 Jahre ab
  Jahresende nach § 147 AO / § 257 HGB) sind **Vorschläge** und müssen mit der/dem Datenschutzbeauftragten abgestimmt
  werden.
- **Löschläufe**: monatlich erzeugt ein Quartz-Job (oder man selbst) eine Vorschau; ausgeführt wird erst nach zwei
  Freigaben durch zwei verschiedene Personen (Verwaltungsleitung, Leitung). Das Löschprotokoll enthält nur Zahlen.
- **Art. 17 Löschverlangen** auf der Personenseite: Prüfung zeigt, was gelöscht und was wegen laufender Frist gesperrt
  wird; das Antwortschreiben landet im Postausgang.
- **Art. 15 Auskunft** als PDF (MigraDoc) und JSON: `/personen/{id}/auskunft.pdf` bzw. `.json`.
- Hinweis zu den Demodaten: Die Massendaten enthalten viele lange inaktive Personen; ein Löschlauf mit 36 Monaten
  anonymisiert davon über tausend. Zum Ausprobieren eine eigene Datenbank verwenden.

## Testbenutzer der Pilot-VHS

Die Startdaten liegen in [`tools/Testdaten/testbenutzer-vhs-egs.json`](../tools/Testdaten/testbenutzer-vhs-egs.json)
(Volkshochschule Emsdetten Greven Saerbeck, NRW; Orte Emsdetten, Greven, Saerbeck).

- Die Testbenutzer stammen von der **öffentlichen Teamseite** der vhs (<https://www.vhs-egs.de/ueber-uns/team-neu>,
  abgerufen am 15.11.2026). Übernommen sind **nur Name, Funktion und Zuständigkeit**, keine E-Mail-Adressen oder
  Telefonnummern. Das Testsystem verschickt ohnehin nichts, sondern legt alles in den Postausgang.
- Die **Rollenzuordnung ist abgeleitet** und muss von Klaus bzw. der Verwaltungsleitung bestätigt werden.
- Fachbereich 6 heißt auf der Teamseite einmal „Informationstechnologie“ und einmal „Beruf und Wirtschaft“;
  hier zusammengefasst zu „Beruf, Wirtschaft und Informationstechnologie“.
- „Was steht an?“ zeigt der Fachbereichsleitung nur Einträge ihrer eigenen Fachbereiche.

## Später: Testsystem auf dem Server

Ziel: <https://liebler.iui.hs-osnabrueck.de/vibevhs> (Pfad-Basis `/vibevhs`, Umgebung `Testsystem`).
Vor der Testanmeldung schützt ein **gemeinsames Zugangspasswort am Webserver (Basic Auth)**, damit niemand von außen
ohne Passwort die Konten der echten Teammitglieder auswählen kann. Vorlagen (systemd, nginx mit Basic Auth,
Ausrollskript) liegen in `deploy/`; umgesetzt wird das in einem späteren Schritt.

## Nachgetragene Anforderungen

- **Allgemeine Suche umfasst auch Orte** (zusätzlich zu Personen, Kursen und Kursnummern). Die Suche folgt in einer
  späteren Iteration; der Platzhalter in der Kopfzeile nennt Orte bereits.

## Offene Punkte nach Iteration 0

- Docker-Image und `docker-compose.yml` mit Mailpit (bewusst zurückgestellt, zuerst nativer Betrieb)
- Echte Anmeldung über die kommunale Benutzerverwaltung (OIDC/SAML) statt Testanmeldung
- Bereitstellung auf dem Testserver (siehe oben)
- Übersetzungsseite `/uebersetzen/{id}` ist ein Platzhalter; Übersetzungsdienst noch nicht angebunden
- Briefe mit QR-Code „Translate this letter in your language“ (Dokumenterzeugung folgt)
- Stammdaten bearbeiten (außer Namen), Rollen von Personen, Kurse, Anmeldung per QR-Code
