# Betrieb: Sicherung, Wiederherstellung, Messwerte

## Datenbank

PostgreSQL 17 (seit 24.11.2026, vorher SQLite). Entwicklung: `docker compose -f deploy/docker-compose.yml up -d` (Container `vibevhs-db-1`,
`127.0.0.1:5441`, Verbindung in `appsettings.Development.json`). Testsystem: Container `vibevhs-db` (Schritt 10 in
`deploy/server/README-server.md`). Verbindung immer über `ConnectionStrings:VibeVHS`; die Kommentare liegen in der eigenen Datenbank
`<haupt>_kommentare` (`ConnectionStrings:Kommentare` überschreibt das). Mandanten: je VHS eine eigene Datenbank (`Mandanten:<Kennung>:Datenbank`).

## Datensicherung

- Täglich um **01:30 Uhr** (`Sicherung:Zeitplan`, Quartz-Cron) sichert VibeVHS die Datenbank mit **`pg_dump`** im Archivformat:
  konsistent (Momentaufnahme einer Transaktion), auch während Benutzer arbeiten.
- Ziel: `Sicherung:Ordner` (Standard: `data/sicherung` im Inhaltsverzeichnis, Testsystem `/var/backups/vibevhs`). Je Sicherung eine Datei
  `vibevhs-JJJJMMTT-HHMMSS.dump` und eine `.json` mit **SHA-256**, Größe, Schemaversion und Testergebnis.
- Direkt danach läuft der **Wiederherstellungstest**: Prüfsumme vergleichen, Sicherung mit `pg_restore` in eine **Wegwerf-Datenbank** auf
  demselben Server zurückspielen, Schemaversion mit der laufenden Datenbank vergleichen, Zeilen der Kerntabellen zählen, Wegwerf-Datenbank
  löschen. Das Ergebnis steht im Protokoll und auf der Seite **Betrieb**.
- Werkzeuge: `Sicherung:Werkzeug=docker` ruft pg_dump/pg_restore im Container `Sicherung:Container` auf (Entwicklung), sonst die Programme
  aus `Sicherung:PgBin` bzw. dem Suchpfad (Testsystem: `/usr/lib/postgresql/17/bin`, ohne Docker-Rechte für den Dienstbenutzer).
- Es bleiben die letzten `Sicherung:Behalten` (Standard 14) Sicherungen. `Sicherung:Aktiv=false` schaltet den Job ab.
- Die **Kommentar-Datenbank** wird bei jeder Sicherung als `kommentare-JJJJMMTT-HHMMSS.dump` mitgesichert und mit der Hauptsicherung aufgeräumt.
- **Wichtig:** Der Sicherungsordner liegt standardmäßig auf demselben Datenträger. Für echten Schutz den Ordner auf
  ein anderes Laufwerk oder Netzlaufwerk legen bzw. ihn von der Dateisicherung des Rechenzentrums erfassen lassen.

## Wiederherstellung im Notfall

1. Anwendung beenden.
2. Auf der Seite **Betrieb** die jüngste Sicherung mit „Wiederherstellungstest ok“ wählen (oder „erneut prüfen“).
3. Prüfsumme vergleichen: `sha256sum vibevhs-….dump` bzw. `Get-FileHash … -Algorithm SHA256` muss dem Wert in der `.json` entsprechen.
4. Laufende Datenbank umbenennen (`ALTER DATABASE vibevhs RENAME TO vibevhs_defekt`), leere anlegen (`createdb vibevhs`), Sicherung einspielen:
   `pg_restore --no-owner -d vibevhs vibevhs-….dump` (im Container: `docker exec -i vibevhs-db pg_restore -U vibevhs -d vibevhs --no-owner < datei`).
5. Anwendung starten. Passt die Schemaversion der Sicherung nicht zur Anwendung, meldet der Start „Datenbankschema veraltet“ (siehe unten).
6. Im Protokoll vermerken, was verloren ist (Zeitraum seit der Sicherung); Postausgang prüfen.

## Lastmessung (I8)

Test `BetriebTests.Lastmessung_3000_Personen_500_Kurse` (damals SQLite im Arbeitsspeicher, Entwicklungsrechner, 15.11.2026; aktuelle Werte mit PostgreSQL in `docs/messungen.md`):

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

In der Datenbank stehen alle Zeitpunkte und Termine als `timestamptz` in UTC (auf Millisekunden gekürzt). Angezeigt und eingegeben wird in der Zeitzone aus der
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
Anwendung das Zusatzschema an (SQL-Funktionen für die Suche, Trigramm-Indizes, lesbare Sichten `v_…`) und vermerkt die Schemaversion
(Prüfsumme über das Schema) in der Tabelle `Schemainfo`; anschließend kommen Start- und Demodaten. Passt eine vorhandene Datenbank nicht zum
aktuellen Modell, bricht der Start mit einer klaren Meldung ab:

> Datenbankschema veraltet (Datenbank „…“: Version …, erwartet …). Bitte die Datenbank beiseitelegen (Datenbank:BeiAbweichung = Beiseitelegen
> oder deploy.ps1 -DbNeu) und die Anwendung neu starten – sie wird dann neu aus Start- und Demodaten aufgebaut.

In der Entwicklung steht `Datenbank:BeiAbweichung=Beiseitelegen` in `appsettings.Development.json`: Die Anwendung benennt die alte Datenbank
in `vibevhs_alt_JJJJMMTT_HHMMSS` um und beginnt neu; die drei jüngsten beiseitegelegten bleiben, ältere löscht sie. Auf dem Testsystem
erledigt das `deploy\server\deploy.ps1 -DbNeu` (bei Fehlstart automatisch zurück; siehe `deploy/server/README-server.md`).

**Ausnahme Kommentar-Datenbank:** Sie wird nie gelöscht oder beiseitegelegt (Kommentare von Leitung und Systemverwaltung). Sie hat eine
eigene Schemaversion (Tabelle `Schemainfo`, `KommentarDbContext.Schemaversion`); passt sie nicht, bricht der Start mit dem Hinweis ab, die
Datenbank per Skript anzupassen. Eine alte `kommentare.db` aus der SQLite-Zeit (`Kommentare:Altdatei`) übernimmt die Anwendung beim ersten
Start in die leere Kommentar-Datenbank und benennt die Datei in `kommentare.db.uebernommen` um.

**Vor dem ersten Produktivbetrieb werden wieder EF-Migrationen eingeführt** (Anfangsmigration aus dem dann aktuellen Modell, Zusatzschema
per SQL in der Migration), damit Daten bei Updates erhalten bleiben.

## Werkzeuge, Sortierung und Suche

Namen sortieren in der Datenbank per ICU-Collation `DIN5007_2` (`de-DE-u-co-phonebk`, Telefonbuch: ä = ae), die PostgreSQL selbst kennt;
jedes Werkzeug (psql, DBeaver, pgAdmin) sortiert also gleich. Die Suche nutzt die SQL-Funktionen `suchtext`, `suchwoerter` und
`namensuchtext` (gleiche Normalisierung wie `Suchtext.Normalisieren` in C#) mit Trigramm-Indizes (`pg_trgm`); die Erweiterungen `pg_trgm`,
`unaccent` und `fuzzystrmatch` legt die Anwendung beim Anlegen der Datenbank an. Am bequemsten liest man über die Sichten `v_…`.
Daten nur über die Anwendung ändern (Protokoll, bitemporale Versionen).
