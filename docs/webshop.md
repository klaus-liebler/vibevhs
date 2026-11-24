# Webshop (I9): Buchungs-API, Web-Komponente, TYPO3

Drei Bausteine, eine Buchungslogik: Gebucht wird immer über `AnmeldungDienst` mit dem Buchungsweg **Online** – dieselbe
Buchungsprüfung (Status, Sichtbarkeit, Buchungsrichtlinie, Kapazität, Warteliste, Doppelbuchung) wie am Tresen.

```
Website der VHS (TYPO3)                     VibeVHS
 ├ Kursliste/Detail (serverseitig, SEO)  ◄─ Delta-Abgleich (Scheduler)  ── GET /api/v1/shop/kurse?geaendertSeit=…
 └ <vhs-kurse> (Browser)  ────────────────── CORS ───────────────────── Verfügbarkeit, Warenkorb, Buchen, Konto
```

## 1. Buchungs-API `/api/v1/shop`

Vertrag: `GET /api/v1/shop/openapi.json` (OpenAPI 3.1, von Hand gepflegt in `src/VibeVHS.Web/Shop/openapi-v1.json`;
ein Test prüft, dass jede Route darin beschrieben ist). Versioniert über den Pfad; neue Felder kommen ohne Versionswechsel hinzu.

| Aufruf | Zweck |
|---|---|
| `GET /kurse?geaendertSeit=&fachbereich=&q=&ort=` | öffentliche Kurse; mit `geaendertSeit` nur Änderungen plus `entfernt` (nicht mehr öffentlich/beendet). Starker **ETag**, `If-None-Match` → 304 |
| `GET /kurse/{nummer}` | Detail: Termine, Orte mit Adresse, **Wegbeschreibung**, Merkmale (nur Gruppen Zugänglichkeit/Ausstattung/Organisation), `barrierefrei` |
| `GET /kurse/{nummer}/verfuegbarkeit` | Plätze, belegt, **reserviert**, frei, Warteliste, `buchbar` mit `code` (Buchungsprüfung, kebab-case) und Text; 30 s zwischengespeichert |
| `POST /warenkorb`, `PUT /warenkorb/{id}/positionen` | Plätze **15 Minuten** reservieren (gleitend); ausgebucht mit Warteliste → Position „Warteliste“ |
| `POST /warenkorb/{id}/buchen` | Pflicht-Header `Idempotency-Key`; Gast (`teilnehmer`) oder Konto (Bearer); Einwilligungen mit Version; Lastschrift (SEPA-Mandat) oder Rechnung |
| `POST /konto/registrierung` | Double-Opt-in, immer 202 (keine Auskunft über bestehende Konten), Rate-Limit |
| `POST /konto/anmelden` | **Test-Identitätsdienst** (nur `Shop:MockIdp`), Token 1 h |
| `GET /konto/buchungen`, `POST /konto/buchungen/{id}/storno` | eigene Buchungen, Storno nach den AGB-Fristen der Richtlinie |

**Reservierung:** Offene Warenkörbe zählen gegen die Kapazität – auch am Tresen (`IPlatzreservierungen` in
`AnmeldungDienst`). Der `ShopJob` (jede Minute) gibt abgelaufene Reservierungen frei und protokolliert das,
löscht unbestätigte Registrierungen nach `Shop:DoiStunden` (48) und aktualisiert den Veröffentlichungsstand.
Reservieren und Buchen laufen nacheinander (eine Sperre im Prozess, eine Instanz je Datenbank) – parallele Anfragen überbuchen nicht.

**Buchen** ist alles-oder-nichts (Datenbanktransaktion). Gleicher Schlüssel + gleiche Anfrage → dieselbe Antwort (200,
`wiederholt: true`, ohne neuen Arbeitsnachweis); gleicher Schlüssel + andere Anfrage → 422 `idempotenz-konflikt`.
Gäste werden als neue Person angelegt (Rolle Teilnehmende, Adresse); Dubletten findet die Dublettenprüfung der Verwaltung.
Gespeichert werden die Einwilligungen `agb`, `datenschutz`, `sepa`, `werbung` mit Version und Zeitpunkt (Tabelle
`Shopbuchungen`); `werbung` setzt die E-Mail-Werbeeinwilligung der Person. Bestätigungsmails gehen wie immer mit
Übersetzungslink in den Postausgang.

**Bot-Schutz ohne Drittanbieter:** Proof-of-Work. `GET /pow` liefert eine signierte, 5 Minuten gültige Aufgabe; gesucht ist
`n` mit SHA-256(`aufgabe:n`) beginnend mit `Shop:PowBits` Null-Bits (Standard 16 ≈ 65 000 Versuche, im Browser unter einer
Sekunde). Header `X-VHS-PoW: aufgabe:n`, jede Aufgabe gilt einmal. Pflicht bei Warenkorb anlegen, Buchen, Registrieren.

**Rate-Limits je IP:** Lesen 300/min, Schreiben 30/min, Registrierung 5/h (`Shop:RateLimit:*`), Antwort 429 mit `Retry-After`.

**CORS** nur für `Shop:ErlaubteOrigins` (z. B. `https://www.vhs-egs.de`). Erlaubt: GET/POST/PUT, Header `Content-Type`,
`Idempotency-Key`, `X-VHS-PoW`, `Authorization`, `If-None-Match`; sichtbar: `ETag`, `Location`, `Retry-After`.

**Kundenkonto:** vorgesehen ist der Identitätsdienst der Kommune (OIDC, Bearer-Token). Bis dahin gibt der
Test-Identitätsdienst nach Angabe der E-Mail eines **bestätigten** Kontos ein signiertes Token aus – nur mit
`Shop:MockIdp=true` (in der Entwicklung Standard, sonst aus). Der Bestätigungslink führt auf eine Seite mit Knopf
(`/shop/konto/bestaetigen`), damit Mail-Scanner, die Links öffnen, nichts bestätigen.

Einstellungen (`appsettings.json`, Abschnitt `Shop`): `ErlaubteOrigins`, `PowBits`, `MockIdp`, `ReservierungMinuten`,
`AgbVersion`/`AgbUrl`, `DatenschutzVersion`/`DatenschutzUrl`, `MandatVersion`, `DoiStunden`, `BarrierefreiMerkmal`,
`OeffentlicheMerkmalgruppen`, `VerfuegbarkeitSekunden`, `RateLimit:LesenProMinute|SchreibenProMinute|RegistrierungProStunde`.
Neue AGB-Fassung: `AgbVersion` erhöhen – ältere Kassen-Seiten bekommen 422 `einwilligung-veraltet` und laden neu.

## 2. Web-Komponente `<vhs-kurse>`

Ein ES-Modul ohne Abhängigkeiten: `/shop/shop.js`. Demo: `/shop/demo.html`, Kassenseite `/shop/kasse.html`,
Kundenkonto `/shop/konto.html`.

```html
<script type="module" src="https://vhs.example/shop/shop.js"></script>
<vhs-kurse fachbereich="5" ort="Greven" suche="Englisch" kasse="extern"></vhs-kurse>
```

Attribute: `fachbereich`, `ort`, `suche` (Vorfilter), `api` (Standard: `/api/v1/shop` am Server des Skripts),
`kasse="extern"` (Kasse auf der VibeVHS-Kassenseite), `ansicht="kasse|konto|buchen"`, `kurs` (für `buchen`).
Gestaltung über CSS-Variablen: `--vhs-farbe`, `--vhs-farbe-text`, `--vhs-farbe-hintergrund`, `--vhs-farbe-rand`,
`--vhs-farbe-hinweis`, `--vhs-schrift`, `--vhs-radius`, `--vhs-abstand`, `--vhs-fokus`.

Barrierefreiheit (Ziel BITV 2.0 / WCAG 2.2 AA): Shadow DOM mit echten Überschriften, Tabellen mit `caption`,
Formular-Labels und `autocomplete`, Fehlerliste mit Sprungmarken (erhält den Fokus), `aria-invalid` und Fehlertext je Feld,
Live-Region für Statusmeldungen, Fokus beim Ansichtswechsel auf die Überschrift (nicht beim ersten Laden),
nicht buchbare Knöpfe mit `aria-disabled` und sichtbarer Begründung, Status nie nur über Farbe, Zielgrößen 44 px,
Reflow bis 320 px, `prefers-reduced-motion`, `forced-colors`. Der Arbeitsnachweis rechnet mit eigenem SHA-256
(funktioniert auch ohne HTTPS) und gibt den Browser regelmäßig frei. Ein Idempotenzschlüssel je Warenkorb
(sessionStorage) verhindert Doppelbuchungen bei Doppelklick oder Netzabbruch.

## 3. TYPO3-Extension `vibevhs_shop`

Ort: `integrations/typo3/vibevhs_shop` – Composer-Paket `vibevhs/vibevhs-shop`, TYPO3 13 LTS, Extbase/Fluid.
Details und Einrichtung: [README der Extension](../integrations/typo3/vibevhs_shop/README.md).
Kurz: Scheduler-Task (Konsolenbefehl `vibevhs:abgleich`) gleicht per Delta in eigene Tabellen ab; Plugins Kursliste/Suche
und Detail rendern serverseitig mit schema.org/Course; Buchungsbox, Warenkorb und Kasse kommen über die Web-Komponente
direkt von VibeVHS; „Mein Konto“ ist nur ein Link. In TYPO3 liegen keine Personendaten.

## Tests

- `tests/VibeVHS.Web.Tests` (xUnit, Kestrel im Testprozess, echte HTTP-Aufrufe, steuerbare Uhr): Reservierung und Verfall,
  Idempotenz, 20 parallele Warenkörbe auf 3 Plätze, Richtlinie/Sichtbarkeit/Status, Delta und ETag, Double-Opt-in,
  Rate-Limit, Konto-Buchung und Storno, Löschen unbestätigter Konten, CORS, Arbeitsnachweis, OpenAPI-Abdeckung.
- `tests/Shop.Komponente/komponententest.mjs`: Headless-Edge gegen `/shop/demo.html` (36 Prüfungen: Tastatur, Fokus,
  Fehlerliste, Beschriftungen, Kontrast, Zielgrößen, Kasse auf der Kassenseite, Konto, Reflow 320 px).
  `node tests/Shop.Komponente/komponententest.mjs http://localhost:5199`
- TYPO3: PHPUnit-Tests unter `integrations/typo3/vibevhs_shop/Tests` – **auf diesem PC nicht ausgeführt** (kein PHP/Composer).
