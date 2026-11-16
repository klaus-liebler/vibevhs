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

## Nach der Normalisierung

(folgt)
