# Bereitstellung

> Stand: noch nicht ausgerollt. Zuerst wird lokal und nativ entwickelt; der Umzug auf den Server folgt später.

## Testsystem: https://liebler.iui.hs-osnabrueck.de/vibevhs (nativ, ohne Docker)

Umgebung `Testsystem` (`src/VibeVHS.Web/appsettings.Testsystem.json`): Pfad-Basis `/vibevhs`, Betrieb hinter
Reverse Proxy, Demodaten an, Startdaten der vhs EGS (Benutzerkonten des Teams).

**Schutz:** Weil die Testanmeldung kein Passwort je Konto kennt und die Konten echte Namen tragen, steht davor ein
gemeinsames Zugangspasswort am Webserver (Basic Auth, siehe `linux/nginx-vibevhs.conf`). Optional kann die
Anwendung zusätzlich einen Zugangscode abfragen (`Anmeldung__Zugangscode` in `/etc/vibevhs.env`, nie im Repository).

### Einmalig auf dem Server (als root)

```bash
useradd --system --home /var/lib/vibevhs --shell /usr/sbin/nologin vibevhs
install -d -o vibevhs -g vibevhs /var/lib/vibevhs
touch /etc/vibevhs.env && chmod 600 /etc/vibevhs.env
cp linux/vibevhs.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable vibevhs
htpasswd -c /etc/nginx/vibevhs.htpasswd vhs-test
# linux/nginx-vibevhs.conf in den bestehenden 443-server-Block übernehmen, dann:
nginx -t && systemctl reload nginx
```

### Ausrollen (vom Entwicklungsrechner)

```powershell
.\deploy\veroeffentlichen.ps1 -Ziel klaus@liebler.iui.hs-osnabrueck.de
```

Baut eine self-contained Linux-Version (kein .NET auf dem Server nötig), kopiert sie nach `/opt/vibevhs`
und startet den Dienst neu. Datenbank, Logs und Schlüssel liegen getrennt unter `/var/lib/vibevhs` und bleiben erhalten.

## Docker

Docker-Image (mehrstufig, SQLite und Dokumente in einem Volume) und `docker-compose.yml` mit Mailpit folgen später.
