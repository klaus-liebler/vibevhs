# Schulungsbetrieb: Instanzen mit Spielzeit

Stand: 24.11.2026 · QA-68 (aus SchoolCraft FK-14 übernommen und auf die vhs angepasst)

Für Schulungen der Verwaltung bekommt jede teilnehmende Person (bis 20) eine **eigene VibeVHS-Instanz mit eigener Datenbank**. Jede Instanz
ist eine Kopie einer fertigen **Vorlage** (PostgreSQL `CREATE DATABASE … TEMPLATE`, wenige Sekunden) und hat ihre eigene **Spielzeit**: Die
Kursleitung springt zu benannten Zeitpunkten einer Woche am Tresen, lässt die Zeit laufen, hält sie an oder rafft sie. Der Schulungsbetrieb
arbeitet nur mit erfundenen Daten (Demodaten, Mock-Postausgang).

## Begriffe

| Begriff | Bedeutung |
| --- | --- |
| Instanz | eigener Prozess mit eigener Datenbank `vibevhs_iNN`, eigener Spielzeit und eigenem Port (ab 5301) |
| Spielzeit | die Uhr, nach der VibeVHS fachlich lebt: „heute“, Fristen, Startseite, zeitgesteuerte Automatik |
| Realzeit | die echte Uhr: Anmeldung (Cookies), Wartezeiten, Abruftakte, Postausgang-Takt |
| Tempo | 1 normal, 0,1 Zeitlupe, 60 Zeitraffer, 0 angehalten |
| Bezugstag | Montag der Woche, in der die Vorlagen gebaut wurden; alle Marken liegen relativ dazu |
| Sprungmarke | benannter Spielzeitpunkt mit beschriebenem Zustand; je Marke eine Vorlage `vibevhs_vorlage_<marke>` |

## Sprungmarken (Schulungswoche)

| Marke | Spielzeit | Zustand |
| --- | --- | --- |
| `ausgangslage` | Montag 7:30 | Tresen öffnet: frische Demodaten, Anmeldungen, Aufgaben in den Körben |
| `mo-1800` | Montag 18:00 | Abendkurse beginnen: Anwesenheit und Check-in |
| `di-0800` | Dienstag 8:00 | nach der ersten Nacht: Automatik gelaufen (Aufgaben, Erinnerungen, Rechnungs- und Mahnvorschläge) |
| `fr-1300` | Freitag 13:00 | Wochenende naht: Mindestteilnehmerzahlen der nächsten Woche, offene Aufgaben |
| `mo2-0730` | Montag eine Woche später, 7:30 | eine Woche Betrieb mit Automatik: Eskalationen, Fristen, Kursbeginne |

Jede spätere Marke entsteht beim Vorlagenbau aus der vorigen: Die Spielzeit läuft stundenweise vor, und jeder zeitgesteuerte Lauf
(stündlich, nachts, morgens) läuft einmal je überschrittenem Zeitpunkt – genau wie im Betrieb.

## Regeln

- **Ein Prozess, eine Datenbank, eine Spielzeit je Instanz.** Instanzen teilen nur den PostgreSQL-Container.
- **Nur gekennzeichnete Datenbanken werden gelöscht:** Instanzdatenbanken tragen den Kommentar `VibeVHS-Schulungsinstanz`; ein Sprung
  löscht nur so gekennzeichnete Datenbanken – nie eine des Echt- oder Testbetriebs.
- **Zustand nur aus Vorlagen:** Ein Sprung kopiert immer eine Vorlage (vorwärts wie rückwärts). Ohne Sprung lässt sich die Uhr nur
  vorstellen, nie zurück.
- **Vorlagen gehören zum Softwarestand:** Passt das Schema einer Vorlage nicht zur Anwendung, bricht der Start mit „Vorlagen neu bauen“ ab.
- **Fachliche Zeit ist Spielzeit:** „heute“, Fristen, Erfassungszeit, Startseite und die zeitgesteuerten Läufe (Spielzeitplaner prüft jede
  Sekunde, welche Zeitpunkte überschritten sind, und startet jeden Lauf einmal).
- **Realzeit-Ausnahmen:** Anmeldung (Cookies), Antiforgery, Postausgang- und Shop-Takt, Wartezeiten.
- **Getrennte Anmeldungen:** Der Cookie-Name trägt den Instanznamen, mehrere Instanzen lassen sich im selben Browser öffnen.
- **Kursleitung vor Teilnehmenden:** `sperren` nimmt den Teilnehmenden die Steuerung; die Seite „Spielzeit“ nennt den Grund.

## Bedienung

- **Spielzeitleiste** oben auf jeder Seite: Instanz, Spielzeit, Tempo, Link zur Seite **Spielzeit** (`/spielzeit`): Anhalten/Weiter,
  Tempo 0,1 · 1 · 10 · 60, vorstellen um 1 Stunde, 1 Tag, 1 Woche, Sprungmarken mit „Hierhin springen“ (mit Rückfrage).
- Nach einem Sprung laden geöffnete Seiten selbst neu; während des Sprungs zeigt die Instanz kurz „Zeitsprung läuft …“.
- **Werkzeug der Kursleitung** `tools/instanzen.ps1`:

```powershell
docker compose -f deploy/docker-compose.yml up -d          # PostgreSQL
.\tools\instanzen.ps1 vorlagen                               # einmal je Softwarestand (gut 1 Minute)
.\tools\instanzen.ps1 starten -Anzahl 12                     # i01 … i12 auf 5301 …; -Rechner <IP> für Zugriff im Netz
.\tools\instanzen.ps1 liste
.\tools\instanzen.ps1 springen di-0800                       # alle Instanzen (oder -Instanzen i01,i02)
.\tools\instanzen.ps1 tempo 60
.\tools\instanzen.ps1 anhalten
.\tools\instanzen.ps1 sperren                                # Teilnehmende steuern nicht mehr selbst
.\tools\instanzen.ps1 entfernen                              # Prozesse beenden, Instanzdatenbanken löschen
```

Das Werkzeug legt einen zufälligen Leitstandschlüssel im Arbeitsverzeichnis (`%TEMP%\vibevhs-schulung`) an; die Instanzen nehmen
Steuerbefehle nur mit diesem Schlüssel an (`/schulung/…`, Kopf `X-Schulung-Schluessel`). Teilnehmende im selben Netz erreichen ihre
Instanz mit `-Rechner <Name oder IP>` (Ports 5301 ff. in der Firewall freigeben).

## Technik

`Spielzeit` (Application/Betrieb) ist ein `TimeProvider`; im Schulungsbetrieb ersetzt er die Systemuhr in der Dependency Injection.
`Zeitplan` (Infrastructure/Hintergrund) ist die gemeinsame Liste der zeitgesteuerten Läufe – im Echtbetrieb als Quartz-Cron, im
Schulungsbetrieb führt sie der `Spielzeitplaner` nach Spielzeit aus. `Schulungsbetrieb` (Infrastructure/Betrieb) baut Vorlagen, bereitet
Instanzen vor und springt; `Schulungsdatenbanken` kopiert und löscht Datenbanken mit Kennzeichnung.
