# Bereitstellung

Testsystem auf **https://liebler.iui.hs-osnabrueck.de/vibevhs** (nginx als Reverse Proxy, Hochschulzertifikat, Node-RED unter
`/nodered`). **Gesamtbild für Admins** (Ablauf, Konfiguration, Pfade, Updates, Datenbank, Betrieb):
[`docs/bereitstellung.md`](../docs/bereitstellung.md). Alles Technische liegt in [`server/`](server/README-server.md):

- `server/README-server.md` – Runbook Schritt für Schritt (SSH-Schlüssel, Härtung, nginx statt Apache, Node-RED, Zertifikat, Dienst, Firewall)
- `server/schritte/` – je sudo-Schritt ein Skript mit Sicherung, Prüfung und Rückweg
- `server/deploy.ps1` – VibeVHS bauen (linux-x64, framework-abhängig; `-MitRuntime` = self-contained) und ausrollen
- `server/schritte/09-dotnet-runtime.sh` – gemeinsame ASP.NET-Core-Runtime für alle .NET-Apps des Servers, nachts aktualisiert
- `server/schritte/10-postgresql.sh` – PostgreSQL 17 als Container `vibevhs-db`, pg_dump/pg_restore 17 auf dem Host
- `server/nginx/`, `server/systemd/`, `server/ssh/`, `server/nodered/` – Konfigurationsvorlagen

Geheimnisse (Zugangscode, Passwörter, Schlüssel) stehen nie im Repository, sondern nur auf dem Server
(`/etc/vibevhs/vibevhs.env`, `/etc/nginx/vibevhs.htpasswd`).
