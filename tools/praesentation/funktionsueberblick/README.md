# Generator „VibeVHS – Funktionsüberblick“

Erzeugt [`docs/praesentation/VibeVHS-Praesentation.pptx`](../../../docs/praesentation/VibeVHS-Praesentation.pptx) (32 Folien, Sprechernotizen,
eigene Layouts und Farbschema) mit **pptxgenjs** aus echten Bildschirmfotos der Anwendung.

**Die pptx-Datei nicht von Hand bearbeiten**: Änderungen gehören in `folien.js`, sonst überschreibt der nächste Lauf sie.

| Datei | Inhalt |
| --- | --- |
| `folien.js` | alle Folien, Texte, Sprechernotizen, Markierungen (`marker(s, r, x, y, n)`: Anteil von Breite/Höhe des Bildes) |
| `aufnahme.mjs` | alle Bildschirmfotos in einem Lauf (Schritte einzeln aufrufbar) → `shots/`, `crops/` |
| `cdp.mjs` | kleiner Headless-Edge/Chrome-Treiber (DevTools-Protokoll) |
| `symbole.js` | Symbole (Material Icons) als PNG → `symbole/` |
| `thema.js` | schreibt Farbschema und Namen des Themas in die pptx |

`shots/`, `crops/`, `symbole/` und `node_modules/` sind erzeugt und nicht im Repo.

## Ablauf

1. Eigene Instanz auf Port 5199 mit Demodaten starten (PostgreSQL-Container läuft, eigene Datenbank, Klaus' Instanz nicht anfassen):
   `ASPNETCORE_ENVIRONMENT=Development`, `ConnectionStrings__VibeVHS=…;Database=vibevhs_p5199`, `Demodaten__Anlegen=true`,
   `Demodaten__Massendaten=3000`, `Sicherung__Aktiv=false`, `Speicher__*` auf ein Temp-Verzeichnis, `ASPNETCORE_URLS=http://localhost:5199`.
2. `npm install`, dann `npm run symbole`.
3. Honorarkraft fürs Dozierendenportal: Anmeldelink anfordern (`/portal/dozierende/anmelden` mit der E-Mail z. B. von Till Rother,
   Adresse in Tabelle `PersonEMails`), Link aus der Tabelle `Postausgang` lesen.
4. `DOZ_LOGIN=<link> npm run aufnahme` (einzelne Schritte: `node aufnahme.mjs suche kursneu …`).
5. `npm run folien`, dann jede Folie mit Markierungen ansehen (PowerPoint „Exportieren“ oder Folie als PNG) und
   die Markierungen in `folien.js` nachziehen, wenn sich die Oberfläche verschoben hat.

Die Suchbeispiele (Folie 9) hängen an den Demodaten (z. B. Ayşe Çelik, 12.08.1984; Anna Becker, früher Schmidt);
`aufnahme.mjs` gibt die Treffer aus – „KEINE TREFFER“ heißt: Beispiel anpassen oder Suche prüfen.
