# Laufzeitmessungen

Messung mit `MessungTests` (Infrastruktur-Tests, nur auf Anforderung: `VIBEVHS_MESSUNG=3000` bzw. `=30000`). Datenbankdatei wie im
Betrieb (SQLite, WAL), Massendaten aus `Testdaten.Massendaten`, dazu 500 bzw. 2000 Kurse mit je 10 Terminen und 12 Belegungen,
20 Räume. Je Messpunkt 8 Läufe, der erste zum Aufwärmen; angegeben ist der **Median** der übrigen 7. Entwicklungsrechner (Windows 11).

## Baseline vor der Normalisierung (16.11.2026, Stand c562cef)

| Messpunkt | 3 000 Personen / 500 Kurse | 30 000 Personen / 2 000 Kurse |
|---|---:|---:|
| Personenliste, erste Seite (50, nach Name) | 1 ms | 6 ms |
| Personenliste, Folgeseite (Keyset) | 1 ms | 7 ms |
| Filter Rolle Dozierende | 1 ms | 6 ms |
| Volltextsuche „müller“ | 1 ms | 8 ms |
| Personendetail (laden + Stand heute) | 1 ms | 1 ms |
| Kursliste (alle Kurse mit Terminen) | 58 ms | 173 ms |
| Raumplan einer Woche | 3 ms | 23 ms |
| Rundschreiben-Empfänger (alle Teilnehmenden) | 1 270 ms | 15 112 ms |
| Startabgleich: Personen-Zwischenstände (Fortschreib-Job) | 537 ms | 7 367 ms |

Auffällig schon vorher: Die Rundschreiben-Empfängerermittlung lädt jede Person vollständig (15 s bei 30 000) – wird im Zuge der
Normalisierung verbessert.

## Nach der Normalisierung (17.11.2026)

Gleiche Messung auf dem Endstand (keine Kopien, keine JSON-Spalten, Änderungsvorgänge, Ids 16 Byte UUIDv7, Tage JJJJMMTT, Zeitpunkte UTC-ms,
Sortierung per Collation, Suche ohne Index). Zum Vergleich wurde der Ausgangsstand (75e3c30) unmittelbar davor auf demselben Rechner erneut
gemessen.

| Messpunkt | 3 000 vorher | 3 000 nachher | 30 000 vorher | 30 000 nachher |
|---|---:|---:|---:|---:|
| Personenliste, erste Seite (50, nach Name) | 1 ms | 76 ms | 7 ms | 71 ms |
| – davon Sortierabfrage (Index `IX_PersonNamen_Sortierung`) | – | 1 ms | – | 1 ms |
| – davon 50 Personen vollständig laden (Stand aus den Versionen) | – | 63 ms | – | 60 ms |
| Personenliste, Folgeseite (Keyset) | 1 ms | 70 ms | 7 ms | 71 ms |
| Filter Rolle Dozierende | 1 ms | 74 ms | 6 ms | 97 ms |
| Volltextsuche „müller“ | 1 ms | 33 ms | 8 ms | 218 ms |
| Personendetail (laden + Stand heute) | 1 ms | 2 ms | 1 ms | 2 ms |
| Kursliste der Oberfläche (alle Kurse, Kennzahlen) | – | 52 ms | – | 376 ms |
| Raumplan einer Woche | 4 ms | 30 ms | 22 ms | 262 ms |
| Rundschreiben-Empfänger (alle Teilnehmenden) | 1 377 ms | 7 ms | 15 929 ms | 104 ms |
| Nächtlicher Fortschreib-Job der Kopien | 592 ms | entfällt | 14 907 ms | entfällt |
| Alle Kurse vollständig mit Terminen laden (keine Oberflächenfunktion) | 59 ms | 339 ms | 209 ms | 1 414 ms |
| **Dateigröße** (nach Checkpoint) | 18,5 MB | 13 MB | 144 MB | 95 MB |

Einordnung:
- **Deutlich schneller:** Rundschreiben (15,9 s → 0,1 s, Zählung und Auswahl in SQL); der nächtliche Fortschreib-Job (15 s) entfällt ganz.
- **Kleiner:** Datei −30 % (3 000) bzw. −34 % (30 000) – vor allem durch 16-Byte-Ids statt 36 Zeichen Text und den Wegfall der Kopien.
- **Personenliste:** Die Sortierabfrage über den Collation-Index bleibt bei 1 ms (`EXPLAIN QUERY PLAN`: `SEARCH … USING INDEX
  IX_PersonNamen_Sortierung (ErsetztDurchVorgangId=? AND Nachname>?)`). Die ~60 ms entstehen beim Laden der 50 Personen der Seite mit allen
  Versionen (Stand wird berechnet statt aus Kopien gelesen) – konstant, unabhängig von der Gesamtzahl.
- **Suche ohne Index:** 218 ms bei 30 000 (vorher FTS 8 ms) – unter 300 ms, dafür ohne Kopie und ohne Pflegeschritt.
- **Kursliste der Oberfläche:** 376 ms (Median; Einzelläufe ab 278 ms) bei 2 000 Kursen in einem Semester – knapp über der 300-ms-Grenze.
  Optimiert wurde bereits auf schmale Projektionen und Joins statt langer Id-Listen (vorher 587 ms). Im Betrieb ist die Liste nach Semester
  gefiltert (typisch einige hundert Kurse); weiterer Schritt bei Bedarf: Seitenweise laden.
- **Alle Kurse vollständig laden** (Messpunkt aus der Ausgangsmessung) ist langsamer, weil Richtlinie, Serie, Team, Anforderungen und Orte
  jetzt eigene Tabellen sind; die Oberfläche nutzt dafür Projektionen (Kursliste, Raumplan).
