# Mandantenfähigkeit – Konzept

**Entscheidung (Klaus, 16.11.2026): eine Datenbank je Volkshochschule.** Heute läuft genau ein Mandant; vorbereitet ist,
dass weitere ohne Umbau des Datenmodells hinzukommen können.

## Varianten im Vergleich

| | **Eigene Datenbank je VHS** (gewählt) | **Eine Datenbank, Spalte MandantId** |
|---|---|---|
| Datenschutz | strikte Trennung; ein Fehler in einer Abfrage kann keine fremden Daten zeigen; Löschen eines Mandanten = Datei löschen | jede Abfrage braucht den Filter; ein vergessener Filter zeigt fremde Personendaten (hohes Risiko, Architekturtests nötig) |
| Sicherung / Wiederherstellung | je VHS einzeln (Online-Sicherung, Litestream je Datei); Rücksicherung betrifft nur eine VHS | nur gemeinsam; Rücksicherung einer VHS = aufwendiges Herauslösen |
| Updates / Migrationen | je Datenbank beim Start (oder nacheinander); eine fehlerhafte Migration trifft zunächst nur eine VHS | eine Migration für alle; kurzer, aber gemeinsamer Ausfall |
| Auswertungen über Mandanten | nur über Export/Statistik-Schnittstelle (DIE, WbG) bzw. eine eigene Auswertungsdatenbank | einfach per Abfrage – aber datenschutzrechtlich ohnehin nur aggregiert erlaubt |
| SQLite | passt gut: eine Datei je VHS, Schreibsperre je Datei – keine gegenseitige Blockade | eine Datei für alle: Schreibsperre trifft alle VHS, Datei wächst |
| Aufwand im Code | gering: Verbindung je Anfrage wählen, Hintergrundjobs je Mandant | hoch: jede Tabelle, jeder Index, jede Abfrage, jeder Test |
| Betrieb | n Dateien überwachen (Größe, Sicherung) | eine Datei |

## Was schon vorbereitet ist

- `Mandanten` / `Mandant` / `Mandantenkontext` (Application/Betrieb): Liste der Mandanten aus der Konfiguration
  (`Mandanten:<kennung>:Datenbank`, optional `:Host`), Auflösung aus Hostname oder erstem Pfadsegment. Ohne Eintrag gibt es genau einen
  Mandanten „standard“ mit `ConnectionStrings:VibeVHS` – das heutige Verhalten.
- Der `VibeVhsDbContext` holt seine Verbindung je Scope aus dem `Mandantenkontext` (nicht mehr fest beim Start).
- Alle VHS-spezifischen Einstellungen liegen in der Datenbank des Mandanten (Einrichtung: Name, Logo, Website, Bundesland, Absender;
  Finanzeinstellungen; Vorlagen; Ferien) – nicht in Konfigurationsdateien.

## Was beim Einschalten noch zu tun ist (nicht umgesetzt)

1. Middleware: `Mandanten.Aufloesen(Host, Pfad)` je Anfrage, `Mandantenkontext.Setzen(...)`; bei Pfad-Variante zusätzlich PathBase je Mandant.
2. Singletons mit Mandantendaten auf „je Mandant“ umstellen: `Briefkopf` (Logo/Name für PDFs), `OeffentlicheAdresse`/`Uebersetzungslinks`
   (Basisadresse), `ShopZwischenspeicher`, `Startabgleich` (Fortschritt), Data-Protection-Schlüssel (je Mandant eigener Ordner).
3. Hintergrundjobs (Quartz): jeder Job läuft je Mandant in einem eigenen Scope mit gesetztem Mandantenkontext.
4. Startdaten, Migrationen und Sicherung je Mandant (beim Start alle Datenbanken migrieren; Sicherungsordner je Mandant).
5. Anmeldung: Benutzerkonten sind je Mandant; ein Konto gilt nur in „seiner“ VHS (Cookie-Name bzw. -Pfad je Mandant).
6. Speicherorte (Dokumente, Logs) je Mandant trennen.
7. Zeitzone je Mandant: heute eine Einstellung „Zeitzone“ für die ganze Anwendung (`Domain.Gemeinsam.Zeitzone`, statisch); beim Einschalten
   aus der Mandantenkonfiguration lesen und die Speicher-Konverter (Termine/Zeitpunkte) über den Mandantenkontext umrechnen lassen.

Grober Aufwand für 1–7: 3–4 Arbeitstage plus Tests (insbesondere „kein Zugriff über Mandantengrenzen“).
