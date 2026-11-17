# VibeVHS

Webbasierte Verwaltungssoftware für Volkshochschulen: einfach zu bedienen für Mitarbeitende, Dozierende und
Teilnehmende, eingebunden in die kommunale Infrastruktur, DSGVO-konform, mit Schnittstellen zu Abrechnungsstellen
(z. B. BAMF-Integrationskurse) und automatisierten, immer protokollierten Routineabläufen.

Alle geplanten Schnittstellen nach außen (Mail, Messaging, VHS-Systeme, Stadtverwaltung, Finanzen, BAMF) mit Stand:
[schnittstellen.md](schnittstellen.md).

## Konzepte

| Dokument | Link |
|---|---|
| Anforderungen | https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG |
| Architektur und Datenmodell | https://claude.ai/artifact/XVL9kYS5XcAYwGsoDQ4CN3 |
| Umsetzungsfahrplan | https://claude.ai/artifact/FFRgE1BfKbphyCYxcVz4S9 |
| Klick-Prototyp (online) | https://claude.ai/artifact/KR88btCTssLuRhCTYFGnbx |
| Buchungs-Shop-Plugin | https://claude.ai/artifact/VsQFogXRz3dzQYZajLgoMm |
| Klick-Prototyp (lokal) | [`spezifikation/prototyp-verwaltung.html`](spezifikation/prototyp-verwaltung.html) |

Schnappschuss aller Konzepte vom 17.11.2026 im Repo: [`spezifikation/`](spezifikation/README.md). Bei Abweichung gilt die lebende Fassung (Links oben).

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

Beim ersten Start wird die SQLite-Datenbank `src/VibeVHS.Web/data/vibevhs.db` neu aus dem Modell angelegt (WAL-Modus,
lesbare Sichten `v_…`, Schemaversion; derzeit ohne Migrationen – bei geändertem Modell die Datei löschen, siehe docs/betrieb.md). Danach werden die **Startdaten** der Pilot-VHS geladen (idempotent, nur was fehlt):
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

Datenbankabfragen müssen eindeutig sortiert sein, wenn sie begrenzen (`Skip`/`Take`) oder den ersten Treffer ohne
Filter holen (`First`/`FirstOrDefault`); Listen mit Nachladen sortieren zusätzlich nach `Id` (die Personenliste über
den Sortierschlüssel, der die Id enthält). In Entwicklung und Tests bricht eine solche Abfrage ab
(`Abfragewarnungen.Konfigurieren`, EF-Warnungen `RowLimitingOperationWithoutOrderBy` und
`FirstWithoutOrderByAndFilter` auf Throw), im Betrieb wird sie nur protokolliert.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/VibeVHS.Domain` | Fachkern ohne Abhängigkeiten: bitemporale Versionen (`BitemporaleVersion`, `ZeitlicheHistorie.AlsStichtag`), `Geld` in Cent, GUID v7, `Person` mit versioniertem Namen |
| `src/VibeVHS.Application` | Anwendungsfälle und Ports: Rollen, `Freigabe` (gesperrt immer mit Begründung), `NaechsterSchritt`, Postausgang (Outbox) mit Übersetzungslink, Audit-Protokoll, Personendienst |
| `src/VibeVHS.Infrastructure` | EF Core + SQLite (WAL), Schema per EnsureCreated, Speicher, Quartz-Job für die Zustellung |
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
  eine Aufgabe an (siehe Aufgaben, I5).
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

## Anmeldung (I3)

- **Tresen-Assistent** `/anmeldung` in drei Schritten: Person (mit Dublettenhinweis und Wahl „wer meldet an“ aus den gültigen
  Vollmachten) → Kurs (jede Zeile mit Buchungsprüfung; nicht buchbare Kurse sind gesperrt, der Tooltip nennt Grund und Weg) →
  Zahlung (Zahlart, eingefrorenes Entgelt, Ermäßigung mit Nachweis). Danach: nächste sinnvolle Schritte.
- **Buchungsprüfung** (`Domain/Anmeldung/Buchungspruefung.cs`): ein Dienst für alle Wege (Tresen, Integrationskurs-Team, online,
  Auftraggeber) mit Begründungscode: archiviert, abgesagt, Entwurf, beendet, Person nicht aktiv, nur öffentlich (Selbstregistrierte,
  FA-62), intern ohne Berechtigung, Weg nicht erlaubt, geschlossen/Zielgruppe, Vollmacht fehlt/abgelaufen, bereits angemeldet/wartend,
  ausgebucht → Warteliste (am Tresen mit Überbuchungsrecht).
- **Belegung** speichert wer für wen über welchen Weg mit welcher Vollmacht gebucht hat, das Entgelt zum Buchungszeitpunkt
  (eingefroren) und eine Ermäßigung mit Nachweis.
- **Warteliste**: wird ein Platz frei, bekommt die erste Person ein Nachrückangebot mit Annahmefrist (Mail). Ein stündlicher Job lässt
  abgelaufene Angebote verfallen und bietet den Platz der nächsten Person an.
- **Storno** nach den AGB-Fristen der Richtlinie (rechtzeitig kostenlos bzw. Bearbeitungsgebühr, danach bleibt das Entgelt fällig);
  **Umbuchung** mit Vorschlägen (gleiche Kursart oder gleicher Fachbereich, nur buchbare Kurse).
- **Kontingente** (z. B. Pilates 10er-Karte): Vorab-Einbuchung je Veranstaltung mit Kapazitätsprüfung je Ort (auch hybrid),
  Warteliste je Veranstaltung mit automatischem Nachrücken, Austragen mit Frist, Nichterscheinen-Regel je Kurs. Der Kontingentstand
  (gekauft, besucht, verfallen, eingebucht, verbleibend) ist immer die Summe unveränderlicher Ereignisse.
- **Bestätigungsmails** in den Postausgang, mit Übersetzungslink in der bevorzugten Sprache und Kalenderdatei (ICS) als Anhang.
  Alle Texte sind **Vorlagen** (`/vorlagen`, Pflege durch Verwaltungsleitung/Leitung) mit Platzhaltern und Vorschau.
- Anwesenheit (besucht/nicht erschienen) kann bis zur QR-Anwesenheit (I4) auf der Belegung von Hand festgehalten werden.
- Demodaten: Belegungen für alle veröffentlichten Kurse, Yoga am Abend voll mit Warteliste (Sabine Weber auf Platz 1),
  Progressive Muskelentspannung mit laufendem Nachrückangebot, Spanisch A1 unter Mindestzahl, abgesagtes Französisch B1 mit
  Teilnehmenden, Integrationskurse über das Integrationskurs-Team, Firmenkurs über die Stadtwerke, Pilates mit 23 Kontingenten.
- Abnahme der Phase A: Klick-Drehbuch in [`abnahme-a.md`](abnahme-a.md).

## Dozierendenmerkmale (FA-120 bis FA-124)

- **Beschäftigungsart** je Dozent/in, zeitlich gültig: festangestellt (Arbeitszeit) oder freiberuflich (Honorar). Ein Wechsel gilt
  ab Datum; frühere Einsätze behalten die damalige Art. Sichtbar auf der Personenseite und in der Datenhistorie.
- **Qualifikationen** aus dem gemeinsamen Merkmalskatalog (Bezug „Person“) mit Wert (z. B. Zulassungsart), Nachweis, ausstellender
  Stelle und Gültigkeit: BAMF-Lehrkraftzulassung, Zusatzqualifikation Alphabetisierung, Prüferlizenz telc/Goethe,
  Übungsleiterlizenz, Erste-Hilfe-Nachweis, erweitertes Führungszeugnis (Vorschläge, unter `/qualifikationen` erweiterbar).
- **Kursarten verlangen Merkmale** an Ort und Dozierenden (`/qualifikationen`); Kurse übernehmen sie und können ergänzen
  (z. B. Alphabetisierung). Ein **Prüfdienst** liefert je Veranstaltungstag einen Begründungscode (geeignet, Ausnahme, fehlt,
  abgelaufen am …, gilt erst ab …).
- **Gesperrte Auswahl** beim Kurs anlegen, im Dozierendenteam und beim Ändern von Terminen: ungeeignete Personen sind
  ausgegraut, der Tooltip nennt den Grund. Die Fachbereichsleitung kann mit Begründung eine protokollierte **Ausnahme**
  erteilen – nicht bei als „gesetzlich“ markierten Merkmalen (BAMF-Zulassung, Alpha, Führungszeugnis).
- **Aufgaben** (täglicher Job, Korb Fachbereichsleitung): Qualifikation läuft bald ab (Vorlauf je Merkmal), Nachweis fehlt,
  künftige Veranstaltungen mit ungeeigneter Person. Betroffene Veranstaltungen sind im Kurs rot markiert.
- **Lehrfreigabe Sprachkurs** (Eignungsprüfung/Hospitation der vhs, Merkmal `sprachfreigabe`): je Lehrkraft für eine oder
  mehrere Sprachen (Mehrfachauswahl). Anforderungen können einen Wert verlangen (`sprachfreigabe=Englisch`). Verlangt die
  Kursart die Freigabe allgemein, bestimmt jeder Kurs die Sprache aus dem Titel („Englisch A2“ → Englisch, „Deutsch B1“ →
  Deutsch (DaF/DaZ)); im Kurs änderbar („Sprache des Kurses“). Begründung z. B. „liegt nur für Italienisch vor, nicht für
  Spanisch“. Interne Freigabe – Ausnahme der Fachbereichsleitung möglich (anders als BAMF-Zulassung). Zusätzlich im Katalog:
  „Qualifikation DaF/DaZ“. Bestehende Datenbanken: Kursarten mit „Sprach“ im Namen verlangen die Freigabe ab dem Update.
- Demodaten: rund jede fünfte Lehrkraft festangestellt, Anna Becker wechselt zum 01.01.2026 in die Festanstellung;
  Integrationskurs-Lehrkräfte mit BAMF-Zulassung, eine läuft in 6 Wochen ab (Anschlusskurs 26H-405 ist ab Januar markiert),
  eine Deutsch-Lehrkraft ohne Zulassung (bei Integrationskursen gesperrt).

## Durchführung (I4): Anwesenheit, Check-in, Bescheinigungen, Briefe

- **Anwesenheitsliste** je Veranstaltung (`/anwesenheit`, Tagesübersicht; Dozierende sehen nur eigene Termine), minuten- und
  UE-genau (45 Min.). Nach Ende gilt jede Änderung als **Nacherfassung** mit Pflichtgrund (protokolliert). „Liste abschließen“
  (oder nachts automatisch) vermerkt Nichterfasste als nicht anwesend; bei Kontingenten greift die Nichterscheinen-Regel.
- **Check-in – immer nur ein Scan**:
  - **A, Teilnehmende**: Die Anmeldebestätigung enthält einen persönlichen Check-in-Link (zufälliges, widerrufbares Token, keine
    Personen-ID). Auf dieser Seite tippt man „Einchecken“, die Kamera (html5-qrcode) liest den **Termin-QR** (Aushang/Beamer)
    und Personen-Token + Termin-Code gehen gemeinsam an den Server. Komfort: „Gerät merken“ (Cookie) – dann genügt auch die
    normale Kamera-App; mehrere gemerkte Personen (Familie) sind auswählbar. Ohne gemerkte Person bittet die Seite, den Link zu öffnen.
  - **B, Kursleitung**: scannt auf der Anwesenheitsliste persönliche QR-Ausweise (Ausweis-PDF oder Bild in der Bestätigungsmail);
    der QR enthält nur ein Pseudonym-Token.
  - **C**: Liste abhaken.
  - Geprüft wird jedes Mal mit Begründung: Person im Kurs, bei Kontingent eingebucht, richtiger Ort (Online-Teilnahme separat in
    der Liste, kein Termin-QR für virtuelle Räume), Zeitfenster (Termin-Code: zufälliger Verweis, 30 Min. vor bis 30 Min. nach
    Beginn), doppelter Check-in.
  - Kursweise einstellbar: Selbst-Check-in erlaubt / **mit Bestätigung durch die Kursleitung** (Standard bei Integrationskursen) /
    aus. Alle Zugänge (Link, Geräte, Ausweise) sind auf der Personenseite (Bereich Datenschutz) sperrbar; die Anonymisierung
    entwertet sie. Gespeichert werden nur Hashes.
- **Bescheinigungen** (Teilnahme, Bildungsurlaub nach AWbG) mit PDFsharp/MigraDoc nach Stichtagsregel: Name der Person und der
  Kursleitung wie zum Kurszeitpunkt, Anschrift aktuell. **Archiv** je Dokument: PDF, eingefrorener JSON-Datenstand, SHA-256.
  Nachdruck liefert das Original nach Hash-Prüfung; Neuausstellung nur bewusst mit Begründung (FBL, Verwaltungsleitung, Leitung).
- **Jeder Brief** (Bescheinigung, Teilnehmerausweis, Auskunft Art. 15) trägt den QR-Code „Translate this letter in your language“
  (QRCoder) zur Übersetzungsseite `/brief/{token}` (ohne Login, nicht erratbares Token, 180 Tage gültig). Übersetzt wird in
  Stufe 1 durch einen Mock-Adapter (Platzhalter in der Zielsprache); dasselbe gilt für Mails (`/uebersetzen/…`).
- Dokumentarchiv je Person (Personenseite) und je Belegung; eingebunden in Auskunft, Anonymisierung (Inhalt gelöscht, Hash bleibt)
  und Aufbewahrungsfristen (Bescheinigungen).
- Demodaten: Anwesenheiten aller vergangenen Termine (Listen abgeschlossen), Testkonto der Pilates-Kursleitung mit Rolle
  „Dozierende“, Kurs 25F-501 (Frühjahr 2025, Kursleitung damals Anna Schmidt) mit archivierter Bescheinigung für Olena Kovalenko,
  Zwischenbescheinigung für Mehmet Yılmaz, Bildungsurlaub 26H-602.

## Aufgaben (I5): Gruppenkörbe, Fristen, Eskalation, Routinen

- **Aufgabenregeln** `/aufgaben?ansicht=regeln` (ändern: Leitung, Systemverwaltung): je Aufgabentyp zuständige Rolle,
  Einschränkung auf den Fachbereich des Anlasses, optional eine feste Person (bekommt die Aufgabe direkt zugewiesen),
  Eskalation an die Leitung *n* Tage nach Fälligkeit, Vorlauf für Prüfregeln und – nur bei kritischen Automatismen –
  „ein Mensch gibt frei“ oder „Automatik handelt bei Fälligkeit selbst“ (QA-32).
- **Gruppenkorb** = aktive Rolle (Fachbereichsleitungen nur ihre Fachbereiche). **Übernehmen** legt die Aufgabe in die
  persönliche Liste, die Kolleg:innen sehen unter „Bei Kolleg:innen“, wer sie bearbeitet. **Zuweisen** an Personen mit der
  zuständigen Rolle, **Zurück in den Korb**, **Erledigt** mit Vermerk. Gesperrte Schaltflächen erklären per Tooltip, warum.
- **Regeln gleichen ab**: Jede Regel liefert ihre aktuellen Anlässe; neue werden angelegt, bestehende aktualisiert,
  entfallene **automatisch geschlossen** (mit Grund), wiederkehrende wieder geöffnet. Von Menschen Erledigtes kommt nicht wieder.
  Abgelaufene, aber nicht verlängerte Zulassungen/Qualifikationen bleiben offen.
- Aufgabentypen: Mindestteilnehmerzahl (FA-07), Qualifikation läuft ab / Nachweis fehlt / Einsatz ohne Qualifikation,
  Merkmal bzw. BAMF-Zulassung eines Orts läuft ab, Löschlauf freigeben, fehlgeschlagene Nachrichten.
- **Mindestteilnehmerzahl**: X Tage (Standard 10) vor Kursbeginn; Aufgabe „absagen oder durchführen?“ im Korb des
  Fachbereichs, fällig 3 Tage vor Beginn. „Kurs absagen“ storniert alle Belegungen ohne Gebühr, lässt künftige Termine
  ausfallen und schickt die Absage (Vorlage `kurs.absage`, mit Übersetzungslink).
- **Zeitplaner (Quartz)**: stündlich Mindestzahl, Wartelisten-Nachrücken, Postausgang, Löschläufe, Eskalation; täglich 7 Uhr
  Erinnerung vor Kursbeginn (`Automatik:ErinnerungTageVorher`, Standard 2; Vorlage `kurs.erinnerung`, je Belegung einmal);
  nachts Qualifikationen und Ortsmerkmale.
- **Startseite „Was steht an?“**: Meine Aufgaben, Gruppenkorb, Bei Kolleg:innen, Heute automatisch erledigt; für die
  Leitung zusätzlich „Überfällig in den Körben“ und eskalierte Aufgaben. Ein Klick auf den Titel führt zur passenden Seite.
- **Protokoll**: Anlegen, Übernahme, Zuweisung, Erledigung, Eskalation und automatisches Schließen. Es nennt nur Aufgabentyp
  und Kurzkennung (`[A-1234abcd]`), weil Titel Namen enthalten können. Bei der Anonymisierung einer Person werden ihre
  Aufgaben neutralisiert und geschlossen.
- **Abwesenheit und Vertretung (QA-44)** `/aufgaben?ansicht=abwesenheit`: Zeitraum, Vertretung (muss eine gemeinsame Rolle haben und
  darf nicht selbst abwesend sein), optionaler Hinweis ohne Gesundheitsdaten. Während der Abwesenheit sieht die Vertretung die
  persönlichen Aufgaben unter „In Vertretung“ (Startseite und Aufgaben), Zuweisungen an die abwesende Person und Aufgaben einer
  festen Person gehen an die Vertretung; ohne Vertretung bleiben neue Aufgaben im Gruppenkorb. Die Auswahl beim Zuweisen zeigt
  Abwesenheiten. „Wieder da“ beendet die Vertretung. Alles protokolliert.

## Finanzen (I6) und GoBD (FA-30 bis FA-35, FA-126 bis FA-133)

Seite `/finanzen` (Buchhaltung, Verwaltungsleitung, Leitung buchen; Teilnehmerservice liest und erfasst Mandate; Rolle
**Prüfung** liest nur und exportiert – Testkonto `pruefung`). An jeder Person: Konto, SEPA-Mandat, Auszahlungskonto und
bei Kursleitungen die Honorarverträge.

- **Belege nach GoBD**: Rechnung, Gutschrift, Zahlungseingang/-ausgang, Mahnung, Honorarabrechnung, Storno. Die Nummer
  (`RE-2026-00042`) wird erst bei der Festschreibung gezogen, je Belegart und Jahr lückenlos (optimistische Sperre).
  Festgeschriebene Belege tragen Belegdatum, Erfassungszeitpunkt und SHA-256; sie werden nie geändert oder gelöscht –
  Korrektur nur per Storno mit Begründung und Verweis. Druckstücke liegen im Dokumentarchiv (Prüfsumme, Übersetzungs-QR).
- **Finanzprotokoll als Hash-Kette**: jede Änderung an Finanzdaten mit Zeitpunkt, Person, vorher/nachher (nur Pseudonyme);
  „Kette prüfen“ findet nachträgliche Änderungen.
- **Rechnungslauf** (täglich 6:30 und auf Knopfdruck): Rechnungen aus Belegungen, bei Abmeldung/Umbuchung Storno und ggf.
  Gebührenrechnung, Firmenkurse als Rechnung an den Auftraggeber mit **XRechnung 3.0 (UBL)**. Kostenträger-Belegungen
  (z. B. BAMF) rechnen andere Wege ab. Mails mit PDF-Anhang und Übersetzungslink.
- **Teilnehmerkonto**: Soll/Haben/Saldo, offene Posten je Rechnung, Teilzahlungen, Guthaben.
- **SEPA**: Mandate (IBAN-Prüfung, FRST/RCUR, Verfall nach 36 Monaten), Lastschriftvorschlag → Freigabe mit
  Vorabankündigung → **pain.008.001.08**; Überweisungen für Honorare **pain.001.001.09**. Gläubiger-ID und Konto sind
  Testwerte (Einstellungen).
- **Kontoauszug camt.053**: Einlesen mit Dublettenschutz, Zuordnungsvorschlag (eigene Referenz, Belegnummer + Betrag,
  Betrag + Name), Buchung erst nach Bestätigung („Alle sicheren buchen“), Rücklastschrift → Storno der Zahlung,
  Bankgebühr, Aufgabe „Rücklastschrift klären“, bei AC04/AC06/MD01 Mandat widerrufen. „Test: Kontoauszug simulieren“
  erzeugt einen passenden Auszug.
- **Mahnwesen**: Stufen mit Abstand und Gebühr (einstellbar), Vorschlag der Automatik, Freigabe durch die Buchhaltung;
  Rechnungen im laufenden Einzug werden nicht gemahnt; ein offener Vorschlag passt sich an Zahlungseingänge an.
- **Honorare als Plugins**: `IHonorarModell` (Kennung, Version, Parameterbeschreibung, `Berechne(HonorarKontext)`), rein
  und deterministisch. Eingebaut: „Honorar pro UE“ (Ausfallvergütung bei kurzfristiger Absage < 48 h) und „Basis pro
  Termin + pro UE + pro Teilnehmer“ (Mindest-/Höchstbetrag), beide mit Fahrtkosten (pauschal oder km). Der Kontext
  enthält nur anonyme Teilnahmezahlen. Der Kern verhindert Doppelabrechnung; an jeder Abrechnung stehen Modell, Version
  und Parameter als Snapshot. Weitere Modelle: DLL in `plugins/honorare` (eigener AssemblyLoadContext). Verträge mit
  Modellauswahl, erzeugtem Formular und Vorschau an der Person; neue Version beendet die alte am Vortag.
- **Übergabe an die Finanzsoftware** (Mock): Buchungsstapel als CSV (Platzhalter-Sachkonten, mit der Kämmerei
  abzustimmen), dokumentiert im Postausgang und Finanzprotokoll.
- **Prüfung (FA-132)**: ZIP mit `belege.csv`, `positionen.csv`, `finanzprotokoll.csv`, `index.xml`
  (Beschreibungsstandard GDPdU) und Verfahrensdoku-Bausteinen (FA-133, auch einzeln als Markdown).
- **Aufbewahrung (FA-130/131)**: Buchungsbelege 8 Jahre (Migration stellt den alten Vorschlag 10 → 8 um),
  Mahnungen als Geschäftsbriefe 6 Jahre, jeweils ab Jahresende. Belege sind Aufbewahrungsgründe im Löschlauf: gesperrt
  statt anonymisiert bis zum Ende der längsten Frist; danach werden Name, Anschrift, Mandate und Kontodaten entfernt,
  Nummer, Betrag und Prüfsumme bleiben.
- **Aufgaben**: Lastschriftlauf freigeben, Mahnvorschlag freigeben, Honorarentwürfe freigeben, Kontoauszug zuordnen,
  Rücklastschrift klären (Buchhaltung); Freigabe für Lastschrift und Mahnung je Regel einstellbar (QA-32).
- **Noch offen**: XSD-Prüfung der SEPA- und XRechnungsdateien gegen die offiziellen Schemata (Download der XSD-Dateien
  braucht Klaus' Zustimmung; bis dahin prüfen die Tests Struktur, Summen und Pflichtfelder), Kassenbuch (FA-36, Soll),
  automatische Erstattung von Guthaben, Entfernung je Ort für km-Fahrtkosten.

## Integrationskurse (I7, FA-40 bis FA-44, FA-134 bis FA-137)

Seite `/integration` (Integrationskurs-Team, Verwaltungsleitung, Leitung bearbeiten; Teilnehmerservice,
Fachbereichsleitung und Prüfung lesen).

- **Kursdaten**: BAMF-Kursnummer, Kursart, Abschnitte (Module zu 100 UE plus Orientierungskurs, je Kursart vorbelegt),
  Kostenerstattungssatz und Kostenbeitrag je UE (**Platzhalter 4,71 € / 2,29 €** – mit der aktuellen Festsetzung des BAMF
  abgleichen). Termine werden nach ihrem Beginn dem Abschnitt zugeordnet (ein Termin wird nicht geteilt).
- **Teilnahmeberechtigung** (Berechtigungsschein, Verpflichtung, Zulassung) mit Teilnehmernummer, Stelle, Gültigkeit,
  UE-Umfang, Kostenbefreiung und Fahrtkostenanspruch – nur Daten, keine Kopie des Bescheids.
- **Identitätsprüfung (FA-134)** am Original: gespeichert werden nur Datum, prüfende Person, Dokumentart und Ergebnis.
  Ohne Berechtigung und Prüfung ist die **Kursbeginnmeldung gesperrt** (Tooltip nennt die Personen).
- **Lichtbild (FA-135)** nur nach bestätigter Information nach Art. 13 DSGVO; wird beim Check-in als data:-URI angezeigt
  (kein abrufbarer Link, keine Datei auf dem Gerät), nie technisch abgeglichen und nach Kursende plus
  `Integration:LichtbildFristTage` (Standard 14) vom täglichen Job gelöscht.
- **Check-in Weg B (FA-136/137)**: Im Integrationskurs zeigt der Ausweis-Scan zuerst Name und Lichtbild; „Stimmt überein“
  checkt ein, „Zweifel“ checkt **vorläufig** ein und erzeugt die Aufgabe „Ausweiskontrolle“. Vorläufige Anwesenheiten
  zählen nicht für die BAMF-Abrechnung; nach der Kontrolle werden sie anerkannt oder verworfen.
- **UE-genau** je Teilnehmenden und Abschnitt: anwesend (abrechenbar), vorläufig, entschuldigt, Quote.
- **Kostenbeitrag**: Rechnung je nicht befreiter Person und Abschnitt (über die Finanzen, festgeschrieben).
- **BAMF-Abrechnung je beendetem Abschnitt**: anwesende UE × (Erstattung − ggf. Kostenbeitrag) plus bewilligte Fahrtkosten,
  als Beleg an das BAMF (mit XRechnung) und Abrechnungsliste mit Teilnehmernummern (ohne Namen) über den
  **BAMF-Mock-Adapter** (`IBamfSchnittstelle`; Meldungen im Postausgang, simulierte Vorgangsnummer).
- **Fahrtkosten (FA-42)** für kostenbefreite Teilnehmende ab 3 km: Entfernung aus dem **Geokodierer-Mock**
  (Postleitzahl → Koordinaten der Region, Luftlinie × 1,3 zum Kursort); Bewilligung erzeugt eine Gutschrift, Auszahlung
  über den Überweisungslauf. Dieselbe Entfernung (hin und zurück) nutzen jetzt die km-Fahrtkosten der Honorarmodelle.
- **Prüfungen (FA-44)**: DTZ und „Leben in Deutschland“ mit Anmeldung über den Mock und Ergebnisprüfung (LiD ab 15/33).
- **Aufgaben**: „Ausweiskontrolle“ und „Kursbeginn vorbereiten“ für das Integrationskurs-Team.
- **Datenschutz**: Anonymisierung löscht Lichtbilder und entfernt Aktenzeichen/Bemerkungen; Berechtigungen sind
  Aufbewahrungsgrund (Frist „integrationskurs“).
- Noch offen: echte InGe-Online-Anbindung, BAMF-Formulare als PDF, NFC-Auslesen (FA-138, Ausbaustufe).

## Betrieb und Daten (I8, FA-50 bis FA-52)

- **Dashboard** `/dashboard`: Auslastung, Umsatz des Jahres, Ausfallquote, offene/überfällige Aufgaben, Auslastung je
  Fachbereich, Umsatz je Monat, unterbelegte Kurse (Fachbereichsleitungen sehen ihre Fachbereiche).
- **Auswertungen** `/auswertungen`: Kursliste mit Belegung, Auslastung je Fachbereich, Umsatz je Monat/Fachbereich,
  unterbelegte Kurse; Filter Semester/Fachbereich/Jahr; Export **XLSX (ClosedXML)** und **CSV** (UTF-8 mit BOM, Semikolon);
  jeder Export wird protokolliert.
- **Statistik-Mock**: VHS-Statistik (DIE) je Programmbereich mit Geschlecht und Altersgruppen und WbG NRW je Sachbereich –
  nur aggregierte Zahlen; Übermittlung (Leitung/Verwaltungsleitung) als Mock über den Postausgang. Die Zuordnung
  Fachbereich → Programm-/Sachbereich ist mit der Statistikstelle abzustimmen.
- **Sicherung** `/betrieb`: tägliche SQLite-Online-Sicherung mit SHA-256 und automatischem Wiederherstellungstest,
  Aufbewahrung der letzten 14; Litestream-Vorlage für Linux. Details: `docs/betrieb.md`.
- **Sicherheit**: Sicherheitsheader, Rate-Limits für öffentliche Seiten, no-store für sensible Seiten; OWASP-Checkliste
  in `docs/sicherheit.md`.
- **Lastmessung** (3.000 Personen, 500 Kurse, 6.000 Belegungen): alle gemessenen Abfragen unter 0,6 s (`docs/betrieb.md`).
- **Import** `/import`: KuferSQL-CSV (UTF-8 oder Windows-Zeichensatz, Trennzeichen erkannt), Zuordnungsvorschlag aus
  typischen Spaltennamen, Vorschau, **Probelauf ohne Schreiben** mit Prüfung (Pflichtfelder, Datum, PLZ, E-Mail) und
  Dublettensuche (Datenbank und Datei); der Import legt nur neue Personen an und merkt sich die Altnummern.
  Beispieldatei: `tools/Testdaten/kufer-export-beispiel.csv`.
- **Kurse und Belegungen** (gleiche Seite, Reiter 2 und 3): Kurse mit Semester (Name/Kürzel), Fachbereich, Kursart, Min/Max,
  Entgelt, Beginn, Uhrzeit, Terminzahl, Ort, Status → Serie wird erzeugt; Belegungen finden ihre Person über die **TN-Nr aus
  dem Personenimport** und den Kurs über die Kursnummer (Status angemeldet/Warteliste/storniert, Anmeldedatum, Zahlart). Probelauf
  mit Fehlern, Dubletten (Datei und Datenbank) und Überbuchungshinweis; beim Import gehen keine Mails hinaus. Beispiele:
  `tools/Testdaten/kufer-kurse-beispiel.csv`, `tools/Testdaten/kufer-belegungen-beispiel.csv`.

## Webshop (I9): Buchungs-API, Web-Komponente, TYPO3

- **Buchungs-API** `/api/v1/shop` (OpenAPI unter `/api/v1/shop/openapi.json`): öffentliche Kurse mit Delta (`geaendertSeit`,
  entfernte Kurse) und ETag, Detail mit Terminen, Orten, Wegbeschreibung und Merkmalen (barrierefrei), Verfügbarkeit mit
  Code und Begründung (30 s Cache), Warenkorb mit **15-Minuten-Reservierung** (zählt auch am Tresen), Buchen mit
  **Idempotency-Key**, Gast oder Kundenkonto, Einwilligungen mit Version, SEPA-Mandat oder Rechnung; Konto mit
  **Double-Opt-in**, eigene Buchungen und Storno nach AGB. Gebucht wird über dieselbe Buchungsprüfung wie in der Verwaltung.
  CORS nur für konfigurierte Origins, Proof-of-Work statt Captcha, Rate-Limits je IP.
- **Web-Komponente** `<vhs-kurse>` (`/shop/shop.js`, Demo `/shop/demo.html`, Kasse `/shop/kasse.html`, Konto `/shop/konto.html`):
  Liste, Detail, Warenkorb, Kasse; Shadow DOM, CSS-Variablen, Tastatur und Screenreader (Ziel WCAG 2.2 AA).
- **TYPO3-Extension** `integrations/typo3/vibevhs_shop` (TYPO3 13 LTS): Delta-Abgleich per Scheduler, Kursliste/Suche/Detail
  serverseitig mit schema.org/Course, Routing `/kurse/{kategorie}` und `/kurse/{nummer}-{titel}`; PHPUnit-Tests vorbereitet,
  hier nicht ausgeführt (kein PHP auf dem Entwicklungs-PC).
- Details: `docs/webshop.md`.

## Kurs-QR, Schnellanmeldung und Dozierendenportal

- **Kurs-QR** je Kurs (PNG/SVG, Sammel-PDF fürs Programmheft) → `/k/{nummer}`: ohne gemerktes Gerät Kursseite mit
  „In den Warenkorb“, mit gemerktem Gerät oder persönlichem Link **Schnellanmeldung** mit einem Klick; Storno-Link per Mail
  (24 h kostenlos, danach AGB).
- **Dozierendenportal** `/portal/dozierende` (mobil, Anmeldelink per Mail): Termine mit Änderungen, Kalender-Abo,
  Anwesenheit, Nachricht an Teilnehmende, Ausfall melden, Unterlagen verteilen, Honorar mit PDF und Einwand, eigene Daten
  (Bankverbindung mit Vier-Augen-Freigabe, Nachweise), Verfügbarkeit und Kursvorschläge. Anliegen landen als Aufgaben bei
  Fachbereichsleitung bzw. Buchhaltung (`/portal-anliegen`).
- **Teilnehmendenportal „Meine vhs“** `/portal/teilnehmende`: Buchungen mit Storno/Umbuchung/Nachrückangebot, Buchen per
  Vollmacht, Termine und Kalender-Abo, Check-in-Link und Ausweis, Unterlagen, Dokumente (Originale) und Teilnehmerkonto,
  eigene Daten (E-Mail mit Bestätigung, Einwilligungen, SEPA-Mandat), Auskunft/Löschung als Vorgang, Hilfe in einfacher Sprache.
- Details: `docs/dozierendenportal.md`.

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
