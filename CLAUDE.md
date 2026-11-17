# VibeVHS – Übergabe für Claude Code

Webbasierte Verwaltung einer Volkshochschule. Pilot: **vhs Emsdetten Greven Saerbeck (vhs EGS)**. Einfach für Verwaltung,
Kursleitungen und Teilnehmende; Routinen automatisiert, aber immer protokolliert. Ansprechpartner: Klaus.

## Konzeptdokumente

- Anforderungen: https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG
- Architektur: https://claude.ai/artifact/XVL9kYS5XcAYwGsoDQ4CN3
- Fahrplan: https://claude.ai/artifact/FFRgE1BfKbphyCYxcVz4S9
- Shop-Konzept: https://claude.ai/artifact/VsQFogXRz3dzQYZajLgoMm
- Prototyp: https://claude.ai/artifact/KR88btCTssLuRhCTYFGnbx
- Im Repo: `docs/README.md` (Gesamtüberblick je Iteration), `docs/schnittstellen.md`, `docs/betrieb.md`, `docs/sicherheit.md`, `docs/webshop.md`,
  `docs/dozierendenportal.md`, `docs/abnahme-a.md` (Abnahme-Drehbuch), `deploy/server/README-server.md` (Bereitstellung)

## Stack und Struktur

.NET 11, ASP.NET Core **Blazor Server** (SSR + InteractiveServer) mit **Radzen**, **EF Core + SQLite**, Serilog, Quartz.NET.
Tests: xUnit v3 (Microsoft.Testing.Platform), Shouldly, Bogus, Verify. Weitere erlaubte Bibliotheken: ClosedXML,
PDFsharp/MigraDoc, QRCoder, html5-qrcode. **Nicht verwenden:** QuestPDF, Nager.Date, MediatR, AutoMapper, FluentAssertions
(neue Pakete nur nach Rückfrage; Microsoft.AspNetCore.OpenApi steht nicht auf der Liste).

```
src/VibeVHS.Domain          Fachmodell (bitemporal: BitemporaleVersion, ZeitlicheHistorie), keine Abhängigkeiten
src/VibeVHS.Application     Dienste/Anwendungsfälle, Ports (I…Speicher), Rechteprüfung, Protokoll
src/VibeVHS.Infrastructure  EF Core (VibeVhsDbContext, Schema per EnsureCreated – derzeit keine Migrationen), Quartz-Jobs, PDF, Startdaten
src/VibeVHS.Adapters.Mocks  Testanmeldung, Mail/Schnittstellen, Übersetzer, Virenprüfung, Geokodierer, BAMF (Mocks)
src/VibeVHS.Web             Blazor-Seiten (Components/Pages, Bausteine, Layout), Shop-API, Portale, Demodaten (Entwicklung/)
tests/…                     Domain, Application, Infrastructure (SQLite in-memory, Schema wie im Betrieb), Web (ShopTestHost), Shop.Komponente (Headless-Edge)
tools/Testdaten             Startdaten vhs EGS (testbenutzer-vhs-egs.json, logo), Beispiel-CSV für Importe
deploy/server               Bereitstellungs-Kit (nur Branch deploy-vorbereitung)
```

## Befehle

```powershell
dotnet build VibeVHS.slnx                                    # TreatWarningsAsErrors
dotnet test --project tests/VibeVHS.Web.Tests [--filter-class "*Name*"]
node tests/Shop.Komponente/komponententest.mjs http://localhost:5199
```

Läuft Klaus' Instanz (sperrt `src/VibeVHS.Web/bin`), mit eigenem Ausgabeordner bauen/testen: `--artifacts-path <temp>`.
Eigene Testinstanz: Port **5199**, eigene Datenbank (`ConnectionStrings__VibeVHS`, `Speicher__*` auf ein Temp-Verzeichnis),
`ASPNETCORE_ENVIRONMENT=Development`, `Sicherung__Aktiv=false`. Browserprüfung per Headless-Edge (CDP), bei mobilen Seiten 390 px.

## Regeln für die Zusammenarbeit

- **Nur lokal committen, nie pushen.** Commit-Trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Klaus' laufende Instanz nie beenden oder anfassen**; Tests immer auf eigener DB/Port 5199.
- Build und alle Tests grün halten; neue Funktionen mit Tests (Rechte, Grenzfälle) und, bei Oberfläche, Browserprüfung.
- Geheimnisse (Zugangscode, Passwörter, Schlüssel, Zertifikate) nie ins Repo; nur Umgebungsvariablen auf dem Zielsystem.
- Server liebler.iui.hs-osnabrueck.de: **Änderungen nur nach Klaus' Go je Schritt**; jeden ssh-Befehl vorher in
  `deploy/server/protokoll/<datum>-*.md` eintragen (Befehl wörtlich, gekürzte Ausgabe, Geheimnisse geschwärzt).
- Berichte und Oberfläche auf Deutsch.

## Konventionen

- **Deutsch** in Oberfläche und Fachcode (Klassen, Methoden, Variablen: `Belegung`, `AnmeldenAsync`, `Freigabe`).
- **Gesperrte Aktionen erklären:** `BegruendeterButton` mit `Freigabe` (Nein + Grund als Tooltip), z. B. „Bitte zuerst …“.
- **Folgeschritte anbieten:** nach jeder Aktion `NaechsteSchritte` (Hauptschritt markiert); Anker brauchen den Seitenpfad (`personen/{id}#…`).
- **Bitemporal:** Stammdaten, Rollen, Merkmale, Adressen … nie überschreiben – „Änderung ab Datum“ vs. „Korrektur“, mit Grund; Datenhistorie zeigt alles.
- **Datenbank ohne Migrationen (Entwicklungsphase, nur Testdaten):** Schema per `EnsureCreated` aus dem Modell, danach Sichten `v_…` und
  Schemaversion (`Schemainfo`, Prüfsumme). Passt eine vorhandene Datei nicht zum Modell, bricht der Start mit „Datenbankschema veraltet –
  Datei löschen oder beiseitelegen“ ab. **Vor dem ersten Produktivbetrieb werden wieder EF-Migrationen eingeführt.**
- **Normalisiert (11/2026):** keine Kopien (Namen, Anzeigetexte, Stände werden zur Laufzeit gelesen: `INamensquelle`, `Personensicht`),
  keine JSON-Spalten (Listen als eigene Tabellen, `Wertlisten`/`Listenwert<T>`), Erfassungsangaben im `Aenderungsvorgang`. Speicherformate:
  Ids 16 Byte (UUIDv7 über `Ids.Neu`), Tage JJJJMMTT, Zeitpunkte/Termine UTC-ms (Anzeige über `Zeitzone`). Namen sortieren per Collation
  `DIN5007_2` (registriert in `SqliteErweiterungen`, ebenso `suchtext()`); Suche direkt in den Versionen (kein Suchindex).
- **Stammdaten-Spalten (Grundreinigung 11/2026):** neue Personenangabe = versioniertes Merkmal (`MerkmalArt`, `AngabeAendern`), nie eine
  überschreibbare Spalte. Spalten der Stammdatentabellen stehen mit Einstufung in `Infrastructure/Datenbank/Spalteneinstufung.cs`
  (Führend / Anonymisierungsrest – Lese-Caches gibt es nicht mehr). Statistik rechnet zur Laufzeit (Geschlecht zum Kursdatum, Alter zum 31.12.).
  `ArchitekturTests` A/B/C sichern das ab.
- **Zurück zur Liste:** Listen schreiben Filter in die URL, Detail-Links tragen `?zurueck=` (`Ruecksprung`), Detailseiten `ZurueckZurListe`; zuletzt geöffnete Zeile wird markiert.
- **Datenschutz:** keine Personendaten in Logs/Protokoll (Pseudonyme `Pseudonym.Person(id)`), Exporte und Zugriffe auf Gesperrte protokollieren.
- **Mehrsprachigkeit:** jede Mail an Kundinnen/Kunden mit Übersetzungslink, jeder Brief mit QR-Code „Translate this letter in your language“ (Dokumentarchiv mit SHA-256).
- **EF Core:** begrenzte Abfragen (`Skip/Take`) und ungefilterte `First` immer eindeutig sortieren (Id als Tiebreaker);
  Warnungen sind in Entwicklung/Tests auf Throw (`Abfragewarnungen.Konfigurieren`). Enums als Text, DateTimeOffset binär (Zeitvergleiche ggf. im Speicher).
- **CSP ohne 'unsafe-inline' für Skripte:** keine Inline-Handler, Verhalten in `wwwroot/verhalten.js` (data-Attribute).
- Blazor-SSR-Formulare: `@formname`, das Formular muss beim POST gerendert werden; Seitennamen-Kollisionen mit Records → `…Seite`.
- Rechte im Dienst prüfen (nicht nur in der UI); Rollen: Leitung, Verwaltungsleitung, Fachbereichsleitung, Teilnehmerservice,
  Buchhaltung, Integrationskurs-Team, Systemverwaltung, Prüfung; Portale: Teilnehmende, Dozierende.

## Stand (16.11.2026)

Iterationen I0–I9 fertig: Personen (bitemporal, Dubletten, Vollmachten, Einwilligungen), Programm/Kurse/Orte/Raumplan, Datenschutz,
Anmeldung, Durchführung (Anwesenheit, Check-in per QR, Bescheinigungen, Briefe), Aufgaben (Körbe, Eskalation, Vertretung),
Finanzen/GoBD (Rechnungen, SEPA, Mahnwesen, Honorare, XRechnung), Integrationskurse (BAMF), Betrieb (Sicherung, Import, Auswertungen),
Webshop (API, Web-Komponente, TYPO3), Kurs-QR/Schnellanmeldung, Dozierenden- und Teilnehmendenportal.

Zuletzt (Funde aus Klaus' Test):
- Startseite: Zähler-Badges, geordneter Startabgleich, „Heute automatisch erledigt“ gruppiert
- „Zurück zur gefilterten Liste“, Kurs-QR-Bogen mit Logo, Logo und Website als Einstellung
- Benutzerverwaltung (Einstellungen → Benutzer)
- Teilnehmende nach Terminausfall informieren, Bescheinigungsdruck auf der Personenseite
- Merkmal-Werttypen (Ja/Nein, Auswahl, Ganzzahl, Text) mit Raumbedingungen „mind. n“
- Rollen an Personen hinzufügen und beenden
- „Stammdaten ändern“ (Anrede, Geburtsname, Geschlecht …)

## Offene Abklärungen

Barzahlung (FA-145), BAMF-Kostenerstattungs-/Kostenbeitragssätze, Sachkonten im GoBD-Export (Kämmerei), echter
Virenscanner und Übersetzer (heute Mocks), interne Lehrfreigaben/Eignungsprüfung der vhs (Ablauf und Nachweise).

## Laufende Arbeit: Bereitstellung

Ziel: https://liebler.iui.hs-osnabrueck.de/vibevhs (Testsystem, nginx statt Apache, Hochschulzertifikat GEANT/HARICA in
/etc/ssl/liebler für nginx und mosquitto – kein Let's Encrypt, Node-RED unter /nodered nur lokal per http). Branch **deploy-vorbereitung** (main hineingemergt): Pfad-Basis /vibevhs, `/gesund`, Testsystem-Banner,
Kit `deploy/server` (Runbook, `schritte/01–08` mit Sicherung/Prüfung/Rückweg, `werkzeuge/zertifikat-tauschen.sh`, `deploy.ps1`).
Erkundung (lesend) erledigt: `deploy/server/protokoll/2026-11-16-erkundung.md`. SSH: `ssh liebler` (Benutzer srvadmin,
Schlüssel `id_ed25519_liebler_iui_hs_osnabrueck`, sudo mit Passwort).
Nächste Schritte: Kit hochladen (`deploy.ps1 -NurKit`), dann 01 SSH härten → 02 Pakete → 03 nginx → 04 Node-RED → 05 mosquitto-Zertifikat →
06 Dienst + `deploy.ps1` → 07 Firewall → später 08 certbot aufräumen – **jeweils erst nach Klaus' Go**. Danach deploy-vorbereitung nach main mergen.
