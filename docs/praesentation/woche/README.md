# „Eine turbulente Woche an der vhs“

Präsentation für Verwaltungsmitarbeitende (nach dem Vorbild der SchoolCraft-„Schulwoche“): Montag bis Freitag im Alltag der vhs EGS,
mit **echten Bildschirmfotos** der laufenden Anwendung. Mitarbeitende tragen verfremdete Namen (Vorname echt, Nachname geändert, ohne Kontaktdaten),
Teilnehmende und Kursleitungen sind erfunden (Demodaten).

| Datei | Inhalt |
| --- | --- |
| `woche.pptx` | die Präsentation (bearbeitbar, mit Sprechernotizen); PDF per PowerPoint „Speichern unter → PDF“ |
| `aufnahmen.json` | Drehbuch für die Aufnahmen: Spielzeit, Konto, Seite, Eingaben, Klicks, Bildschirmfoto |
| `szenen.py` | Texte der Folien → `folien.json` und `drehbuch.md` |
| `bilder.py` | Bildschirmfotos zuschneiden (leere Ränder) und als JPEG nach `bilder/` legen |

## Neu aufnehmen

Die Woche läuft im **Schulungsbetrieb** (docs/schulungsbetrieb.md) auf einer eigenen Instanz mit eigener Spielzeit; die Ereignisse
(Anmeldung, Absage, Abmeldung, Ausfallmeldung, Mahnungen …) entstehen durch Klicks in der echten Oberfläche, die Uhr läuft über
„+1 Stunde“ auf der Seite Spielzeit – die nächtliche Automatik läuft dabei wie im Betrieb.

1. Anwendung veröffentlichen und Vorlagen mit eigenem Präfix bauen (Umgebungsvariablen wie in `tools/instanzen.ps1`, zusätzlich
   `Schulung__Vorlagen=vibevhs_p5199_vorlage`, `Schulung__VorlagenBauen=true`, `Demodaten__Anlegen=true`, `Demodaten__Massendaten=500`).
2. Instanz auf Port 5199 starten: `ConnectionStrings__VibeVHS=…;Database=vibevhs_p5199_i01`, `Schulung__Aktiv=true`,
   `Schulung__Schluessel=woche-lokal`, `Schulung__Startmarke=ausgangslage`, `ASPNETCORE_URLS=http://localhost:5199`.
3. `node tools/praesentation/app-bilder.mjs docs/praesentation/woche/aufnahmen.json <ordner>` – springt zur Marke „ausgangslage“,
   spielt die Woche durch und fotografiert (Verwaltung 1600 × 1000 ohne Seitenleiste, Handy 390 × 844, doppelte Pixeldichte).
4. `py -3 docs/praesentation/woche/bilder.py <ordner>`, dann `py -3 docs/praesentation/woche/szenen.py` und
   `py -3 tools/praesentation/praesentation.py woche`.

Der Lauf beginnt immer mit einem Sprung zur Ausgangslage; er verändert nur die Datenbank der Schulungsinstanz.
