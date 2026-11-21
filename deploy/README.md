# Bereitstellung

Testsystem auf **https://liebler.iui.hs-osnabrueck.de/vibevhs** (nginx als Reverse Proxy, Hochschulzertifikat, Node-RED unter
`/nodered`). **Gesamtbild für Admins** (Ablauf, Konfiguration, Pfade, Updates, Datenbank, Betrieb):
[`docs/bereitstellung.md`](../docs/bereitstellung.md). Alles Technische liegt in [`server/`](server/README-server.md):

- `server/README-server.md` – Runbook Schritt für Schritt (SSH-Schlüssel, Härtung, nginx statt Apache, Node-RED, Zertifikat, Dienst, Firewall)
- `server/schritte/` – je sudo-Schritt ein Skript mit Sicherung, Prüfung und Rückweg
- `server/deploy.ps1` – VibeVHS bauen (self-contained linux-x64) und ausrollen
- `server/nginx/`, `server/systemd/`, `server/ssh/`, `server/nodered/`, `server/litestream/` – Konfigurationsvorlagen

Geheimnisse (Zugangscode, Passwörter, Schlüssel) stehen nie im Repository, sondern nur auf dem Server
(`/etc/vibevhs/vibevhs.env`, `/etc/nginx/vibevhs.htpasswd`).
