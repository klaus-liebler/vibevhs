# VibeVHS – Sicherheit und Betrieb

> **Für Technik, Datenschutz und IT-Betrieb.** Wer sich nur für die Funktionen interessiert, springt direkt zu den
> [Konzepten](README.md#konzepte). Die OWASP-Einzelheiten stehen in [sicherheit.md](sicherheit.md), der Notfallablauf in
> [betrieb.md](betrieb.md), die Servereinrichtung in [bereitstellung.md](bereitstellung.md).

**Diese Seite zeigt den Ist-Stand.** Wie VibeVHS sicher betrieben werden soll, beschreibt das [Zielbild](zielbild-sicherheit-und-betrieb.md).

Stand: 26.11.2026, abgeglichen mit Code und Konfiguration im Repository (nicht mit einem laufenden Server).
✅ umgesetzt · ⚠️ teilweise · ❌ nicht umgesetzt · 🔜 geplant

Wichtig zur Einordnung: Das [Sicherheitskonzept in architektur.md](spezifikation/architektur.md#sicherheits--und-datenschutzkonzept)
beschreibt den **Zielzustand** (OIDC der Kommune, Feldverschlüsselung, Docker Secrets). Diese Seite beschreibt, was davon heute
im Code steckt.

## Auf einen Blick

| Ebene | Stand in einem Satz |
|---|---|
| Netz | Testsystem `https://liebler.iui.hs-osnabrueck.de/vibevhs/` hinter nginx-Basic-Auth; SSH nur aus dem VPN, Node-RED nur lokal. |
| Webserver (nginx) | TLS 1.2/1.3, HTTPS-Umleitung, `server_tokens off`, Basic Auth; HSTS in nginx noch aus, die Anwendung setzt es. |
| Anwendung | Rechte im Dienst, EF Core, strenge CSP, Rate-Limits auf öffentlichen Seiten und der Shop-API; Kestrel nur auf `127.0.0.1:5080`. |
| Anmeldung | **Mock-Anmeldung** (Konto und Rolle wählen) hinter dem Zugangspasswort; OIDC/SAML der Kommune geplant. |
| Daten | SQLite; **keine Feldverschlüsselung** (Konzept sieht sie für IBAN und Aufenthaltsdaten vor). |
| Betrieb | systemd mit Härtung, Runtime-Updates per Timer, Zertifikatsablauf-Wächter, Sicherung und Litestream-Vorlage; Logs nur lokal. |

## 1. Netz und Erreichbarkeit

| Punkt | Stand | Umsetzung / offen |
|---|---|---|
| Nur intern / VPN | ⚠️ | Kein VPN, aber alles hinter Basic Auth (`deploy/server/nginx/liebler.d/vibevhs.conf`). Ohne Passwort: statische Dateien, Kalender-Abos, Check-in/Kurs-QR (Token im Link). Shop-API und Portale sind vorbereitet, aber auskommentiert. |
| Host-Firewall | ✅ | `deploy/server/schritte/07-firewall.sh`: 80/443, SSH nur aus dem VPN, Node-RED 1880 zu. Hinweis im Skript: Docker (snap) veröffentlicht 9443/8000 an ufw vorbei. |
| SSH-Härtung | ✅ | `deploy/server/ssh/10-haertung.conf`: nur Schlüssel, kein root, nur `srvadmin`. |
| Kestrel nur lokal | ✅ | `127.0.0.1:5080`. |
| Datenbank nicht im Netz | ✅ | SQLite-Datei in `/var/lib/vibevhs`. |

## 2. Webserver (nginx)

| Punkt | Stand | Umsetzung / offen |
|---|---|---|
| TLS 1.2/1.3, HTTP → HTTPS | ✅ | `deploy/server/nginx/liebler.conf`; Zertifikatsablauf-Wächter (`zertifikat-ablauf.timer`). |
| HSTS | ⚠️ | In `liebler.conf` auskommentiert („erst einschalten, wenn alles über HTTPS läuft“); die Anwendung setzt HSTS. |
| CSP, X-Frame-Options, nosniff, Referrer-, Permissions-Policy | ✅ | In der Anwendung (`Sicherheitsheader.cs`). |
| `server_tokens off` | ✅ | `liebler.conf`. |
| Rate-Limit | ⚠️ | In der Anwendung (`Sicherheit:RateLimitProMinute`, Shop-API zusätzlich Proof-of-Work). Kein `limit_req` in nginx. |
| IP-Allowlist | ❌ | Nicht gesetzt. |

## 3. Anwendung

| Punkt | Stand | Umsetzung / offen |
|---|---|---|
| Rechte im Dienst | ✅ | Rollen je Aktion, Fachbereichsrechte, Dozierende nur im Portal. |
| SQL-Injection, CSRF | ✅ | EF Core; Antiforgery; XML ohne DTD, CSV ohne Formeln. |
| Uploads | ✅ | Größen-, Endungs- und Signaturprüfung; Virenprüfung nur als Mock. |
| Integrität | ✅ | Hash-Kette im Finanzprotokoll (GoBD), Prüfsummen für Belege und Sicherungen. |

## 4. Anmeldung

| Verfahren | Stand | Umsetzung / offen |
|---|---|---|
| Verwaltung | ⚠️ | Mock (`MockAnmeldung.cs`) – nur hinter dem Zugangspasswort zulässig. |
| Dozierende / Kursleitungen | ✅ | Anmeldelink per Mail (15 Min., einmalig, Bestätigung per POST). |
| Kundinnen und Kunden (Shop) | ⚠️ | Test-Identitätsdienst (`Shop:MockIdp`, außerhalb der Entwicklung aus). |
| OIDC/SAML der Kommune mit 2FA | 🔜 | Konzept in `architektur.md`. |
| Mehrere Verfahren je Instanz | 🔜 | Gemeinsamer Baustein für alle vier Systeme in Planung. |

## 5. Daten und Verschlüsselung

| Punkt | Stand | Umsetzung / offen |
|---|---|---|
| Feldverschlüsselung IBAN/Aufenthalt | ❌ | Im Konzept „ASP.NET Data Protection“. Im Code wird Data Protection nur für die Cookie-Schlüssel genutzt (`Program.cs`); die Spalten liegen im Klartext. |
| Laufwerks- und Sicherungsverschlüsselung | ❌ | Laut `sicherheit.md` über die Laufwerksverschlüsselung des Servers zu lösen; nicht im Kit. |
| Datenschutzfunktionen | ✅ | Pseudonyme im Protokoll, Löschläufe mit Vier-Augen-Prinzip, Auskunft, Anonymisierung. |

## 6. Betrieb

| Punkt | Stand | Umsetzung / offen |
|---|---|---|
| Dienst | ✅ | `deploy/server/systemd/vibevhs.service` mit Härtung. |
| Ausrollen | ✅ | `deploy/server/deploy.ps1`, Schritte 01–09 je mit Rückweg. **Kein Docker** (die Anwendung hat aber einen Container-Healthcheck `--gesundheit`). |
| Datenbankschema | ⚠️ | `EnsureCreated` mit Prüfwert, **keine EF-Migrationen**. |
| Sicherung | ✅/⚠️ | Tägliche Sicherung mit Wiederherstellungstest; Litestream-Vorlage (`litestream.yml`) vorbereitet, nicht aktiv. |
| Patch-Management | ✅/⚠️ | `dotnet-runtime-aktualisieren.timer`; NuGet-Prüfung nur von Hand, kein CI. |
| Logs und Monitoring | ⚠️ | Serilog-Dateien und journald, `/gesund` (prüft die Datenbank). Kein zentraler Log-Server, keine Metriken. |

## Nächste Schritte

Echte Anmeldung (OIDC) mit 2FA, Feldverschlüsselung wie im Konzept, EF-Migrationen, Log-Versand und Metriken,
HSTS und `limit_req` in nginx, Abhängigkeitsprüfung in CI, Penetrationstest.
