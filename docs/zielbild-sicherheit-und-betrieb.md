# VibeVHS – Zielbild Sicherheit und Betrieb

> **Für Technik, Datenschutz und IT-Betrieb.** Dieses Kapitel beschreibt, wie VibeVHS sicher betrieben werden **soll** und womit
> es kompatibel ist – aus Sicht eines beliebigen Betreibers, nicht einer bestimmten Installation. Es ist für alle vier Systeme
> (SchoolCraft, ProfOMat, FraktionPerfekt, VibeVHS) gleich aufgebaut. **Was davon heute schon im Code steckt, steht ehrlich im
> [Ist-Stand](sicherheit-und-betrieb.md).** Das Zielbild enthält nur, was mit vertretbarem Aufwand umsetzbar ist und aktueller Praxis entspricht
> (OWASP ASVS, BSI IT-Grundschutz, Mozilla-TLS-Empfehlungen).

## Grundsätze

1. **Schutz in der Anwendung, nicht nur davor.** Sicherheitsköpfe, Ratenbegrenzung, Rechteprüfung und Verschlüsselung leistet
   VibeVHS selbst. Reverse Proxy, Firewall und VPN sind zusätzliche Schichten, keine Voraussetzung (Verteidigung in der Tiefe).
2. **Sicher voreingestellt, je Instanz anpassbar.** Alles, was ein Betreiber unterschiedlich braucht (Anmeldeverfahren,
   Netzbeschränkung, Log-Ziele), ist Konfiguration – nichts davon erfordert eine eigene Programmversion.
3. **Nichts Geheimes im Code, im Paket oder im Abbild.** Schlüssel, Passwörter und Zertifikate entstehen auf dem Server oder
   kommen aus dessen Geheimnisverwaltung.
4. **Standardschnittstellen statt Sonderwege.** SAML 2.0, OIDC, Syslog, OpenTelemetry, Prometheus, HTTP-Gesundheitsprüfung:
   VibeVHS fügt sich in vorhandene Werkzeuge ein.
5. **Jede Änderung hat einen Rückweg** – beim Ausrollen, bei Konfigurationsänderungen und bei der Datenbank.

## 1. Einbettung in die Infrastruktur

**Betriebsarten** (frei wählbar, gleiche Funktionen):

| Art | Inhalt | Auf dem Server nötig |
|---|---|---|
| Docker | App-Container; die SQLite-Datei liegt im Datenordner des Hosts; ASP.NET-Runtime im Abbild | Docker mit Compose |
| Nativ, self-contained | Programm mit eigener .NET-Runtime | Linux mit systemd |
| Nativ, framework-abhängig | nur das Programm | Linux mit systemd und ASP.NET-Core-Runtime |

Datenbank: SQLite (eine Datei im Datenordner). Für Ubuntu und Debian liefern wir ein **Ausrollskript**, das vor jeder Änderung prüft (Programme, Ports,
Platz, Konfiguration, Datenbank und Migration), bestehende Konfiguration nur ergänzt, den Start mit Countdown beobachtet und bei
Fehlern sofort zurückrollt. Andere Linux-Systeme folgen der Anleitung, Docker läuft überall.

**Reverse Proxy frei wählbar.** VibeVHS setzt voraus: TLS endet am Proxy, WebSockets werden durchgereicht (Blazor),
`X-Forwarded-*` wird gesetzt – die Anwendung glaubt diesen Köpfen nur aus konfigurierten Netzen –, der Pfad ist frei wählbar.
Damit passen nginx, Apache, Caddy, Traefik, HAProxy und Kubernetes-Ingress. **Für nginx liefern wir fertige Snippets und das
Einrichtungsskript mit**, für die anderen eine Einbindungsempfehlung.

**Netz.**

- Die Anwendung lauscht nur auf `127.0.0.1` bzw. im Container-Netz, die Datenbank ist nie aus dem Netz erreichbar.
- Empfohlene Host-Firewall (ufw, firewalld oder Netzwerk-Firewall): eingehend nur 443 (und 80 für die Umleitung), SSH nur mit
  Schlüssel und nur aus dem Verwaltungsnetz bzw. VPN.
- **Verwaltungsoberfläche auf Netze beschränkbar** (Hochschul-, Schul- oder Verwaltungsnetz, VPN) – im Proxy (`allow`/`deny`) oder
  in der Anwendung selbst, je Instanz schaltbar, Standard aus. Öffentlich bleiben nur die dafür gedachten Teile: Webshop, Portale für Teilnehmende und Kursleitungen, Kalender-Abos, Check-in per QR.

## 2. Webschutz

| Maßnahme | Zielbild |
|---|---|
| TLS | 1.2 und 1.3 nach Mozilla „intermediate“, HTTP nur zur Umleitung, HSTS |
| Sicherheitsköpfe | Von der Anwendung gesetzt: strenge CSP ohne Inline-Skript, `frame-ancestors`, `nosniff`, Referrer- und Permissions-Policy |
| Ratenbegrenzung | In der Anwendung (Anmeldung, öffentliche Seiten, Schnittstellen) plus Anmeldebremse; im Proxy zusätzlich `limit_req` empfohlen |
| Eingaben | EF Core (keine SQL-Zeichenketten), Antiforgery, Upload-Prüfung nach Größe, Endung und Signatur, CSV/XML ohne Formeln bzw. DTD |
| Proxy-Härtung (Empfehlung) | `server_tokens off`, Größenlimits, Zeitlimits |
| Prüfmaßstab | OWASP ASVS Stufe 2; Penetrationstest vor dem Echtbetrieb |

## 3. Anmeldung und Rechte

Grundlage ist **ASP.NET Core Identity** (Konten, Passwort-Hashes, Sperre nach Fehlversuchen, zweiter Faktor, Passkeys,
Sitzungsende bei Passwort- oder Rollenwechsel). Die Verfahren werden **je Instanz in der Konfiguration freigeschaltet** und
erscheinen auf der Anmeldeseite als getrennte Kacheln nebeneinander:

| Verfahren | Wofür | Umsetzung |
|---|---|---|
| Hochschul-, Firmen- oder Behördenkonto (SAML 2.0) | Shibboleth (DFN-AAI, eduGAIN), Entra ID/ADFS, Okta, BundID … | Sustainsys.Saml2 (MIT), Metadaten und Zertifikat erzeugt die Anwendung |
| Firmen- oder Behördenkonto (OIDC) | Entra ID, Keycloak, Google Workspace … | ASP.NET Core OpenID Connect |
| Konto mit Passwort und zweitem Faktor | Betreiber ohne eigenen Identitätsdienst, Notfallkonto | Identity; TOTP (Authenticator-App) Pflicht für Rollen mit Verwaltungs- oder Freigaberechten |
| Passkey (WebAuthn) | phishing-sichere Anmeldung ohne Passwort | Identity (Stufe 2) |
| Link per Mail | Externe ohne Konto | einmalig, kurz gültig, nur als Hash gespeichert |
| Testanmeldung | nur Entwicklung | das Ausrollen bricht ab, wenn sie eingeschaltet ist |

Für VibeVHS typisch: Identitätsdienst der Kommune (SAML oder OIDC), Entra ID; Teilnehmende per Einmal-Link oder Konto.

- **Ein Identitätsdienst genügt, kein Vermittlerserver nötig.** Bei SSO entscheidet der Identitätsdienst über Passwort, Passkey oder
  App; VibeVHS verlangt für privilegierte Rollen den Nachweis eines zweiten Faktors (REFEDS-MFA bzw. `amr`).
- Externe Konten werden über eine dauerhafte Kennung verknüpft (subject-id bzw. pairwise-id, sonst eppn bzw. `sub`); welche
  Personengruppen sich anmelden dürfen, regelt die Instanz (z. B. nur Beschäftigte).
- **Rechte im Dienst**, nicht nur in der Oberfläche; Rollen nach dem Prinzip der geringsten Rechte, Vier-Augen-Prinzip für
  folgenreiche Schritte, ein Notfallkonto mit eigenem Passwort und zweitem Faktor für den Ausfall des Identitätsdienstes.
- Mandanten: eine Volkshochschule je Instanz.

## 4. Daten

- **Klassifizierung** jeder Datenart (normal, sensibel, besonders). Besonders schutzbedürftig in VibeVHS: Bankverbindungen (IBAN), Aufenthaltsdaten bei Integrationskursen, Gesundheitsangaben für Kurse.
- **Feldverschlüsselung** für die Stufe „besonders“: AES-256-GCM in der Anwendung, nach der Rechteprüfung; Datenbank und Sicherungen
  sehen nur Geheimtext. Jeder Wert trägt die **Schlüsselversion** – damit sind Schlüsselwechsel (Rotation) im laufenden Betrieb
  möglich; je Mandant ein abgeleiteter Schlüssel (HKDF).
- **Schlüssel geheim halten:** als verschlüsseltes **systemd-Credential** (an das TPM gebunden, wo vorhanden) bzw. als **Docker
  Secret** – nicht als Umgebungsvariable, nie in Datenbank, Repository oder Abbild. Zweitablage im Passwortsafe des Betreibers.
- **Sicherungen** täglich und vor jedem Update, **mit `age` verschlüsselt** (öffentlicher Schlüssel auf dem Server, privater offline),
  Kopie außer Haus, Prüfsumme und regelmäßige **automatische Rücksicherungsprobe**.
- **Laufwerksverschlüsselung** (LUKS bzw. Hypervisor) als Anforderung an den Server.
- **Datenbank:** SQLite-Datei mit Rechten 0600 nur für das Dienstkonto, WAL-Modus, Sicherung per `sqlite3 .backup` im laufenden Betrieb.
- **Datenschutzfunktionen:** Löschfristen mit Vier-Augen-Prinzip, Auskunft, Pseudonyme statt Klarnamen in Logs.

## 5. Betrieb

| Bereich | Zielbild | Anbindung |
|---|---|---|
| Ausrollen | Prüfphase ohne Änderung, Sicherung, Konfiguration nur ergänzen, Countdown mit Abbruch bei Fehlern, Rückweg jederzeit | eigenes Skript; idempotent, auch aus Ansible o. Ä. aufrufbar |
| Datenbankschema | EF-Core-Migrationen; das Ausrollen prüft vorher, ob die vorhandene Datenbank migrierbar ist | – |
| Pakete | SHA-256-Prüfsumme je Paket, Stückliste (SBOM, CycloneDX) je Version | Abhängigkeitsprüfung, z. B. Dependency-Track |
| Updates | Sicherheitsupdates des Systems automatisch (`unattended-upgrades`, `needrestart`), .NET-Runtime bzw. Abbild monatlich, Abhängigkeitsprüfung in der CI | Dependabot oder Renovate |
| Logs | strukturiert ins Journal, keine Personendaten im Klartext | Syslog über TLS (rsyslog) an Graylog, Loki, Elastic oder ein SIEM; OTLP |
| Metriken und Traces | OpenTelemetry | OTLP an jeden Collector; Prometheus-Endpunkt `/metrics` nur für das Monitoring-Netz |
| Gesundheit | `/gesund/live` (läuft), `/gesund/bereit` (Datenbank, Platz, Zertifikat), `/gesund/version` | Checkmk, Zabbix, Icinga, Uptime Kuma, Kubernetes-Probes |
| Zertifikate | Ablauf wird überwacht | Meldung über Log und Gesundheitsprüfung |

## 6. Unterstützung von Sicherheitsprüfungen (Audit)

- **Sicherheitsprotokoll** getrennt vom Betriebslog: Anmeldungen und Fehlversuche, Rechte- und Rollenänderungen, Exporte, Zugriffe
  auf besonders schutzbedürftige Daten, Konfigurationsänderungen. Nur Anfügen, hash-verkettet (Manipulation fällt auf),
  weiterleitbar an ein SIEM.
- **Prüferunterlagen** je System, aktuell gehalten: Sicherheitskonzept (dieses Kapitel), Datenflussbild, Rollen-Rechte-Matrix
  (aus dem Code erzeugt), technische und organisatorische Maßnahmen nach Art. 32 DSGVO, Logging-, Patch- und Sicherungskonzept,
  ASVS-Checkliste mit Umsetzungsstand und Zuordnung zu den BSI-Bausteinen APP.3.1 (Webanwendungen), APP.3.2 (Webserver),
  SYS.1.3 (Linux-Server), CON.3 (Datensicherung), OPS.1.1.5 (Protokollierung).
- **Zugänge für Prüfende:**
  - Rolle **„Revision“** in der Anwendung: nur lesend; sieht Konfiguration, Rollen und Rechte, Sicherheitsprotokoll – keine Inhalte.
  - **Sicherheitsbericht** als Export (aktive Verfahren, Schalter, Versionen, letzte Sicherung und Rücksicherungsprobe, Zertifikate).
  - Befristetes **Serverkonto `pruefer`** mit ausschließlich lesenden Befehlen (wirksame Proxy- und Dienstkonfiguration, Firewall,
    Logs), vom Betreiber freigeschaltet und wieder entzogen.
  - **Testinstanz mit Demodaten** für Penetrationstests; `security.txt` mit Meldeweg für Sicherheitslücken.

## Bewusst nicht im Zielbild

Web Application Firewall (z. B. ModSecurity), Hardware-Sicherheitsmodul und ein eigener Vermittler-Identitätsdienst (z. B. Keycloak)
sind für Systeme dieser Größe unverhältnismäßig; kann ein Betreiber ergänzen. Optional empfohlen: fail2ban oder CrowdSec am Server.
