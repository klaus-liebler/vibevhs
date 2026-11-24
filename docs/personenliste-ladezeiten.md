# Personenliste: Aufbau und Ladezeiten

Stand 15.11.2026, Iteration I1 (historisch: damals SQLite; seit 24.11.2026 PostgreSQL, aktuelle Werte in `messungen.md`). Messrechner: Entwicklungs-PC (Windows 11, .NET 11, SQLite im WAL-Modus).

## Aufbau

- **Suche zuerst:** Ein Suchfeld oben, Treffer nach Eingabe (250 ms Verzögerung). Gesucht wird über einen
  SQLite-FTS5-Index (`PersonSuche`) mit normalisierten Texten: alle (auch frühere) Namen, E-Mail-Adressen,
  Geburtsdatum (`TT.MM.JJJJ`), Wohnort. Jedes Wort wird als Präfix gesucht, alle Wörter müssen passen.
  Normalisierung: Groß/klein, Akzente, ı→i, ş→s, ç→c, ß→ss, ä/ae→a, ö/oe→o, ü/ue→u.
- **Filter-Chips mit Trefferzahlen:** Rolle, Status (aktiv, ruhend, Löschung vorgemerkt), Fachbereich 1–8,
  Hinweis „mögliche Dubletten“. Die Zahl je Chip berücksichtigt alle übrigen gesetzten Filter.
- **Virtuelles Scrollen statt Blättern:** Blazor `Virtualize` mit `ItemsProvider`. Nachgeladen wird in Blöcken
  von 50 per **Keyset-Pagination** (`Sortierschluessel > letzter Schlüssel`, Index auf der Spalte) statt `OFFSET`,
  deshalb ist eine Seite weit hinten genauso schnell wie die erste.
- **Massenaktionen auf das ganze Filterergebnis:** „Alle n Treffer anschreiben“ (Postausgang, Werbung nur mit
  Einwilligung, vorher Empfänger zählen) und Excel-Export (ClosedXML, protokolliert).
- Die Liste liest nur Zwischenstands-Spalten der Personentabelle (Anzeigename, Sortierschlüssel, Wohnort, E-Mail,
  Rollen, Status, Fachbereiche); die Historie wird erst auf der Personenseite geladen.

## Messwerte (3.012 bzw. 3.028 Personen)

| Messung | Ergebnis | Wie gemessen |
|---|---|---|
| Erste Seite (50 Einträge) | 0,4–0,5 ms | Test `Ladezeit_mit_3000_Personen`, SQLite im Arbeitsspeicher |
| Seite ab Position 2.000 (Keyset) | 0,3–0,9 ms je Block | derselbe Test, Ø über 40 Blöcke |
| Suche „yil“ (Präfix, FTS5) | 0,5–0,9 ms | derselbe Test |
| Trefferzahlen für alle Chips (16 Zählabfragen) | 22–46 ms | derselbe Test |
| Anwendung: Liste öffnen bis Zeilen sichtbar | unter 1 s inkl. Seitenaufbau | headless Edge gegen laufende Anwendung, Datei-Datenbank |
| Anwendung: Sprung ans Listenende (61 Blöcke nachladen) | 29–730 ms | headless Edge bzw. Browser-Pane |
| Excel-Export (166 Dozierende) | 0,5 s, 16 KB | Browser, Datei-Datenbank |
| Erzeugen der 3.012 Testpersonen beim ersten Start | ca. 3 s | Anwendungsprotokoll |

Die Zählabfragen für die Chips sind der teuerste Teil; sie laufen nur bei Filter- oder Suchänderung, nicht beim Scrollen.
Bei deutlich größeren Beständen (über 50.000 Personen) können sie bei Bedarf zusammengefasst oder zwischengespeichert werden.
