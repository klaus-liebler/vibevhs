# Betrieb: Sicherung, Wiederherstellung, Messwerte

## Datensicherung (nativ unter Windows, jetzt)

- Täglich um **01:30 Uhr** (`Sicherung:Zeitplan`, Quartz-Cron) sichert VibeVHS die SQLite-Datenbank mit der
  **Online-Backup-API** von SQLite: konsistent, auch während Benutzer arbeiten.
- Ziel: `Sicherung:Ordner` (Standard: Ordner `sicherung` neben der Datenbank). Je Sicherung eine Datei
  `vibevhs-JJJJMMTT-HHMMSS.db` und eine `.json` mit **SHA-256**, Größe, Schemaversion und Testergebnis.
- Direkt danach läuft der **Wiederherstellungstest**: Prüfsumme vergleichen, Kopie in einen temporären Ordner
  „wiederherstellen“, öffnen, `PRAGMA integrity_check` (mit registrierter Collation), Schemaversion mit der laufenden Datenbank vergleichen,
  Zeilen der Kerntabellen zählen. Das Ergebnis steht im Protokoll und auf der Seite **Betrieb**.
- Es bleiben die letzten `Sicherung:Behalten` (Standard 14) Sicherungen. `Sicherung:Aktiv=false` schaltet den Job ab.
- **Wichtig:** Der Sicherungsordner liegt standardmäßig auf demselben Datenträger. Für echten Schutz den Ordner auf
  ein anderes Laufwerk oder Netzlaufwerk legen bzw. ihn von der Dateisicherung des Rechenzentrums erfassen lassen.

## Wiederherstellung im Notfall

1. Anwendung beenden.
2. Auf der Seite **Betrieb** die jüngste Sicherung mit „Wiederherstellungstest ok“ wählen (oder „erneut prüfen“).
3. Prüfsumme vergleichen: `Get-FileHash vibevhs-….db -Algorithm SHA256` muss dem Wert in der `.json` entsprechen.
4. Die laufende `vibevhs.db` (und ggf. `-wal`/`-shm`) beiseitelegen, die Sicherung als `vibevhs.db` einsetzen.
5. Anwendung starten. Passt die Schemaversion der Sicherung nicht zur Anwendung, meldet der Start „Datenbankschema veraltet“ (siehe unten).
6. Im Protokoll vermerken, was verloren ist (Zeitraum seit der Sicherung); Postausgang prüfen.

## Später unter Linux: Litestream

Vorlage: `deploy/linux/litestream.yml`. Litestream repliziert das WAL fortlaufend (Sekunden statt einmal täglich).
Voraussetzung ist der WAL-Modus der Datenbank (`PRAGMA journal_mode=WAL;` einmalig setzen). Die tägliche
Online-Sicherung bleibt als zweite, unabhängige Linie bestehen. Wiederherstellungsprobe regelmäßig mit
`litestream restore` und `PRAGMA integrity_check`.

## Lastmessung (I8)

Test `BetriebTests.Lastmessung_3000_Personen_500_Kurse` (SQLite im Arbeitsspeicher, Entwicklungsrechner, 15.11.2026):

| Vorgang | Dauer |
|---|---|
| Aufbau 3.000 Personen, 500 Kurse mit 5.000 Terminen, 6.000 Belegungen | 4,4 s |
| Personensuche „müller“ (erste Seite, 50) | 29 ms |
| Auswertung Kursliste (500 Kurse mit Kennzahlen) | 188 ms |
| Dashboard | 103 ms |
| VHS-Statistik (Programmbereiche) | 104 ms |
| Excel-Export Kursliste | 595 ms |

Grenze im Test: jede Abfrage unter 5 s. Ältere Messung der Personenliste: `docs/personenliste-ladezeiten.md`.

## Zeitzone

In der Datenbank stehen alle Zeitpunkte und Termine in UTC (Millisekunden seit 1970). Angezeigt und eingegeben wird in der Zeitzone aus der
Einstellung `Zeitzone` (z. B. `Zeitzone=Europe/Berlin` als Umgebungsvariable bzw. in appsettings). Fehlt sie, gilt die Systemzeitzone des
Servers – auf einem Linux-Server steht diese oft auf UTC, dann zeigt die Anwendung UTC-Uhrzeiten. Deshalb auf dem Zielsystem entweder
`Zeitzone=Europe/Berlin` setzen oder die Systemzeitzone prüfen und setzen:

```bash
timedatectl
sudo timedatectl set-timezone Europe/Berlin
```

Serientermine werden je Termin aus der Ortszeit („dienstags 18:00“) nach UTC umgerechnet und bleiben so nach der Zeitumstellung um 18:00
Ortszeit.

## Datenbankschema (Entwicklungsphase: ohne Migrationen)

Solange es nur Testdaten gibt, entsteht die Datenbank beim ersten Start komplett neu aus dem Modell (`EnsureCreated`), danach legt die
Anwendung die lesbaren Sichten `v_…` an und vermerkt die Schemaversion (Prüfsumme über das Schema) in der Tabelle `Schemainfo`;
anschließend kommen Start- und Demodaten. Passt eine vorhandene Datenbankdatei nicht zum aktuellen Modell (ältere Version, Datei aus der
Zeit mit Migrationen), bricht der Start mit einer klaren Meldung ab:

> Datenbankschema veraltet (…: Version …, erwartet …). Bitte die Datenbankdatei löschen oder beiseitelegen und die Anwendung neu starten –
> sie wird dann neu aus Start- und Demodaten aufgebaut.

Dann die Datei `data/vibevhs.db` (mit `-wal` und `-shm`) löschen oder umbenennen und neu starten.

**Vor dem ersten Produktivbetrieb werden wieder EF-Migrationen eingeführt** (Anfangsmigration aus dem dann aktuellen Modell, Sichten und
Collation-Index per SQL in der Migration), damit Daten bei Updates erhalten bleiben.

## Werkzeuge und Collation

Namen sortieren in der Datenbank per Collation `DIN5007_2`, die Suche nutzt die Funktion `suchtext()` – beide registriert die Anwendung bei
jeder Verbindung. Ein DB-Browser ohne diese Erweiterungen kann die Datenbank lesen (am besten über die Sichten `v_…`), aber nicht nach
Namen sortieren, keine Namensversionen schreiben und kein `PRAGMA integrity_check` ausführen. Daten deshalb nur über die Anwendung ändern.

