# VibeVHS – Architektur und Fachkonzepte

Nov 14, 2026 · @Klaus

Dieses Dokument ist Schritt 2 von 4. Es setzt die [Anforderungsanalyse](https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG) technisch um: Endausbau mit Portalen im Blick, implementiert wird zuerst die Verwaltung.

## Architektur

VibeVHS ist ein modularer Monolith: eine Anwendung, ein Docker-Image, eine SQLite-Datei. Das passt zu einer einzelnen VHS und zum kommunalen Betrieb, und die Fachmodule bleiben trotzdem sauber getrennt.

&#91;embedded content: Architektur · 4 Kanäle, 5 Schichten, Adapter zu 24 Schnittstellen\]

Die Verwaltung arbeitet mit einer Blazor-Oberfläche. Die REST-API entsteht von Anfang an mit, weil die Portale und die Website später genau diese API nutzen. So gelten Buchungsregeln und Stichtagslogik für alle Kanäle gleich.

- **Abhängigkeiten zeigen nach innen:** Die Domäne kennt weder Datenbank noch Oberfläche; dadurch ist die Fachlogik ohne Infrastruktur testbar.
- **Fachmodule** haben eigene Ordner, eigene Tabellen-Präfixe und sprechen nur über definierte Schnittstellen miteinander.
- **Hintergrunddienste** (Outbox, zeitgesteuerte Abläufe) laufen im selben Prozess, das hält den Betrieb einfach.
- **Betrieb:** Der Container läuft hinter dem Reverse-Proxy der Kommune, die SQLite-Datei und das Dokumentarchiv liegen auf einem Volume und werden laufend gesichert.
- **Wachstum:** Reicht SQLite nicht mehr, wird nur der Datenbankanbieter in EF Core gewechselt (PostgreSQL); die Anwendung bleibt gleich.

### Blazor-Betriebsart je Kanal

Die Verwaltung nutzt Blazor Server: Die Oberfläche läuft auf dem Server, der Browser hält eine WebSocket-Verbindung (SignalR) und erhält nur die geänderten Bildschirmteile. Die Portale nutzen später statisch gerenderte Seiten oder WebAssembly über die REST-API.

| Kanal | Betriebsart | Datenweg | Begründung |
| --- | --- | --- | --- |
| Verwaltung (Stufe 1) | Blazor Server (Interactive Server) | WebSocket/SignalR, Oberfläche ruft die Anwendungsschicht direkt auf | Intranet, ca. 30 Nutzer, kein großer Download, Fachlogik und Daten bleiben auf dem Server, schnellere Entwicklung |
| Teilnehmendenportal, Website (Endausbau) | Statisches Server-Rendering, Interaktivität nur wo nötig | HTTP, REST-API | Viele, teils anonyme Nutzer im Mobilfunknetz; keine stehende Verbindung je Besucher |
| Dozierendenportal (Endausbau) | Blazor WebAssembly oder Auto | HTTP, REST-API | Mobil, verkraftet schwankende Verbindungen besser |

Nachteil von Blazor Server: Jede offene Sitzung belegt eine Verbindung und etwas Serverspeicher, bei Netzabbruch wird neu verbunden, offline geht nichts. Für die Verwaltung ist das unkritisch.

## Datenmodell

Das Modell umfasst den Endausbau mit Portalen. 16 Entitäten sind zeitlich versioniert (neu: Kontaktdaten und Merkmalswerte); alle Vorgänge wie Belegung, Anwesenheit und Zahlung sind unveränderliche Ereignisse. Die Feldlisten entstehen mit dem Code in Schritt 4.

&#91;embedded content: Datenmodell · 6 Bereiche, Entitäten im Überblick\]

Die Bereiche hängen über wenige Kernbeziehungen zusammen:

| Beziehung | Kardinalität | Bemerkung |
| --- | --- | --- |
| Person → Belegung ← Kurs | 1 : n : 1 | Belegung speichert zusätzlich, wer gebucht hat und mit welcher Vollmacht |
| Kurs → Veranstaltung → Ortszuordnung → Ort | 1 : n : n : 1 | Mehrere Orte je Veranstaltung (hybrid), je Ort eigene Kapazität; Konflikte je Ort geprüft |
| Raum → Gebäude → Liegenschaft | n : 1, n : 0..1 | Lagebeschreibungen ergeben die Wegbeschreibung; Merkmale werden vererbt |
| Ort → Merkmalswert ← Merkmal | 1 : n : 1 | Katalog pflegbar; Wert mit Nachweis und Gültigkeit |
| Belegung → Reservierung ← Veranstaltung | 1 : n : 1 | Nur bei Kontingent mit Vorab-Einbuchung; Check-in bucht eine Einheit ab |
| Termin → Anwesenheit ← Belegung | 1 : n : 1 | Minuten- bzw. UE-genau; Grundlage für BAMF und Honorar |
| Kurs → Dozenteneinsatz ← Person | 1 : n : 1 | Eine Person kann Dozentin und Teilnehmerin zugleich sein |
| Kurs → Buchungsrichtlinie | n : 1 | Aus der Vorlage der Kursart kopiert, je Kurs anpassbar |
| Person/Organisation → Buchungsvollmacht → Person | 1 : n : 1 | Mit Gültigkeitszeitraum und Nachweis |
| Belegung → Posten ← Rechnung | 1 : n : 1 | Entgelt zum Buchungszeitpunkt wird in den Posten übernommen |
| Dokument → Vorgang | n : 1 | Stichtag, PDF, JSON-Datenstand, Prüfwert |

**Konventionen:**

- Eine Person ist eine Person, egal in welcher Rolle (Teilnehmende, Dozierende, Ansprechpartner). Rollen hängen an der Person, nicht an getrennten Tabellen.
- Schlüssel als sortierbare GUID (Version 7), damit sie sicher über API und QR-Code nach außen gehen können.
- Geldbeträge in Cent als Ganzzahl, Zeitpunkte in UTC, reine Daten als Datum ohne Uhrzeit.
- Kein „weiches Löschen“: Gelöscht wird nach Frist, sonst anonymisiert.
- Jede Tabelle trägt eine Versionsnummer gegen gleichzeitige Änderungen.

### Kontaktdaten

Eine Person hat beliebig viele Postadressen, Rufnummern (Telefon, Mobil, Messenger) und E-Mail-Adressen. Jede Angabe ist zeitlich versioniert wie der Name. Zwei Kennzeichen hängen an den Postadressen und sind selbst zeitlich gültig: **üblicher Aufenthaltsort** (für Statistik, Förderung und Fahrtkosten) und **Standard-Korrespondenzadresse** (für Briefe). Im Normalfall zeigen beide auf dieselbe Adresse; die Oberfläche bietet „Korrespondenz abweichend“ nur als Ausnahme an. Bei Rufnummern und E-Mail gibt es je ein bevorzugtes Element und eine Einwilligung je Kanal (z. B. SMS-Erinnerung ja, Werbung nein).

### Kurs, Veranstaltungen und Serien

Der **Kurs** ist die logische Klammer: Titel, Kursart, Entgelt, Buchungsrichtlinie, Dozierendenteam. Die einzelnen Treffen heißen **Veranstaltungen**. Der Kurs hat einen von zwei Umfängen:

- **Begrenzt:** eine feste Menge Veranstaltungen (z. B. 12 Abende).
- **Unbefristet regelmäßig:** z. B. „jeden Dienstag, 18 Uhr“ ohne Ende. Die Veranstaltungen werden rollierend für einen Planungshorizont (z. B. 6 Monate) erzeugt und von einem Hintergrunddienst nachgeneriert.

Eine **Serienregel** (Wochentag, Uhrzeit, Rhythmus, Beginn, Anzahl oder offen, Ferien und Feiertage aus dem Bundesland-Kalender) erzeugt Veranstaltungen. Der Schnellweg „regelmäßig, gleicher Ort, gleiche Dozentin“ ist nur eine Serienregel mit Vorgabe-Ort und Vorgabe-Dozierender. Daneben lassen sich **Einzelveranstaltungen** anlegen: anderes Datum, andere Orte, andere Dozierende, in der Regel aus dem Dozierendenteam des Kurses (andere nur mit Hinweis). Jede erzeugte Veranstaltung merkt sich ihre Herkunftsregel; wird sie einzeln geändert, gilt sie als Ausnahme und wird bei „Serie ab hier ändern“ nicht überschrieben.

### Orte und Ortszuordnung

Ein **Ort** hat eine von drei Arten:

| Ortsart | Beispiel | Wofür |
| --- | --- | --- |
| Raum (gehört zu einem Gebäude, das optional zu einer Liegenschaft gehört) | Raum 2.14, Gebäude B, Campus Bahnhofstraße | Wegbeschreibung aus den Lagebeschreibungen von Liegenschaft, Gebäude und Raum; Adresse und Koordinaten für Fahrtkosten |
| Allgemeiner Ort | Treffpunkt Parkplatz Stadtwald | Eigene Adresse oder Koordinaten plus Lagebeschreibung |
| Virtueller Raum | Videokonferenz-Raum „Sprachen 3“ | Zugangslink, wird erst kurz vor Beginn an Belegte verschickt; keine Fahrtkosten |

Die **Wegbeschreibung** setzt sich automatisch von außen nach innen zusammen: Anfahrt zur Liegenschaft, Weg zum Gebäude, Weg zum Raum. Jeder Baustein wird einmal gepflegt und in allen Briefen und Mails wiederverwendet. Für **Fahrtkosten** zählt die Strecke von der Adresse der Dozierenden (üblicher Aufenthaltsort) zum Gebäude oder Ort; die Entfernung liefert ein Routing-Adapter (zunächst Mock).

Eine Veranstaltung ist über die **Ortszuordnung** mit einem oder mehreren Orten verbunden; mehrere Orte gleichzeitig ergeben eine hybride Veranstaltung. Jeder Ort hat **Kapazitäten je Nutzungsart** (z. B. Seminar 20, Vortrag 40, Bewegung 12, Prüfung 10). Die Ortszuordnung übernimmt die Kapazität zur Nutzungsart der Veranstaltung oder setzt eine eigene, kleinere Grenze. Die Gesamtkapazität einer hybriden Veranstaltung ist die Summe ihrer Orte; online kann auch „unbegrenzt“ sein.

### Raummerkmale

Orte, Gebäude und Liegenschaften tragen **Merkmale** aus einem pflegbaren Katalog, damit neue Merkmale ohne Programmänderung dazukommen. Ein Merkmal hat einen Typ (ja/nein, Zahl, Text, Auswahl), optional einen Nachweis (Dokument, Aktenzeichen) und einen Gültigkeitszeitraum. Ein Gebäudemerkmal gilt für alle Räume darin, sofern der Raum es nicht überschreibt.

| Gruppe | Beispiele |
| --- | --- |
| Zugänglichkeit | barrierearmer Zugang, barrierefreier Zugang (DIN 18040), Aufzug, rollstuhlgerechtes WC, induktive Höranlage, Rollstuhlplätze (Zahl) |
| Zulassungen | Kursort für Integrationskurse beim BAMF gemeldet (mit Nachweis und Ablaufdatum), Prüfungsort telc/Goethe, zugelassen für Kinderbetreuung |
| Ausstattung | Beamer, Smartboard, Lehrküche, Schwingboden, PC-Arbeitsplätze (Zahl), WLAN |
| Organisation | Öffnungszeiten, Schlüssel bei Hausmeisterei, Parkplätze, ÖPNV-Haltestelle, Hausordnung |

Kursarten können **Anforderungen an Merkmale** stellen (Integrationskurs braucht „BAMF-Kursort gültig am Veranstaltungstag“). Die Raumplanung filtert danach und erklärt im Tooltip, warum ein Raum nicht wählbar ist, etwa „BAMF-Meldung abgelaufen am 30.09.2026“. Läuft eine Zulassung bald ab, entsteht eine Aufgabe. Zugänglichkeitsmerkmale erscheinen in Wegbeschreibung, Anmeldebestätigung und später im Portal.

### Dozierendenmerkmale

Dasselbe Muster gilt für Dozierende. Ein gemeinsamer **Merkmalskatalog** unterscheidet, ob ein Merkmal an Orten oder an Personen hängt. Dozierende tragen:

- **Beschäftigungsart** (festangestellt oder freiberuflich), zeitlich gültig. Sie steuert, ob ein Einsatz über Arbeitszeit oder über Honorarvertrag läuft.
- **Qualifikationen** mit Nachweis, ausstellender Stelle und Gültigkeit, z. B. BAMF-Lehrkraftzulassung für Integrationskurse, Alpha-Zusatzqualifikation, Prüferlizenz telc oder Goethe, Übungsleiterlizenz, erweitertes Führungszeugnis.

Kursarten verlangen Merkmale an Ort **und** Dozierenden. Ein Prüfdienst liefert je Veranstaltung und Tag einen Begründungscode. Daraus entstehen die gesperrten Auswahlen mit Tooltip, die Aufgaben vor Ablauf und die Markierung betroffener Veranstaltungen. Ausnahmen erteilt die Fachbereichsleitung mit Begründung und Protokoll; gesetzlich geforderte Merkmale wie die BAMF-Zulassung lassen keine Ausnahme zu.

### Belegungsarten und Vorab-Einbuchung

Eine **Belegung** gilt entweder für **alle Veranstaltungen** des Kurses oder als **Kontingent** über eine Anzahl (z. B. 10 Veranstaltungen in einem unbefristeten Kurs, optional mit Verfallsdatum). Der Kurs legt fest, ob für Kontingente eine **Vorab-Einbuchung** nötig ist. Dann gilt:

1. Die Person reserviert eine Veranstaltung. Das geht nur, solange die Kapazität des gewählten Ortes nicht erreicht ist; sonst Warteliste für genau diese Veranstaltung.
2. Die Reservierung verbraucht einen Platz, aber noch keine Einheit des Kontingents.
3. Beim Check-in (QR-Code oder Liste) wird die Reservierung bestätigt und eine Einheit abgebucht. Ohne Reservierung lehnt der Check-in ab und nennt den Grund.
4. Absagen bis zu einer Frist geben den Platz frei; ein Nichterscheinen verbraucht die Einheit, wenn der Kurs das so regelt.

Reservierung, Check-in, Absage und Abbuchung sind unveränderliche Ereignisse; der Kontingentstand ist ihre Summe.

## Zeitliche Gültigkeit

Stammdaten werden bitemporal versioniert, Bewegungsdaten sind unveränderliche Ereignisse, und jedes erzeugte Dokument wird mit seinen Daten eingefroren. Diese drei Bausteine verhindern die „seltsamen Ergebnisse“ einer naiven Umsetzung, bei der eine Änderung rückwirkend alle alten Vorgänge verfälscht.

### Baustein 1: Bitemporale Stammdaten

Jede versionierte Entität besteht aus einer stabilen Identität (z. B. `Person`) und ihren Versionen (`PersonVersion`). Eine Version trägt zwei Zeitachsen:

- **Gültigkeitszeit** (`GueltigVon`, `GueltigBis`): Wann ist das in der Wirklichkeit so? Halboffenes Intervall, offenes Ende = 9999-12-31. Je Entität dürfen sich Gültigkeiten nicht überlappen.
- **Erfassungszeit** (`ErfasstAm`, `ErsetztAm`): Wann wusste das System das? Versionen werden nie geändert oder gelöscht, nur durch neue ersetzt.

Beispiel: Die Dozentin wurde 2019 mit Tippfehler erfasst, am nächsten Werktag korrigiert, heiratete am 14.06.2025 und meldete das am 02.07.2025.

| Nr. | Nachname | Gültig von | Gültig bis | Erfasst am | Ersetzt am | Vorgang |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Schmitt | 2019-03-01 | offen | 2019-03-01 | 2019-03-04 | Ersterfassung mit Tippfehler |
| 2 | Schmidt | 2019-03-01 | offen | 2019-03-04 | 2025-07-02 | Korrektur (gilt rückwirkend) |
| 3 | Schmidt | 2019-03-01 | 2025-06-14 | 2025-07-02 | – | Ende der Gültigkeit wegen Heirat |
| 4 | Becker | 2025-06-14 | offen | 2025-07-02 | – | Änderung ab Heiratsdatum |

Damit beantwortet das System beide Fragen richtig:

- „Wie hieß die Dozentin im Kurs vom Herbst 2024?“ → Schmidt (aktueller Wissensstand, Gültigkeit Herbst 2024).
- „Was stand am 02.03.2019 im System?“ → Schmitt (für Prüfung und Revision).

**Zweites Beispiel: Umzug mit nachträglicher Korrektur.** Frau Muster wohnt seit 2020 in der Lindenstraße. Am 15.03.2026 meldet sie: „Ich ziehe zum 01.08. in den Bergweg.“ Am 10.07. geht ihre Kursbestätigung raus. Am 20.09. kommt der Brief zurück, und sie sagt: „Ich bin schon zum 01.07. umgezogen.“ Die Spalte „Gültig bis“ ist ausschließlich (halboffenes Intervall): 2026-08-01 heißt „gilt bis einschließlich 31.07.“.

| Nr. | Adresse | Gültig von | Gültig bis | Erfasst am | Ersetzt am | Vorgang |
| --- | --- | --- | --- | --- | --- | --- |
| A | Lindenstr. 5 | 2020-01-01 | offen | 2020-01-10 | 2026-03-15 | Ersterfassung |
| B | Lindenstr. 5 | 2020-01-01 | 2026-08-01 | 2026-03-15 | 2026-09-20 | Ende der Gültigkeit wegen gemeldetem Umzug |
| C | Bergweg 2 | 2026-08-01 | offen | 2026-03-15 | 2026-09-20 | Umzug ab 01.08. |
| D | Lindenstr. 5 | 2020-01-01 | 2026-07-01 | 2026-09-20 | – | Korrektur: Umzug war früher |
| E | Bergweg 2 | 2026-07-01 | offen | 2026-09-20 | – | Korrektur: Umzug war früher |

- „Wo wohnte sie am 10.07.?“ → Bergweg. Es zählen nur die nicht ersetzten Zeilen D und E (heutiger Wissensstand, Gültigkeitszeit).
- „Wohin hätten wir am 10.07. geschrieben?“ → Lindenstraße. Es zählt der Wissensstand vom 10.07., also die damals erfassten und noch nicht ersetzten Zeilen B und C (Erfassungszeit). Damit ist belegt, dass der Brief nach damaligem Wissen richtig adressiert war.

Fachbegriff: **bitemporale Datenhaltung**. Die beiden Achsen heißen in der Literatur *valid time* (Gültigkeitszeit) und *transaction time* oder *system time* (Erfassungszeit); SQL:2011 kennt sie als *application-time period* und *system-versioned table*.

**Versioniert werden:** Personen (Name, Anschrift, Kontakt, Bankverbindung, Sprache), Organisationen und Zugehörigkeiten, Dozierenden-Qualifikationen und Honorarsätze, Entgelte und Ermäßigungen, Räume, Kurskerndaten ab Veröffentlichung, Buchungsvollmachten und Rollen sowie die Daten der VHS selbst (Leitung, Briefkopf, Bankverbindung, Logo). Der letzte Punkt wird oft vergessen: Eine alte Bescheinigung trägt die damalige Leitung.

### Baustein 2: Bewegungsdaten als Ereignisse

Belegungen, Anwesenheiten, Rechnungen, Zahlungen und Honorarabrechnungen werden nicht versioniert. Sie sind Ereignisse mit eigenem Zeitpunkt und werden nach dem Buchhaltungsprinzip nur durch Storno oder Gegenbuchung korrigiert, nie überschrieben.

### Baustein 3: Stichtagsregeln und Dokumentarchiv

Jede Dokumentart legt fest, zu welchem Stichtag ihre Inhalte aufgelöst werden. Die Versandanschrift ist bewusst immer die aktuelle, denn der Brief soll ankommen.

| Dokumentart | Inhalt zum Stichtag | Versandanschrift |
| --- | --- | --- |
| Teilnahmebescheinigung | Kursende (Name, Kurstitel, Dozierende, Unterrichtsstunden) | aktuell |
| Rechnung, Gutschrift | Rechnungsdatum | aktuell |
| Honorarvertrag | Vertragsdatum | aktuell |
| Mahnung | Mahndatum; Forderung aus der Originalrechnung | aktuell |
| BAMF-Abrechnung | Ende des Abrechnungszeitraums | entfällt |

Beim Erzeugen speichert das System das PDF, die aufgelösten Daten als JSON und einen Prüfwert. Ein Nachdruck liefert das archivierte Original. Eine Neuausstellung mit anderen Daten ist nur bewusst möglich, mit Begründung, und trägt den Vermerk „Neuausstellung vom …“.

### Bedienung und Umsetzung

- Bei einer Änderung fragt die Maske „Korrektur (war schon immer so)“ oder „Änderung ab Datum“; Standard ist Änderung ab heute.
- Historisierte Felder zeigen ein kleines Uhr-Symbol; ein Klick öffnet die Zeitleiste der Versionen.
- Technisch: eine generische Basis für Versionen, ein Abfrage-Helfer `AlsStichtag(gueltigAm, bekanntAm)` für EF Core, Indizes auf (Id, GueltigVon). SQLite kennt keine temporalen Tabellen, das Muster wird daher in der Anwendung umgesetzt.

## Buchungsrechte

Ein einziger Prüfdienst entscheidet über jede Buchung, egal ob sie am Tresen, später im Portal oder per QR-Code entsteht. Er liefert neben Ja oder Nein immer einen Grund. Daraus entstehen die erklärenden Tooltips.

### Datenmodell

| Entität | Wichtige Felder | Zeitlich versioniert |
| --- | --- | --- |
| Kurs | Sichtbarkeit: öffentlich, intern, geschlossen; Verweis auf Buchungsrichtlinie | ja (ab Veröffentlichung) |
| Buchungsrichtlinie | Name, Vorlage für Kursart, Liste von Regeln | ja |
| Buchungsregel | Rolle, darf sich selbst buchen, darf andere buchen, nur innerhalb Vollmacht ja/nein | als Teil der Richtlinie |
| Kurszielgruppe | Kurs, zugelassene Organisation oder Personengruppe (für geschlossene Kurse) | nein |
| Buchungsvollmacht | Bevollmächtigte Person oder Organisation, betroffene Person oder Gruppe, Art (Erziehungsberechtigung, Firma, Betreuung, Kostenträger), Nachweis, Bestätigt durch, Gültigkeit | ja |
| Zugehörigkeit | Person, Organisation, Funktion (z. B. Mitarbeitende, Bevollmächtigte) | ja |
| Belegung | Kurs, Person, gebucht von, Rolle beim Buchen, Vollmacht, Kanal (Tresen, Portal, QR) | nein (Ereignis) |

### Vorlagen je Kursart

| Kursart | Sichtbarkeit | Verwaltung | Registrierte Teilnehmende | Firmenbevollmächtigte | Dozierende |
| --- | --- | --- | --- | --- | --- |
| Offener Kurs | öffentlich | selbst und andere | selbst; andere mit Vollmacht | eigene Mitarbeitende | keine Buchung |
| Firmenkurs | geschlossen | andere | keine Buchung | eigene Mitarbeitende | keine Buchung |
| Integrationskurs | intern | andere | keine Buchung | keine Buchung | keine Buchung |
| Interne Fortbildung | intern | selbst und andere | keine Buchung | keine Buchung | selbst |

### Prüfablauf

1. **Kurs buchbar?** Status „veröffentlicht“, Anmeldezeitraum offen; sonst Warteliste anbieten.
2. **Harte Regel:** Selbstregistrierte Teilnehmende buchen nur öffentliche Kurse.
3. **Sichtbarkeit:** Bei geschlossenen Kursen muss die gebuchte Person am Kursbeginn zur Zielgruppe gehören.
4. **Richtlinie:** Die Rolle der buchenden Person muss „selbst“ bzw. „andere“ erlauben.
5. **Vollmacht:** Bei Buchung für andere außerhalb der Verwaltung muss am Buchungstag eine bestätigte Vollmacht gelten.
6. **Datenrechte:** Verwaltungsmitarbeitende brauchen das Recht für Fachbereich und Semester des Kurses.
7. **Fachliche Prüfungen:** Mindestalter, Voraussetzungen, BAMF-Berechtigung, Doppelbuchung, Zeitüberschneidung.
8. **Ergebnis:** erlaubt oder abgelehnt, mit Grundcode und Hinweis (z. B. „Dieser Kurs ist nur über die Verwaltung buchbar. Telefon 0211 …“). Die Belegung speichert, wer für wen mit welcher Vollmacht gebucht hat.

In Stufe 1 bucht nur die Verwaltung. Der Prüfdienst ist trotzdem vollständig, damit die Portale später dieselben Regeln nutzen.

## Aufgaben und große Listen

Aufgaben werden von Regeln erzeugt, an Gruppen geroutet und schließen sich selbst, wenn ihr Anlass entfällt. Listen laden Daten abschnittsweise nach und zeichnen nur sichtbare Zeilen, damit auch 40.000 Belegungen flüssig bleiben.

### Aufgabenmodell

| Baustein | Inhalt |
| --- | --- |
| Aufgabenregel | Typ, Auslöser (Ereignis oder Zeitplan), Prüfbedingung, zuständige Rolle, Fachbereichsbezug, Fälligkeitsformel, Eskalationsfrist, Ziel-Maske |
| Aufgabe | Typ, Bezug (Kurs, Person, Rechnung), Gruppe, bearbeitende Person, Status, Fälligkeit, Dringlichkeit, Schlüssel gegen Doppelanlage |
| Abwesenheit | Person, Zeitraum, Vertretung |

Lebenszyklus einer Aufgabe:

1. **Offen im Gruppenkorb:** Die Regel erzeugt die Aufgabe einmal je Anlass (Schlüssel z. B. „Mindestzahl:26H-302“), sichtbar für alle mit Rolle und Fachbereichsrecht.
2. **In Bearbeitung:** Eine Person übernimmt sie oder bekommt sie zugewiesen.
3. **Erledigt:** durch die Aktion in der Ziel-Maske, oder **automatisch geschlossen**, weil die Prüfbedingung bei der nächsten Auswertung nicht mehr zutrifft.
4. **Eskaliert:** Nach Ablauf der Eskalationsfrist erscheint sie zusätzlich in der Leitungsübersicht; sie bleibt bei der bisherigen Gruppe.

Die Regeln laufen nach jedem relevanten Ereignis (z. B. neue Belegung) und zusätzlich zeitgesteuert. Jeder Statuswechsel landet im Protokoll.

### Listen mit vielen Datensätzen

| Ebene | Umsetzung |
| --- | --- |
| Oberfläche | Blazor-Komponente `Virtualize` mit `ItemsProvider` bzw. Radzen DataGrid mit Virtualisierung: Es werden nur die sichtbaren Zeilen plus Puffer gezeichnet; fehlende Abschnitte lädt die Komponente beim Scrollen nach und zeigt solange Platzhalterzeilen. |
| Abfrage | Abschnitte à 50 Zeilen über Keyset-Pagination („nach Name X, Id Y“) statt OFFSET, damit auch tiefe Positionen schnell bleiben; die Gesamtzahl kommt aus einer separaten COUNT-Abfrage. |
| Suche | SQLite FTS5-Volltextindex über Namen, E-Mail, Kursnummer und Titel; zusätzlich eine normalisierte Suchspalte (Yılmaz → yilmaz, ä → ae), damit Schreibvarianten gefunden werden. |
| Filter | Facettenzählungen je Filterwert über gruppierte Abfragen auf indizierten Spalten; bei Bedarf zwischengespeichert. |
| Sammelaktionen | Arbeiten auf der Filterdefinition, nicht auf geladenen Zeilen; große Mengen laufen als Hintergrundauftrag mit Protokoll. |

Blättern mit Seitenzahlen bleibt nur für die REST-API und Druckausgaben. Eine harte Obergrenze („mehr als 100 Treffer, bitte filtern“) gibt es nur in der Schnellsuche der Kopfzeile.

## Sicherheits- und Datenschutzkonzept

Verwaltungsmitarbeitende melden sich über die kommunale Anmeldung an, alle Zugriffe laufen über Rollen plus Datenrechte, und jede Änderung landet in einem manipulationsgeschützten Protokoll. Die Löschfristen sind ein Vorschlag und mit der oder dem Datenschutzbeauftragten abzustimmen.

### Sicherheitsmaßnahmen

| Thema | Maßnahme |
| --- | --- |
| Anmeldung Verwaltung | OpenID Connect gegen die kommunale Anmeldung; Rollen aus AD-Gruppen; Zwei-Faktor über den Identitätsdienst für Admin und Buchhaltung. Stufe 1: Mock-Login mit Testbenutzern je Rolle. |
| Anmeldung Portale (Endausbau) | Eigene Konten mit E-Mail-Bestätigung, Passkey oder Passwort mit 2FA; getrennt von den Verwaltungskonten |
| Berechtigungen | Rollen (z. B. Teilnehmerservice, Fachbereichsleitung, Buchhaltung, Admin) + Datenrechte je Fachbereich und Semester + Feldrechte für sensible Felder (Bank, Aufenthaltsstatus) |
| Protokoll | Audit-Log jeder Änderung mit Benutzer, Zeit, alt/neu; Einträge per Hash verkettet, damit Lücken oder Manipulation auffallen; Lesezugriffe auf sensible Daten werden ebenfalls protokolliert |
| Verschlüsselung | TLS am Reverse-Proxy der Kommune; IBAN und Aufenthaltsdaten feldverschlüsselt (ASP.NET Data Protection); Volume und Sicherungen verschlüsselt |
| Webschutz | Anti-CSRF, Content-Security-Policy, sichere Cookies, Begrenzung der Anfragen an öffentliche Endpunkte (QR, Übersetzung, Anmeldung) |
| QR- und Link-Tokens | Zufällige 128-Bit-Tokens; Check-in-Codes nur im Terminzeitraum gültig; Übersetzungslinks laufen nach 90 Tagen ab und zeigen keine Daten ohne Token |
| Betrieb | Container ohne Root-Rechte, Geheimnisse per Docker Secrets, regelmäßige Abhängigkeits-Scans |

### Datenschutz (DSGVO)

| Datenart | Rechtsgrundlage | Löschung bzw. Anonymisierung (Vorschlag) |
| --- | --- | --- |
| Teilnehmende ohne offene Vorgänge | Vertrag (Art. 6 Abs. 1 b) | Anonymisierung 3 Jahre nach letzter Belegung |
| Rechnungen, Zahlungen, Honorare | Rechtliche Pflicht (Art. 6 Abs. 1 c, § 147 AO) | 10 Jahre, danach Löschung |
| Integrationskurs-Daten | Rechtliche Pflicht (Integrationskursverordnung) | nach BAMF-Aufbewahrungsvorgaben |
| Einwilligungen (Newsletter, Fotos, vhs.cloud) | Einwilligung (Art. 6 Abs. 1 a) | bis Widerruf; Nachweis 3 Jahre danach |
| Anwesenheit außerhalb geförderter Kurse | Vertrag | 1 Jahr nach Kursende |
| Protokolle | Berechtigtes Interesse / IT-Sicherheit | 2 Jahre |

- **Anonymisierung statt Löschung**, wo Statistik betroffen ist: Die Belegung bleibt mit Alter, Geschlecht und Wohnort-Kreis erhalten, der Personenbezug verschwindet.
- **Auskunft nach Art. 15** als ein Klick: alle Daten einer Person als PDF und JSON, inklusive Historie.
- **Einwilligungstexte versioniert**: gespeichert wird, welcher Text wann zugestimmt wurde.
- **Auftragsverarbeitung**: Übersetzung, SMS und Zahlungsanbieter nur mit AV-Vertrag und Serverstandort EU.
- **Datenschutz-Folgenabschätzung** prüfen, weil Aufenthaltsdaten von Geflüchteten verarbeitet werden.

### Archivieren, Sperren, Anonymisieren, Auskunft

Die referentielle Integrität bleibt erhalten, weil **nie eine Zeile gelöscht wird, auf die andere zeigen**. Stattdessen durchläuft eine Person (und analog ein Kurs) vier Zustände:

| Zustand | Was passiert | Wer sieht es |
| --- | --- | --- |
| aktiv | normaler Betrieb | alle mit Recht |
| archiviert | schreibgeschützt, aus Standardlisten ausgeblendet, über Filter „archiviert“ suchbar | alle mit Recht |
| gesperrt (Art. 18 DSGVO) | Löschverlangen liegt vor, aber Aufbewahrungsfristen laufen (Buchungsbelege, Rechnungen, Integrationskurs-Nachweise); keine Verwendung außer für die Pflicht | nur Buchhaltung bzw. Integrationskurs-Team, jeder Zugriff protokolliert |
| anonymisiert | Personenbezug in allen Versionen entfernt; Zeile bleibt mit ID als „Gelöschte Person · anonymisiert am …“ | Zählungen und Statistik |

**Anonymisierung an Ort und Stelle** statt einer gemeinsamen Pseudo-Person: Jede Person behält ihren eigenen Platzhalter, damit Teilnahmezahlen, Statistik nach Weiterbildungsgesetz NRW und BAMF-Abrechnungen stimmen. Erhalten bleiben nur grobe Merkmale (Altersgruppe, Geschlecht, Kreis). Die Anonymisierung erfasst alle bitemporalen Versionen, Kontaktdaten, Einwilligungen, Vollmachten, den Suchindex, Mails im Postausgang und archivierte Dokumente außerhalb von Aufbewahrungsfristen. Das Protokoll speichert von Anfang an nur IDs, keine Klarnamen; es bleibt daher unverändert gültig.

**Löschläufe** sind regelbasiert und einstellbar (z. B. „Personen ohne Belegung seit 3 Jahren“, „Kurse mit Ende vor 10 Jahren“). Sie zeigen vorher Treffer und Zahlen, brauchen eine Freigabe nach dem Vier-Augen-Prinzip und schreiben ein Löschprotokoll (Anzahl, Regel, Freigabe, ohne Personendaten). Ein Kurs wird erst endgültig bereinigt, wenn alle seine Belege ihre Frist erreicht haben; seine Teilnehmenden werden dann anonymisiert, Kurs und Kennzahlen bleiben.

**Löschverlangen nach Art. 17** löst sofort aus, was ohne Pflicht möglich ist (Kontaktdaten, Einwilligungen, Werbung, Portalkonto), sperrt den Rest bis zum Fristende und erzeugt eine Antwort an die Person: was gelöscht ist, was bis wann warum gesperrt ist. Nach Fristende anonymisiert der Löschlauf automatisch.

**Auskunft nach Art. 15** erzeugt ein PDF und eine maschinenlesbare JSON-Datei mit allen Daten inklusive Historie, Dokumenten, Empfängern (Schnittstellen) und Fristen.

**Datensicherungen:** Anonymisierte Daten können in älteren Sicherungen noch stehen. Stufe 1 begrenzt deshalb die Aufbewahrung der Sicherungen und dokumentiert das. Als Ausbaustufe ist **Crypto-Shredding** vorgesehen: Personendaten werden mit einem Schlüssel je Person verschlüsselt; wird der Schlüssel vernichtet, sind die Daten auch in alten Sicherungen unlesbar.

## Integrationskonzept

Jede der 24 Schnittstellen aus der Anforderungsanalyse ist ein Port mit austauschbarer Implementierung. Ausgehende Nachrichten laufen über eine Outbox, sodass kein Versand verloren geht und jeder Versand am Vorgang sichtbar ist. In Stufe 1 schreiben die Mocks realistische Dateien in einen „Postausgang“, den die Verwaltung im System ansehen kann.

### Bausteine

- **Port und Adapter:** Die Anwendung kennt nur Schnittstellen wie `IFinanzUebergabe`, `IBamfMeldung`, `IUebersetzung`, `IMailVersand`, `ISepaDatei`. Welche Implementierung läuft (Mock oder echt), steht in der Konfiguration.
- **Outbox:** Die fachliche Änderung und der Versandauftrag werden in derselben SQLite-Transaktion gespeichert. Ein Hintergrunddienst versendet, wiederholt bei Fehlern mit wachsendem Abstand und setzt den Status.
- **Inbox für Eingangsdateien:** Kontoauszüge (camt) und Rückmeldungen werden eingelesen, gegen doppelte Verarbeitung gesichert und zugeordnet; nicht zuordenbare Posten landen in einer Klärungsliste.
- **Protokoll am Vorgang:** Jeder Versand und Empfang erscheint am Kurs, an der Person oder an der Rechnung, mit Inhalt, Zeitpunkt und Ergebnis.
- **Vertragstests:** Mock und echte Implementierung bestehen dieselbe Testreihe, damit der Wechsel später keine Überraschungen bringt.
- **Länderkonfiguration:** Ferien, Feiertage, Landesstatistik und Förderregeln liegen hinter `ILandesregeln`; Stufe 1 enthält nur NRW.

### Mocks in Stufe 1

| Schnittstelle | Verhalten des Mocks |
| --- | --- |
| S-01 Kommunale Anmeldung | Auswahl eines Testbenutzers je Rolle beim Login |
| S-03 Finanzsoftware | CSV-Datei je Übergabelauf im Postausgang, mit Protokoll |
| S-04 bis S-06 Bank | pain.008 und pain.001 als gültige XML-Dateien; camt.053-Beispieldatei zum Einlesen |
| S-08 E-Rechnung | XRechnung-XML, gegen das offizielle Schema geprüft |
| S-09, S-10 BAMF | Simulierte Antworten (Kursbeginn bestätigt, Teilnehmerauskunft), Abrechnungsbogen als PDF |
| S-13, S-14 Statistik | Exportdatei mit Plausibilitätsprüfung |
| S-17 Website | Lesende REST-Schnittstelle für öffentliche Kurse |
| S-19 E-Mail | Mails landen im Postausgang (und lokal in Mailpit), nicht beim Empfänger |
| S-20 Übersetzung | Erzeugt Übersetzungslink und QR-Code; die Zielseite zeigt einen Platzhaltertext je Sprache |

### Übersetzungslinks und QR-Codes

Jede Mail und jeder Brief erhält beim Erzeugen ein Token. Der Link `/t/{token}` zeigt den Text in der bevorzugten Sprache der Person, wählbar sind weitere Sprachen. Der QR-Code „Translate this letter in your language“ auf Briefen führt zur selben Seite. Übersetzt wird beim ersten Aufruf und zwischengespeichert.

## Testkonzept

Die meisten Tests prüfen die Fachlogik schnell und ohne Oberfläche; wenige End-to-End-Tests sichern die Kernabläufe der Verwaltung ab. Jeder Use Case aus Schritt 1 erhält mindestens einen Abnahmetest.

| Ebene | Was wird geprüft | Werkzeug | Wann |
| --- | --- | --- | --- |
| Unit | Zeitliche Gültigkeit, Buchungsprüfung, Entgelte und Ermäßigungen, Stornofristen, Mahnstufen, UE-Berechnung | xUnit, Shouldly | jeder Build |
| Integration | Datenzugriff mit echter SQLite-Datei, Migrationen, API-Endpunkte, Outbox | WebApplicationFactory, Testcontainers bei Bedarf | jeder Build |
| Vertrag | Mock und echter Adapter verhalten sich gleich; Dateiformate gegen XSD (SEPA, XRechnung) | xUnit, Schemaprüfung | jeder Build |
| Dokumente | PDF- und Mailinhalte bleiben stabil (Namen, Stichtage, QR-Code vorhanden) | Verify (Snapshot) | jeder Build |
| End-to-End | Kernabläufe: Kurs anlegen, Anmeldung am Tresen, QR-Check-in, Bescheinigung, Mahnlauf | Playwright für .NET | täglich |
| Barrierefreiheit | WCAG 2.1 AA auf allen Masken | axe-core über Playwright | täglich |
| Sicherheit | Bekannte Schwachstellen, Konfiguration | OWASP ZAP Baseline, Abhängigkeits-Scan | wöchentlich |
| Last | Anmeldestart mit 500 gleichzeitigen Besuchern | NBomber oder k6 | vor Release |
| Abnahme | Use Cases als Szenarien mit Verwaltungsmitarbeitenden | Prototyp, Testdrehbuch | je Ausbaustufe |

**Pflichtfälle für die zeitliche Gültigkeit:**

- Namensänderung nach Kursende: Nachdruck und Neuausstellung zeigen den alten Namen, der Brief geht an die neue Anschrift.
- Rückwirkende Korrektur gegenüber Änderung ab Datum, inklusive Abfrage „was wussten wir am …“.
- Entgeltänderung während der Anmeldephase: Bestehende Belegungen behalten ihren Preis.
- Leitungswechsel der VHS: Alte Bescheinigungen tragen die alte Unterschrift.
- Vollmacht läuft ab: Buchung am Folgetag wird mit Grund abgelehnt.

**Testdaten:** Ein Generator (Bogus) erzeugt eine realistische VHS mit 2 Semestern, rund 300 Kursen, 3.000 Personen, Integrationskursen und Historienänderungen. Echte Personendaten werden nie zum Testen verwendet.

## Externe Bibliotheken

Alle vorgeschlagenen Bibliotheken sind frei für eine Kommune nutzbar (MIT, Apache 2.0, BSD, MPL 2.0). Drei bekannte Kandidaten scheiden aus Lizenzgründen aus; sie stehen unter der Tabelle.

| Bibliothek | Zweck | Lizenz |
| --- | --- | --- |
| .NET 11 (erschienen am 10.11.2026), ASP.NET Core, Blazor | Laufzeit, Web, interaktive Oberfläche | MIT |
| Entity Framework Core + Microsoft.Data.Sqlite | Datenzugriff, Migrationen | MIT |
| [Radzen.Blazor](https://github.com/radzenhq/radzen-blazor) | Tabellen, Formulare, Terminplaner (Scheduler), Diagramme | MIT |
| Microsoft.AspNetCore.Authentication.OpenIdConnect | Kommunale Anmeldung | MIT |
| FluentValidation | Eingabeprüfung mit verständlichen Meldungen | Apache 2.0 |
| PDFsharp / MigraDoc | Briefe, Bescheinigungen, Verträge als PDF | MIT |
| QRCoder | QR-Codes erzeugen | MIT |
| html5-qrcode | QR-Codes mit der Kamera im Browser scannen | Apache 2.0 |
| MailKit | E-Mail-Versand | MIT |
| ZUGFeRD-csharp | XRechnung und ZUGFeRD | Apache 2.0 |
| ClosedXML | Excel-Export für Auswertungen | MIT |
| Quartz.NET | Zeitgesteuerte Abläufe | Apache 2.0 |
| Serilog | Strukturiertes Logging | Apache 2.0 |
| Microsoft.AspNetCore.OpenApi + Scalar | API-Beschreibung und Dokumentation | MIT |
| Litestream | Laufende Sicherung der SQLite-Datei | Apache 2.0 |
| xUnit, Shouldly, Verify, Bogus | Tests, Snapshot-Tests, Testdaten | Apache 2.0 / BSD / MIT / MIT |
| Playwright für .NET, axe-core | End-to-End- und Barrierefreiheitstests | Apache 2.0 / MPL 2.0 |
| Mailpit | Mail-Postfach für Entwicklung und Mocks | MIT |

**Bewusst nicht verwendet:**

- **QuestPDF:** Die kostenlose Community-Lizenz schließt öffentliche Stellen ausdrücklich aus ([Lizenz](https://www.questpdf.com/license/community.html)). Daher PDFsharp/MigraDoc.
- **Nager.Date:** Aktuelle Versionen verlangen einen Lizenzschlüssel ([NuGet](https://www.nuget.org/packages/nager.date)). Die NRW-Feiertage werden selbst berechnet (Osterformel), die Schulferien als Datei je Jahr gepflegt.
- **MediatR, AutoMapper, FluentAssertions:** In neueren Versionen kommerziell lizenziert. Sie sind für dieses Projekt ohnehin entbehrlich.
