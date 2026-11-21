# VibeVHS – Buchungs-Shop-Plugin

Stand: 17.11.2026

VibeVHS bekommt eine öffentliche Buchungs-API; darauf setzen eine TYPO3-Extension für vhs-egs.de und eine Web-Komponente für jede andere Website auf. VibeVHS bleibt die einzige Datenquelle.

## Ausgangslage

vhs-egs.de läuft mit TYPO3 (Generator-Tag „TYPO3 CMS“, Autor im Quelltext: Kubus Software GmbH); der Kursteil ist sehr wahrscheinlich KuferWEB. Geprüft am 15.11.2026 über die öffentliche Seite, die TYPO3-Version ist unbekannt.

| Bereich | Heutige URL | Hinweis |
| --- | --- | --- |
| Kursliste und Kategorien | `/kurse`, `/kurse/kategorie/…` | Kategorien entsprechen Fachbereichen |
| Kurssuche | `/kurssuche` |  |
| Warenkorb und Kasse | `/warenkorb/kasse/bereich/4` |  |
| Login, Registrierung, Passwort | `/teilnehmer-login/…` | Konto der Teilnehmenden |
| Kufer-Werkzeuge | `/fileadmin/kuferweb/kufertools` | verlinkt als „Dozenten“ |

KuferWEB synchronisiert laut [Agenturbeschreibung](https://www.gosign.de/en/typo3/kuferweb/) Kursdaten zeitgesteuert in TYPO3-Tabellen, fragt freie Plätze kurz gecacht live ab und schickt Buchungen über eine Warteschlange an die Kufer-API. Dieses bewährte Muster übernehmen wir.

## Aufbau

Drei Bausteine, eine Datenquelle: Die Website zeigt Kurse aus einer lokalen Kopie und bucht immer live gegen VibeVHS.

1. **Buchungs-API in VibeVHS** (ASP.NET Core, OpenAPI, versioniert unter `/api/v1/shop`): Katalog, Verfügbarkeit, Warenkorb, Buchung, Konto. Nutzt dieselbe Buchungsprüfung mit Begründungscodes wie die Verwaltung.
2. **TYPO3-Extension `vibevhs_shop`** (Composer-Paket, Extbase/Fluid): Ein Scheduler-Task holt Änderungen per Delta-Abgleich (`?geaendertSeit=`) in eigene Tabellen; Kursliste, Suche und Detailseiten werden serverseitig gerendert (Suchmaschinen, schnelle Ladezeit). Freie Plätze, Warenkorb, Kasse und Konto laufen live über die API.
3. **Web-Komponente `<vhs-kurse>`** (ein Script-Tag, ohne Framework-Abhängigkeit): für Websites ohne TYPO3 oder für Kooperationspartner, z. B. Stadt oder Familienbüro.

Die Buchung folgt in allen Varianten demselben Ablauf: Warenkorb anlegen → Plätze für 15 Minuten reservieren → Daten und Einwilligungen erfassen → verbindlich buchen → Bestätigung mit Übersetzungslink und persönlichem Check-in-Link aus VibeVHS.

## Öffentliche Buchungs-API

Nur öffentliche Kurse und nur Rollen, die die Buchungsrichtlinie des Kurses zulässt (FA zu Buchungsrechten).

| Endpunkt | Zweck | Zugriff |
| --- | --- | --- |
| `GET /kurse?geaendertSeit=&fachbereich=&q=` | Katalog, Delta-Abgleich | öffentlich, ETag |
| `GET /kurse/{nummer}` | Details, Termine, Orte mit Wegbeschreibung, Merkmale (barrierefrei) | öffentlich |
| `GET /kurse/{nummer}/verfuegbarkeit` | freie Plätze, Warteliste, Buchbarkeit mit Grund | öffentlich, 30 s Cache |
| `POST /warenkorb`, `PUT /warenkorb/{id}/positionen` | Warenkorb, Reservierung 15 min | Gast oder Konto |
| `POST /warenkorb/{id}/buchen` | verbindliche Buchung, Header `Idempotency-Key` | Gast oder Konto |
| `GET /konto/buchungen`, `POST /konto/buchungen/{id}/storno` | eigene Buchungen, Storno nach AGB | Konto (OIDC) |
| `POST /konto/registrierung` | Selbstregistrierung mit Double-Opt-in | öffentlich, Rate-Limit |

Antworten liefern bei Ablehnung den Begründungscode und einen erklärenden Text („Kurs voll – Platz auf der Warteliste möglich“), damit die Website dieselben Erklärungen zeigt wie die Verwaltung. Zahlungsarten: SEPA-Mandat online, Rechnung; Online-Bezahldienste später als Adapter.

## TYPO3-Extension und Web-Komponente

**TYPO3-Extension `vibevhs_shop`**

- Plugins für Redakteure: Kursliste (Filter Fachbereich, Ort, Wochentag, Zielgruppe), Kurssuche, Kursdetail, Warenkorb, Kasse, „Neue Kurse“-Teaser. „Mein Konto“ ist nur ein Link ins Teilnehmendenportal von VibeVHS (gemeinsame Anmeldung über OIDC).
- Routing übernimmt die bisherigen URL-Muster (`/kurse/{kategorie}`, `/kurse/{kursnummer}-{titel}`), alte KuferWEB-Adressen leiten per 301 weiter.
- Darstellung über Fluid-Templates, die Kubus an das bestehende Layout anpassen kann; strukturierte Daten nach schema.org/Course für Suchmaschinen.
- Konfiguration: API-Adresse, Mandanten-Schlüssel, Abgleichsintervall (Standard 15 min), Cache-Zeiten. Zielversion TYPO3 13 LTS, zu prüfen mit Kubus.

**Web-Komponente `<vhs-kurse>`**

- Einbindung: `<script type="module" src="https://…/vibevhs/shop.js"></script>` und `<vhs-kurse fachbereich="4" ort="greven"></vhs-kurse>`.
- Shadow DOM, Farben über CSS-Variablen, lädt nur von der VibeVHS-Adresse; Kasse optional als Weiterleitung auf eine VibeVHS-Seite, damit die Partnerseite keine Personendaten berührt.

## Abgrenzung zum Teilnehmendenportal

Das Portal wird getrennt vom Shop gebaut und von VibeVHS selbst ausgeliefert; Shop und Portal teilen API und Anmeldung. Der Shop dient dem Finden und Buchen auf jeder Website, das Portal allem nach der Buchung.

| Aufgabe | Shop (Website) | Portal (VibeVHS) |
| --- | --- | --- |
| Kurse finden, buchen, Warteliste | ja | Link zum Shop |
| Buchungen ansehen, stornieren, umbuchen | – | ja |
| Termine in den Kalender (ICS-Abo mit persönlichem Token, Einzeltermin-Download) | – | ja |
| Unterlagen der Kursleitung, Bescheinigungen, Rechnungen | – | ja |
| Check-in-Link, Ausweis-QR, Daten und Einwilligungen pflegen, Auskunft | – | ja |

Gründe: Personendaten und Dokumente gehören nicht in die TYPO3-Datenbank; das Portal funktioniert gleich für jede Website und Partnerseite; Dozierendenportal und Teilnehmendenportal teilen sich eine Basis; Änderungen am Portal brauchen kein TYPO3-Update.

## Sicherheit, Datenschutz, Barrierefreiheit

- **Datensparsamkeit:** Die TYPO3-Datenbank enthält nur öffentliche Kursdaten, nie Personendaten. Personendaten fließen direkt von Browser bzw. Kasse an VibeVHS.
- **Anmeldung:** Konto über OIDC (Authorization Code mit PKCE), Gastbuchung ohne Konto möglich.
- **Missbrauchsschutz:** Rate-Limits je IP und Warenkorb, Reservierungen verfallen nach 15 Minuten, Idempotenz-Schlüssel gegen Doppelbuchung, Bot-Schutz ohne Drittanbieter (z. B. Rechenaufgabe im Browser statt reCAPTCHA).
- **Einwilligungen** (Newsletter, Foto, AGB, Datenschutz) werden mit Version und Zeitpunkt in VibeVHS gespeichert.
- **Barrierefreiheit:** BITV 2.0 / WCAG 2.2 AA für Extension und Komponente; Mehrsprachigkeit der Oberfläche über den Übersetzungsadapter.

## Umstieg, Einplanung, offene Fragen

Geplant als Iteration I9 „Webshop“ nach I8, weil die API Finanzen (I6) und Integrationskurse (I7) voraussetzt. Umstieg in drei Schritten: Extension parallel auf einer Testseite, dann Kursliste und Detail live bei weiterlaufender Kufer-Kasse, zuletzt Kasse und Konto.

- [ ] TYPO3-Version und Hosting von vhs-egs.de bei Kubus erfragen
- [ ] Klären, ob Kubus die Templates anpasst oder wir liefern
- [ ] Bestehende Teilnehmendenkonten aus KuferWEB übernehmen oder neu registrieren lassen
- [ ] Online-Bezahlung (z. B. ePayBL der Kommunen) gewünscht?

## Quellen

- [vhs-egs.de, Team-Seite (Quelltext-Merkmale)](https://www.vhs-egs.de/ueber-uns/team-neu)
- [Gosign: KuferWEB in TYPO3](https://www.gosign.de/en/typo3/kuferweb/)
