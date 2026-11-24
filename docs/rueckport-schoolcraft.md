# Rückport aus SchoolCraft: priorisierte Kandidaten

Stand: 24.11.2026 · Vergleich SchoolCraft `main` (73315f2) mit VibeVHS `main` (c16a334) · **Entscheidungsvorlage für Klaus – noch nichts umgesetzt**

SchoolCraft hat die Bedien- und Datenregeln von VibeVHS übernommen und in den letzten Tagen eigene Konzepte ergänzt. Diese Liste
sagt je Konzept, was eine Volkshochschule davon hat, wie viel Arbeit der Rückport ist und was für VibeVHS anders sein muss
(SQLite statt PostgreSQL, Erwachsene statt Kinder, Tresen und Telefon statt Sekretariat, Abendbetrieb in fremden Gebäuden).

**Aufwand:** S = eine Arbeitssitzung, M = ein bis zwei Sitzungen, L = mehrere Sitzungen mit eigenem Fachkonzept.
**Priorität:** P1 = sofort lohnend (großer Nutzen, kleiner Aufwand), P2 = lohnend, P3 = nur bei konkretem Bedarf.

## Übersicht

| Nr. | Konzept (SchoolCraft) | Nutzen für die vhs | Aufwand | Priorität |
| --- | --- | --- | --- | --- |
| 1 | Löschen nur nach Rückfrage (FA-3230) | hoch | S | **P1** |
| 2 | Tastenkürzel als `<kbd>` und Kürzelliste (FA-3236) | mittel | S | **P1** |
| 3 | Fehlertolerante Personensuche und -auswahl (FA-3227/3228) | sehr hoch (Tresen, Telefon) | M | **P1** |
| 4 | Alles-Suche: Leserecht, <kbd>Strg</kbd>+<kbd>K</kbd>, Pfeiltasten, Funktionen (FA-3231) | hoch | S–M | **P1** |
| 5 | Schnellauswahl <kbd>F8</kbd> für Anrufe und Anliegen (FK-16) | hoch | M | **P2** |
| 6 | 30-Minuten-Korrekturregel (FA-3235) | mittel | S–M | **P2** |
| 7 | Einheitliches Anlegen per Dialog „+ … hinzufügen“ (FA-3229) | mittel | M | **P2** |
| 8 | Info-Monitore im Foyer mit Vorschau (FA-412 ff.) | hoch, sehr sichtbar | M | **P2** |
| 9 | Briefe/Rundschreiben mit Editor, Gestaltungen, Anreden, Archiv als Vorlage (FA-820–824) | hoch | M–L | **P2** |
| 10 | Schlüsselverwaltung (Ausgabe an Kursleitungen) | hoch, wo vhs fremde Gebäude nutzt | M | **P2** |
| 11 | Schulungsinstanzen mit Spielzeit und Sprungmarken (FK-14) | hoch für Einarbeitung, sonst keiner | L | **P3** |
| 12 | Unterrichts-Apps inkl. Podium (FK-15) | mittel (Sprach- und Integrationskurse) | S (Link) bis L (tief) | **P3** |
| 13 | Massenaktionen mit Vorschau und Vier-Augen-Freigabe | mittel | M | P3 |
| 14 | Telefonnotizen und Anruferkennung (Modul 31, ohne Sprachagent) | mittel | M | P3 |
| 15 | Signatur ausgehender Dokumente (FA-2107 ff.) | mittel (Bescheinigungen, BAMF) | L | P3 |

**Empfohlenes erstes Paket:** 1 + 2 + 3 + 4 (zusammen etwa zwei Sitzungen). Die vier Punkte hängen zusammen – die neue Suche bringt
Pfeiltasten und Kürzel mit, die Kürzel brauchen `<kbd>` – und sie verbessern genau die Arbeit am Tresen, für die VibeVHS QA-12 schon fordert.

---

## P1 – sofort lohnend

### 1 Löschen nur nach Rückfrage (FA-3230)

- **SchoolCraft:** `Rueckfragen.cs` (17 Zeilen, Erweiterung auf Radzen `DialogService`): Frage nennt das Objekt und die Folge, die Schaltfläche heißt wie die Folge („Löschen“, nicht „OK“), Klick neben den Dialog schließt nicht.
- **VibeVHS heute:** keine einheitliche Rückfrage. Beispiele ohne Rückfrage: „Entfernen“ (Kursteam, `KursDetail.razor`), „Lichtbild löschen“ (Integration), „Logo entfernen“, „Löschen“ (Abwesenheit), Rolle beenden. Nur der Shop fragt per Doppelklick.
- **Nutzen:** verhindert Fehlklicks am Tresen; einheitliches Bild für Schulungen.
- **Anpassung:** Stornos, die schon einen Grund verlangen (Belege, Belegungen), behalten ihren Grund-Dialog; die Rückfrage kommt nur dort hinzu, wo heute ein Klick reicht. Als Regel in die Konventionen von `CLAUDE.md` und als QA-Nummer in `anforderungen.md`.
- **Aufwand:** S (Hilfsklasse + rund 15 Stellen, Oberflächentest je Stelle).

### 2 Tastenkürzel als `<kbd>` (FA-3236)

- **SchoolCraft:** jeder Tastenhinweis in Oberfläche, Hilfe, Touren und Doku steht in `<kbd>`; Dialoge nennen <kbd>Esc</kbd>, Vorschlagslisten <kbd>↑</kbd> <kbd>↓</kbd> <kbd>Enter</kbd>; die Hilfeseite listet alle Kürzel.
- **VibeVHS heute:** kein `<kbd>`; `docs/hilfe.md` erwähnt „Tab-Taste“ als Fließtext; es gibt keine Kürzelliste.
- **Nutzen:** Kürzel werden überhaupt erst entdeckt; wichtig, sobald 3–5 kommen.
- **Anpassung:** keine; CSS-Stil für `kbd` in `site.css`, `hilfe.md` und `touren/*.json` durchgehen.
- **Aufwand:** S.

### 3 Fehlertolerante Personensuche und -auswahl (FA-3227, FA-3228)

- **SchoolCraft:** `Fachregeln/Unscharf.cs` (153 Zeilen, rein und deterministisch): Groß-/Kleinschreibung, ä = ae = a, ß/ss, Akzente, Präfixe, ein Tippfehler (ab 8 Buchstaben zwei), Bewertung 0–100, beste Treffer zuerst. Baustein `Personenauswahl.razor` mit Vorschlägen beim Tippen, <kbd>↑</kbd> <kbd>↓</kbd> <kbd>Enter</kbd>; Zuordnungsdialoge suchen nur im passenden Kreis und nur mit Recht.
- **VibeVHS heute:** `Suchtext.Normalisieren` deckt Umlaute, Akzente und Umschriften (Yılmaz, Łukasz) schon ab, `Suchtext.Abstand` (Levenshtein) gibt es nur für die Dublettenprüfung. Gesucht wird in SQL über `suchtext()` mit Wortanfängen – „Mueler“ oder „Schmitt/Schmidt“ finden nichts. Die Dozierendensuche im Kursteam ist ein eigenes Textfeld.
- **Nutzen:** sehr hoch: am Telefon werden Namen buchstabiert oder falsch verstanden, viele Teilnehmende haben nicht-deutsche Namen.
- **Anpassung:** VibeVHS hat viele Personen und bewusst keinen Suchindex (`docs/personenliste-ladezeiten.md`). Vorschlag: exakte Treffer weiter in SQL; nur wenn sie nicht reichen, eine zweite SQLite-Funktion `aehnlich(suchtext, eingabe)` (registriert wie `suchtext()` in `SqliteErweiterungen`) mit Vorfilter auf Länge und ersten Buchstaben. Vor der Umsetzung messen (Ziel aus `messungen.md`). `Unscharf` mit `Suchtext` zusammenführen statt zweite Normalisierung einzuführen. Auswahlbaustein für Kursteam, Vollmachten, Firmenansprechpartner, Rechnungsempfänger.
- **Aufwand:** M.

### 4 Alles-Suche verbessern (FA-3231)

- **SchoolCraft:** ein Feld für alle Objektarten **und Funktionen** (Seiten wie „Mahnlauf“), <kbd>Strg</kbd>+<kbd>K</kbd>, fehlertolerant, **streng nur Treffer mit Leserecht** (dieselbe Prüfung wie beim Öffnen; Gesperrtes erscheint weder als Treffer noch als Anzahl).
- **VibeVHS heute:** `GlobaleSuche.razor` findet Personen, Kurse, Orte; QA-12 nennt auch Rechnungen; kein Kürzel; Pfeiltasten prüfen; Leserechte nicht ausdrücklich geprüft (gesperrte Personen nach Datenschutzsperre!).
- **Nutzen:** hoch: schneller Sprung zu Funktionen hilft gerade neuen Mitarbeitenden; Leserecht-Filter schließt eine Datenschutzlücke.
- **Anpassung:** Objektarten der vhs: Personen, Kurse (Nummer), Orte/Räume, Rechnungen/Belege (Nummer), Firmen, Funktionen. Gesperrte und anonymisierte Personen nach den Regeln von FA-11x behandeln. Grenze 8 Treffer + „Alle … anzeigen“ (QA-55) bleibt.
- **Aufwand:** S–M (baut auf Nr. 3 auf).

---

## P2 – lohnend

### 5 Schnellauswahl <kbd>F8</kbd> (FK-16)

- **SchoolCraft:** Schaltfläche „⚡ Schnell“ und <kbd>F8</kbd> auf jeder Verwaltungsseite, fest nummerierte Punkte, Ziffer wählt, volle Eingabehilfe; Alarmierendes mit roter Vollbild-Sicherheitsabfrage.
- **Nutzen für die vhs:** der Tresen und das Telefon sind der Engpass, besonders zu Semesterbeginn.
- **Anpassung:** andere Punkte, z. B. 1 Anmeldung zu Kurs, 2 Abmeldung/Storno, 3 Kursleitung fällt aus (→ Termin absagen, Teilnehmende informieren – gibt es schon), 4 Rückrufbitte/Telefonnotiz, 5 Adresse oder Bankverbindung geändert (Änderung ab Datum), 6 Bescheinigung erneut senden. Der Hilferuf entfällt zunächst; ein Notfallpunkt für den Abendbetrieb (Hausdienst, Ersthelfer) wäre denkbar, braucht aber Rollen, die die vhs heute nicht kennt.
- **Aufwand:** M (Baustein 275 Zeilen übertragbar, Punkte neu verdrahten).

### 6 30-Minuten-Korrekturregel (FA-3235)

- **SchoolCraft:** `Korrekturregel.cs` (33 Zeilen): eigene Einträge 30 Minuten ohne Begründung zurückziehen/berichtigen, danach und für die Verwaltung nur mit Grund; Storno statt Löschen.
- **Nutzen:** weniger Rückfragen bei Tippfehlern, trotzdem nachvollziehbar.
- **Anpassung:** **nicht** für festgeschriebene Belege (GoBD, FA-126 bleibt: Storno und Neubeleg mit Grund). Geeignet für Anwesenheit und Check-in (Kursleitungen im Portal), Notizen, Aufgabenkommentare, Zuordnung von Zahlungseingängen vor Festschreibung. Bitemporale Stammdaten haben mit „Korrektur“ schon ihr eigenes Modell – dort nicht doppeln.
- **Aufwand:** S–M.

### 7 Einheitliches Anlegen per Dialog (FA-3229)

- **SchoolCraft:** „+ ‹Objekt› hinzufügen“ öffnet einen modalen Dialog mit OK/Abbrechen; OK nennt, was fehlt; Eingaben bleiben bei Fehlern erhalten (`Hinzufuegen.razor`, `Dialogknoepfe.razor`).
- **VibeVHS heute:** gemischt: eigene Seiten (Person, Kurs), Inline-Formulare (Termine, Rollen, Ortsbedingungen).
- **Anpassung:** Für große Objekte (Person, Kurs) bleiben eigene Seiten mit Dublettenprüfung; Dialoge für kleine Unterobjekte (Termin, Rolle, Merkmal, Raum, Ermäßigung, Vollmacht). Blazor-SSR-Formulare (`@formname`) beachten: Dialoge nur auf interaktiven Seiten.
- **Aufwand:** M (seitenweise, gut in Etappen).

### 8 Info-Monitore im Foyer mit Vorschau (FA-412 ff.)

- **SchoolCraft:** Monitore mit geheimer Adresse (nur Prüfwert gespeichert), Sperren, Vorschau im neuen Fenster mit eigenem 30-Minuten-Token, Echtzeit-Aktualisierung (`MonitorDienst`, `monitor.js`).
- **Nutzen für die vhs:** „Heute im Haus“ – Kurse mit Raum und Uhrzeit, Raumänderungen und Ausfälle tagesaktuell, je Standort (Emsdetten, Greven, Saerbeck). Ersetzt ausgedruckte Aushänge; Ausfälle erreichen auch die, die keine Mail lesen.
- **Anpassung:** Inhalt aus Raumplan und Terminausfällen; je Monitor ein Ort bzw. Gebäude; nur Kurstitel, Raum, Zeit, ggf. Kursleitung (Einwilligung prüfen), nie Teilnehmende. Mehrsprachige Anzeige optional.
- **Aufwand:** M.

### 9 Briefe und Rundschreiben mit Editor, Gestaltungen, Anreden und Archiv (FA-820 bis FA-824)

- **SchoolCraft:** Zielgruppen frei kombinierbar; Radzen-HTML-Editor mit serverseitiger Bereinigung (`HtmlBereinigung`, Whitelist); Gestaltungen (schlicht, Briefkopf, jahreszeitlich) gleich in App, Portal und Mail; Platzhalter mit vier Anredestilen und geschlechtsneutraler Ausweich-Anrede; Archiv gesendeter Briefe als Vorlage.
- **VibeVHS heute:** Textvorlagen als reiner Text (`VorlagenSeite`), Anredestil förmlich/informell mit neutraler Ausweich-Anrede (schon da), Rundschreiben über Personenfilter mit Einwilligungsprüfung (schon da), kein Archiv als Vorlage.
- **Nutzen:** Programmankündigungen, Semesterbriefe an Kursleitungen, Einladungen – heute außerhalb des Systems gestaltet.
- **Anpassung:** Zielgruppen sind Personenfilter, Kurse, Fachbereiche, Kursleitungen (statt Jahrgänge/Klassen). Übersetzungslink (QA-21) und Einwilligung je Kontaktzweck bleiben Pflicht. Gestaltung „Briefkopf“ aus Einstellungen (Logo, Website). **PDF-Briefe:** MigraDoc kann kein HTML – entweder nur eine kleine Teilmenge (fett, kursiv, Listen, Überschriften) nach MigraDoc übersetzen oder Briefe auf Papier ohne Formatierung lassen. Anrede „Du“ nur für ausgewählte Bereiche (z. B. Junge vhs).
- **Aufwand:** M (Mail und Portal) bis L (mit formatiertem PDF).

### 10 Schlüsselverwaltung

- **SchoolCraft:** Schlüssel mit Ausgabe an Beschäftigte, Zuordnung per fehlertoleranter Auswahl im passenden Kreis.
- **Nutzen für die vhs:** Kursleitungen bekommen Schlüssel für Schulen, Gemeindehäuser und Außenstellen; heute vermutlich auf Papier. Rückgabe bei Ende der Kursleitung als Aufgabe.
- **Anpassung:** Schlüssel gehört zu einem Ort (vorhandene Orte), Ausgabe an Personen mit Rolle Dozierende (bitemporal: ausgegeben ab/zurück am), Aufgabe „Schlüssel zurückfordern“, wenn keine Kurse mehr am Ort.
- **Aufwand:** M.

---

## P3 – bei konkretem Bedarf

### 11 Schulungsinstanzen mit Spielzeit (FK-14)

- **SchoolCraft:** bis 20 Instanzen mit eigener Datenbank als Kopie einer Vorlage je Sprungmarke, Spielzeit (`Spielzeit : TimeProvider` mit Anhalten, Zeitlupe, Zeitraffer), Drehbuch, Spielzeitleiste, Werkzeug `instanzen.ps1`.
- **Nutzen für die vhs:** Einarbeitung neuer Verwaltungskräfte in den Semesterlauf (Programm veröffentlichen → Anmeldestart → Kursbeginn → Mahnlauf → Bescheinigungen) an einem Vormittag. Ergänzt den vorhandenen Abnahme- und Schulungsmodus mit Touren.
- **Anpassung und Vorteil:** VibeVHS nutzt **SQLite** – eine Vorlage ist einfach eine Datei, kopieren statt `CREATE DATABASE … TEMPLATE`; R2 (nur gekennzeichnete Datenbanken löschen) über `Schemainfo` umsetzen. `TimeProvider` ist schon durchgängig eingespritzt (rund 80 Stellen), die Spielzeit lässt sich also fast direkt einsetzen. Aufwändig sind: Quartz-Jobs (eigene Uhr, müssen im Schulungsbetrieb der Spielzeit folgen wie in FK-14 R5), ein **Drehbuch für ein vhs-Semester** (gibt es noch nicht; SchoolCraft hatte seine Vorführungswoche schon) und die Realzeit-Ausnahmen (Check-in-Tokens, Shop, Portale).
- **Aufwand:** L. Sinnvoll, sobald echte Schulungen der vhs EGS geplant sind.

### 12 Unterrichts-Apps inkl. Podium (FK-15)

- **SchoolCraft:** Katalog eingebauter (Podium, GeoGebra, H5P, Zeichenfläche) und angebundener Apps (LTI 1.3, Link), Einsatz je Lerngruppe, Ergebnisse zurück.
- **Nutzen für die vhs:** mittel; Sprach- und Integrationskurse nutzen Quiz und H5P, EDV-Kurse kaum. Kursleitungen sind Honorarkräfte mit eigenen Werkzeugen.
- **Anpassung:** Stufe 1 (S): im Dozierendenportal je Kurs Links zu freigegebenen Apps (z. B. selbst gehostetes Podium) mit Datenschutzhinweis. Stufe 2 (L): Podium-Runde aus dem Kurs starten, Teilnahme mit Spitzname ohne Konto – keine Noten, also keine Übernahme ins Notenbuch.
- **Aufwand:** S bis L.

### 13 Massenaktionen mit Vorschau und Vier-Augen-Freigabe

Aus der Geräteverwaltung von SchoolCraft. Für die vhs denkbar bei Mahnlauf, Semesterkopie und Sammelstorno eines Kurses. VibeVHS hat Vorschauen schon an einigen Stellen; die Vier-Augen-Freigabe wäre neu. M.

### 14 Telefonnotizen und Anruferkennung (Modul 31, ohne Sprachagent)

Eingehende Nummer → passende Person vorschlagen, Notiz mit Rückrufaufgabe. Passt gut zu Nr. 5; braucht eine Telefonanlage mit Schnittstelle (offen). M.

### 15 Signatur ausgehender Dokumente

SchoolCraft signiert alles, was das System verlässt (Siegel des Servers, Nutzerzertifikate im Browser). VibeVHS hat SHA-256 im Dokumentarchiv; eine Signatur von Bescheinigungen und BAMF-Unterlagen wäre ein Mehrwert, aber ein eigenes Projekt. L.

---

## Bewusst nicht vorgeschlagen

Schulspezifisches ohne vhs-Gegenstück: Stundenplan und Vertretung, Klassenbuch, Zeugnisse, Oberstufe, Geräteverwaltung (Android), Schuljahreswechsel,
Kinderschutz, Mensa, Wahlen und Gremien, Sportnachweise, ePortfolio, SmartStatistics (die amtliche vhs-Statistik ist in VibeVHS fest umgesetzt).

## Nächster Schritt

Klaus wählt die Punkte aus. Für jeden gewählten Punkt: Anforderung in `docs/spezifikation/anforderungen.md` (neue QA-/FA-Nummern),
Umsetzung mit Tests und Browserprüfung auf Port 5199, danach Übertrag nach fraktionperfekt prüfen (Regel aus `CLAUDE.md`).
