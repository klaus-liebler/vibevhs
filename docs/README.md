# VibeVHS

Webbasierte Verwaltungssoftware für Volkshochschulen: einfach zu bedienen für Mitarbeitende, Dozierende und
Teilnehmende, eingebunden in die kommunale Infrastruktur, DSGVO-konform, mit Schnittstellen zu Abrechnungsstellen
(z. B. BAMF-Integrationskurse) und automatisierten, immer protokollierten Routineabläufen.

## Konzepte

| Dokument | Link |
|---|---|
| Anforderungen | https://claude.ai/artifact/5zHT9KccZeiUj1xS1BmKNG |
| Architektur und Datenmodell | https://claude.ai/artifact/XVL9kYS5XcAYwGsoDQ4CN3 |
| Umsetzungsfahrplan | https://claude.ai/artifact/FFRgE1BfKbphyCYxcVz4S9 |
| Klick-Prototyp (online) | https://claude.ai/artifact/KR88btCTssLuRhCTYFGnbx |
| Klick-Prototyp (lokal) | [`prototyp/prototyp-verwaltung.html`](../prototyp/prototyp-verwaltung.html) |

## Voraussetzungen

- .NET 11 SDK (`winget install --id Microsoft.DotNet.SDK.11 -e`)
- Git

Docker wird erst für die Bereitstellung benötigt (siehe `deploy/`).

## Starten

```powershell
cd C:\repos\vibevhs
dotnet run --project src\VibeVHS.Web --launch-profile http
```

Dann im Browser <http://localhost:5189> öffnen und bei der **Testanmeldung** Konto und Rolle wählen
(Teilnehmerservice, Fachbereichsleitung, Buchhaltung, Integrationskurs-Team).

Beim ersten Start wird die SQLite-Datenbank `src/VibeVHS.Web/data/vibevhs.db` angelegt (WAL-Modus, Migrationen
werden automatisch eingespielt). In der Entwicklungsumgebung kommen fünf Beispielpersonen dazu, darunter
Anna Becker mit Namenshistorie (Tippfehler korrigiert, später Heirat).

Mehr Testdaten (reproduzierbar, fester Startwert):

```powershell
dotnet run --project tools\Testdaten -- 500
```

Zurücksetzen: Anwendung beenden und den Ordner `src/VibeVHS.Web/data` löschen.

## Bauen und testen

```powershell
dotnet build VibeVHS.slnx
dotnet test --solution VibeVHS.slnx
```

Tests laufen mit xUnit v3 auf Microsoft.Testing.Platform (eingestellt in `global.json`).

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/VibeVHS.Domain` | Fachkern ohne Abhängigkeiten: bitemporale Versionen (`BitemporaleVersion`, `ZeitlicheHistorie.AlsStichtag`), `Geld` in Cent, GUID v7, `Person` mit versioniertem Namen |
| `src/VibeVHS.Application` | Anwendungsfälle und Ports: Rollen, `Freigabe` (gesperrt immer mit Begründung), `NaechsterSchritt`, Postausgang (Outbox) mit Übersetzungslink, Audit-Protokoll, Personendienst |
| `src/VibeVHS.Infrastructure` | EF Core + SQLite (WAL), Migrationen, Speicher, Quartz-Job für die Zustellung |
| `src/VibeVHS.Adapters.Mocks` | Mock-Anmeldung (Konto + Rolle), simulierte Mail- und Schnittstellenkanäle |
| `src/VibeVHS.Web` | Blazor Web App (Interactive Server, Radzen), deutsche Oberfläche |
| `tests/*` | xUnit v3 + Shouldly |
| `tools/Testdaten` | Testdaten-Generator (Bogus, fester Startwert) |
| `deploy/` | Docker (folgt) |

## Leitlinien in der Oberfläche

- **Gesperrt heißt erklärt:** Nicht klickbare Schaltflächen (`BegruendeterButton`) und nicht editierbare Felder
  (`GesperrtesFeld`) zeigen bei Maus und Tastaturfokus einen Tooltip mit dem Grund.
- **Nächste sinnvolle Schritte:** Nach jeder Aktion bietet der Baustein `NaechsteSchritte` die naheliegenden Folgeschritte an.
- **Nichts geht unbemerkt raus:** Jede Mail und jeder Schnittstellenaufruf läuft über den Postausgang (Outbox),
  wird gemeinsam mit der fachlichen Änderung gespeichert, per Hintergrundjob zugestellt und protokolliert.
- **Übersetzungslink:** Jede Kundenmail enthält „Diese Nachricht in Ihrer Sprache lesen“ in der Wunschsprache.
- **Nichts wird überschrieben:** Zeitlich veränderliche Daten sind bitemporal (gültig von/bis, erfasst/ersetzt).
  Korrektur (rückwirkend) und Änderung ab Datum sind getrennte Vorgänge.

## Bibliotheken

Erlaubt: Radzen.Blazor, EF Core + SQLite, Serilog, Quartz.NET, xUnit, Shouldly, Bogus, Verify.
Nicht verwenden: QuestPDF, Nager.Date, MediatR, AutoMapper, FluentAssertions.
Versionen zentral in `Directory.Packages.props`.

## Nachgetragene Anforderungen

- **Allgemeine Suche umfasst auch Orte** (zusätzlich zu Personen, Kursen und Kursnummern). Die Suche folgt in einer
  späteren Iteration; der Platzhalter in der Kopfzeile nennt Orte bereits.

## Offene Punkte nach Iteration 0

- Docker-Image und `docker-compose.yml` mit Mailpit (bewusst zurückgestellt, zuerst nativer Betrieb)
- Echte Anmeldung über die kommunale Benutzerverwaltung (OIDC/SAML) statt Mock-Anmeldung
- Übersetzungsseite `/uebersetzen/{id}` ist ein Platzhalter; Übersetzungsdienst noch nicht angebunden
- Briefe mit QR-Code „Translate this letter in your language“ (Dokumenterzeugung folgt)
- Stammdaten bearbeiten (außer Namen), Rollen von Personen, Kurse, Anmeldung per QR-Code
