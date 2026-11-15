# Betrieb: Sicherung, Wiederherstellung, Messwerte

## Datensicherung (nativ unter Windows, jetzt)

- Täglich um **01:30 Uhr** (`Sicherung:Zeitplan`, Quartz-Cron) sichert VibeVHS die SQLite-Datenbank mit der
  **Online-Backup-API** von SQLite: konsistent, auch während Benutzer arbeiten.
- Ziel: `Sicherung:Ordner` (Standard: Ordner `sicherung` neben der Datenbank). Je Sicherung eine Datei
  `vibevhs-JJJJMMTT-HHMMSS.db` und eine `.json` mit **SHA-256**, Größe, Migrationsstand und Testergebnis.
- Direkt danach läuft der **Wiederherstellungstest**: Prüfsumme vergleichen, Kopie in einen temporären Ordner
  „wiederherstellen“, öffnen, `PRAGMA integrity_check`, Migrationsstand mit der laufenden Datenbank vergleichen,
  Zeilen der Kerntabellen zählen. Das Ergebnis steht im Protokoll und auf der Seite **Betrieb**.
- Es bleiben die letzten `Sicherung:Behalten` (Standard 14) Sicherungen. `Sicherung:Aktiv=false` schaltet den Job ab.
- **Wichtig:** Der Sicherungsordner liegt standardmäßig auf demselben Datenträger. Für echten Schutz den Ordner auf
  ein anderes Laufwerk oder Netzlaufwerk legen bzw. ihn von der Dateisicherung des Rechenzentrums erfassen lassen.

## Wiederherstellung im Notfall

1. Anwendung beenden.
2. Auf der Seite **Betrieb** die jüngste Sicherung mit „Wiederherstellungstest ok“ wählen (oder „erneut prüfen“).
3. Prüfsumme vergleichen: `Get-FileHash vibevhs-….db -Algorithm SHA256` muss dem Wert in der `.json` entsprechen.
4. Die laufende `vibevhs.db` (und ggf. `-wal`/`-shm`) beiseitelegen, die Sicherung als `vibevhs.db` einsetzen.
5. Anwendung starten. Migrationen laufen beim Start automatisch nach, falls die Sicherung älter ist.
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
