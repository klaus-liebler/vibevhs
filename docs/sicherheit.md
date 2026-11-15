# Sicherheit: OWASP-Checkliste (Stand I8)

Orientierung: OWASP Top 10 (2021) und ASVS 4.0 Level 2. ✅ umgesetzt · ⚠️ teilweise/offen · ⏳ mit der kommunalen Anmeldung.

| Thema | Stand | Umsetzung / offener Punkt |
|---|---|---|
| A01 Zugriffskontrolle | ✅ | Rollen je Aktion im Anwendungsdienst geprüft (nicht nur in der Oberfläche); gesperrte Knöpfe erklären den Grund; Fachbereichsleitungen sehen nur eigene Fachbereiche; Rolle „Prüfung“ nur lesend; Endpunkte mit `RequireAuthorization` |
| A02 Kryptografie | ✅/⚠️ | Check-in-Tokens nur als SHA-256 gespeichert; Prüfsummen für Belege, Dokumente, Sicherungen; HSTS außerhalb der Entwicklung. ⚠️ Datenbank-Verschlüsselung at rest: über Laufwerksverschlüsselung des Servers lösen |
| A03 Injection | ✅ | EF Core mit Parametern; keine zusammengesetzten SQL-Texte mit Benutzereingaben; CSV-Import ohne Formelauswertung; XML ohne DTD-Verarbeitung (camt) |
| A04 Unsicheres Design | ✅ | Vier-Augen-Prinzip (Löschlauf), Freigaben für kritische Automatismen, Hash-Kette, Storno statt Löschen |
| A05 Fehlkonfiguration | ✅/⚠️ | Sicherheitsheader (CSP, X-Frame-Options DENY, nosniff, Referrer-Policy, Permissions-Policy, COOP); `Cache-Control: no-store` für Personen-, Finanz- und Auswertungsseiten. ⚠️ CSP noch mit `'unsafe-inline'` für Inline-Styles (Radzen) und zwei Inline-Handler (Rollenwechsel) – Ziel: Nonce/Hash bzw. Handler ins Skript verlagern |
| A06 Veraltete Komponenten | ⚠️ | Zentrale Paketversionen; `dotnet list package --vulnerable` vor jeder Auslieferung (noch kein CI) |
| A07 Authentifizierung | ⏳ | Testanmeldung ohne Passwort (nur Entwicklung, Zugangsschutz am Webserver vorgesehen); echte Anmeldung per OIDC/SAML der Kommune folgt; Rate-Limit auf `/anmelden` |
| A08 Integrität | ✅ | Hash-Kette Finanzprotokoll, Prüfsummen bei Nachdruck und Sicherung, Plugins nur aus lokalem Ordner (Administrationsrecht nötig) |
| A09 Protokollierung | ✅ | Revisionssicheres Protokoll (Pseudonyme), Serilog-Dateilogs, Exporte und Auskünfte protokolliert |
| A10 SSRF | ✅ | Keine serverseitigen Abrufe von Adressen aus Benutzereingaben |
| Rate-Limits | ✅ | Öffentliche Seiten (`/anmelden`, `/checkin`, `/brief`, `/uebersetzen`): `Sicherheit:RateLimitProMinute` (Standard 60) je Adresse und Bereich, Antwort 429 mit Retry-After |
| Datenschutz | ✅ | Pseudonyme im Protokoll, Lichtbild nur als data:-URI, Löschläufe, Auskunft, Anonymisierung |
| Uploads | ✅ | Größenlimits (CSV 10 MB, camt 5 MB, Lichtbild 400 KB), Typprüfung, keine Ablage im Webverzeichnis |

Nächste Schritte: CSP ohne `'unsafe-inline'`, Abhängigkeitsprüfung in einer CI-Stufe, Penetrationstest vor dem Echtbetrieb,
echte Anmeldung (OIDC) mit Sitzungs-Timeout.
