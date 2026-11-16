# Bereitstellung auf liebler.iui.hs-osnabrueck.de

Runbook für den Umzug von „Apache auf 443 + Node-RED auf 1880“ zu **nginx als Reverse Proxy** mit Let's Encrypt,
**Node-RED unter `/nodered`** und **VibeVHS unter `/vibevhs`**. Jeder Schritt, der root braucht, ist ein eigenes Skript in
`schritte/`: idempotent, mit Sicherung vorher (`/root/vibevhs-sicherung/<Zeit>-<Schritt>`), Prüfung danach und Rückweg.

| | |
|---|---|
| Server | Ubuntu 26.04.1 LTS, x86_64 · Benutzer `srvadmin` (sudo mit Passwort) |
| Adressen | 131.173.241.60 · 2001:638:513:f10::83ad:f13c (IPv6 – nginx und ufw gelten für beide) |
| Heute | Apache 2.4.66 auf 80/443 (statisch `/var/www/html`: `index.html`, `rgb/`, `simplesorter/`), Node-RED auf 1880 (HTTPS), OpenSSH 10.2 |
| Zertifikat | certbot (snap) mit webroot `/var/www/html`, `snap.certbot.renew.timer` aktiv; ausgeliefert wird aber GEANT (bis 07.02.2027) aus dem LE-Pfad |
| Weitere Dienste | mosquitto (8883 TLS), influxdb3 (8181), nagios-nrpe (5666), Docker als snap (9443, 8000) – bleiben unverändert |
| Firewall | ufw schon **aktiv** (Standard DROP) – Schritt 07 ergänzt nur, nichts wird zurückgesetzt |
| Ziel | 443 nginx (+80 für Let's Encrypt und Umleitung) · 22 nur aus dem VPN · 1880 zu |
| CAA | keine Einträge – Let's Encrypt darf ausstellen; Port 80 ist von außen offen |

Bestandsaufnahme (nur lesend, alle Befehle wörtlich): [`protokoll/2026-11-16-erkundung.md`](protokoll/2026-11-16-erkundung.md).

SSH-Host-Schlüssel des Servers (bei der ersten Verbindung vergleichen):

```
ED25519 SHA256:ToSJngSfrZMY5dHJPFAAcIIa91NQG3PZu7vznb/XB40
ECDSA   SHA256:Es80ZFNAUByLJLQ3McI9vbLR6loVR9aaMWktcjoPEvQ
RSA     SHA256:EkK0c7hZFTd/bvy/LcZgrAJkIDSQnMzTGvuGS1rVHCo
```

**Reihenfolge:** A → 01 → 02 → 03 → 04 → 05 → 06 → Ausrollen → 07. **Jeden Schritt startet Klaus selbst** (bzw. gibt ihn frei). Bei jedem Schritt eine **zweite SSH-Sitzung offen
lassen** (Rettungsanker). Alle Kommandos stammen aus Windows-PowerShell auf dem Entwicklungsrechner, im VPN.

---

## A · SSH-Schlüssel (Windows, ohne sudo)

```powershell
ssh-keygen -t ed25519 -C "klaus@protzklotz" -f $env:USERPROFILE\.ssh\id_ed25519_liebler_iui_hs_osnabrueck      # Passphrase setzen
type $env:USERPROFILE\.ssh\id_ed25519_liebler_iui_hs_osnabrueck.pub | ssh srvadmin@liebler.iui.hs-osnabrueck.de "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

Kurzname in `%USERPROFILE%\.ssh\config` (dann reicht `ssh liebler`):

```
Host liebler
    HostName liebler.iui.hs-osnabrueck.de
    User srvadmin
    IdentityFile ~/.ssh/id_ed25519_liebler_iui_hs_osnabrueck
```

**Prüfen:** `ssh liebler` fragt nur nach der Passphrase, nicht nach dem Server-Passwort.
Scheitert die Windows-Anmeldung am Schlüsselaustausch (OpenSSH 10 auf dem Server), das `ssh` aus Git verwenden
(`C:\Program Files\Git\usr\bin\ssh.exe`) oder Windows-OpenSSH aktualisieren.

**Kit hochladen** (vor den Schritten und nach jeder Änderung am Kit):

```powershell
.\deploy\server\deploy.ps1 -NurKit
```

Jeder Schritt startet dann so (sudo fragt nach dem Passwort von `srvadmin`):

```powershell
ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/<schritt>.sh"
```

---

## 01 · SSH nur mit Schlüssel

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/01-ssh-haerten.sh"`

- Prüft, dass `~srvadmin/.ssh/authorized_keys` einen Schlüssel enthält und dass `srvadmin` sich damit angemeldet hat.
- Spielt `ssh/10-haertung.conf` nach `/etc/ssh/sshd_config.d/` ein: `PasswordAuthentication no`, `KbdInteractiveAuthentication no`,
  `PermitRootLogin no`, `AllowUsers srvadmin`; `sshd -t` vor dem Neuladen.
- **Prüfen** (neues Fenster): `ssh liebler` klappt; `ssh -o PubkeyAuthentication=no srvadmin@liebler.iui.hs-osnabrueck.de` wird abgelehnt.
- **Rückweg:** `sudo bash /tmp/vibevhs-kit/schritte/01-zurueck.sh` (in der offenen zweiten Sitzung).

**sudo:** `srvadmin` hat sudo mit Passwort – so bleibt es (empfohlen). Wer „direkt sudo“ ohne Passwort möchte:

```bash
echo 'srvadmin ALL=(ALL) NOPASSWD:ALL' | sudo tee /etc/sudoers.d/srvadmin && sudo chmod 440 /etc/sudoers.d/srvadmin && sudo visudo -c
```

Sicherheitshinweis: Dann reicht der SSH-Schlüssel allein für root. Nur sinnvoll mit Passphrase auf dem Schlüssel; gern
befristet (danach `sudo rm /etc/sudoers.d/srvadmin`).

---

## 02 · Pakete

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/02-pakete.sh"`

nginx, certbot, ufw, apache2-utils (htpasswd), at, sqlite3, curl und die passende `libicuNN` für .NET. nginx wird **nicht**
gestartet (Apache belegt 80/443 noch). **Prüfen:** Ausgabe zeigt Versionen und „Apache läuft weiter“. Rückweg: nicht nötig.

VibeVHS braucht **kein .NET auf dem Server**: `deploy.ps1` baut self-contained für linux-x64 (für Ubuntu 26.04 gibt es noch
keine verlässliche Microsoft-Paketquelle).

---

## 03 · nginx statt Apache

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/03-nginx-statt-apache.sh"`

1. **Bestand:** zeigt, was Apache ausliefert (Sites, Module, Inhalte unter `/var/www`), sichert `/etc/apache2` und `/etc/nginx`.
   Liefert Apache mehr als die Standardseite aus, fragt das Skript nach (**Bisherige Inhalte** unten).
2. **Zertifikat:** übernimmt vorerst das Hochschul-Zertifikat von Apache → kein Zertifikatswechsel beim Umschalten.
3. **Konfiguration:** `nginx/liebler.conf` (+ Snippets) nach `/etc/nginx`, Standard-Site aus, `nginx -t`.
4. **Zugangspasswort** für `/vibevhs` (Benutzer `vhs-test`, Passwort fragt `htpasswd`).
5. **Umschalten:** Apache stoppen und deaktivieren (nicht deinstallieren), nginx starten – startet nginx nicht, kommt Apache sofort zurück.

**Prüfen:** `https://liebler.iui.hs-osnabrueck.de/` zeigt die Übersicht; `http://…` leitet auf HTTPS um.
**Rückweg:** `sudo bash /tmp/vibevhs-kit/schritte/03-zurueck.sh` (nginx aus, Apache an).

**Bisherige Inhalte:** Apache liefert nur statische Seiten aus `/var/www/html` (`index.html`, `rgb/`, `simplesorter/`, kein PHP,
keine Proxys). nginx liefert dasselbe Verzeichnis unverändert aus (`root /var/www/html`); das Skript prüft `/rgb/` und `/simplesorter/`.
Schritt 03 legt außerdem den certbot-Hook „nginx neu laden“ an (neben dem vorhandenen `nodered-cert-copy.sh`).

---

## 04 · Node-RED unter /nodered

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/04-nodered-lokal.sh"`

Node-RED läuft als `nodered.service` (Benutzer `nodered`, v4.1.0, `/home/nodered/.node-red/settings.js`) mit eigenem HTTPS
(`requireHttps`, Zertifikat kopiert der certbot-Hook) und `adminAuth`. Das Skript setzt (mit Sicherung) `uiHost: "127.0.0.1"`,
`httpAdminRoot` und `httpNodeRoot` = `"/nodered"`, prüft die Syntax und startet Node-RED neu. HTTPS und Anmeldung bleiben;
nginx verbindet sich per `https://127.0.0.1:1880`. Danach ist 1880 nur noch lokal.

**Achtung:** HTTP-Endpunkte der Flows heißen danach `https://liebler…/nodered/<pfad>` statt `…:1880/<pfad>` – Geräte oder
Skripte, die Node-RED direkt auf 1880 ansprechen, vorher umstellen. MQTT (mosquitto) ist nicht betroffen.

- **Prüfen:** `https://liebler.iui.hs-osnabrueck.de/nodered/` öffnet den Editor. HTTP-Endpunkte der Flows heißen jetzt `/nodered/<pfad>`.
- Findet das Skript `settings.js` nicht: `ssh -t liebler "sudo NODERED_SETTINGS=/pfad/settings.js bash /tmp/vibevhs-kit/schritte/04-nodered-lokal.sh"`.
- **Rückweg:** `sudo bash /tmp/vibevhs-kit/schritte/04-zurueck.sh`.

---

## 05 · Let's Encrypt mit automatischer Erneuerung

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/05-zertifikat.sh"`

certbot ist schon eingerichtet (snap, webroot `/var/www/html`, `snap.certbot.renew.timer` zweimal täglich). Der Dienst, der das
Zertifikat erneuert, heißt also **certbot** – „Let's Encrypt“ ist die Zertifizierungsstelle, die es ausstellt. Im Let's-Encrypt-Pfad
liegt derzeit aber das GEANT-Zertifikat. Das Skript zeigt `certbot certificates`, macht einen Probelauf und holt nach Rückfrage
ein echtes Let's-Encrypt-Zertifikat (`certbot renew --force-renewal`). Danach erneuert der Timer automatisch; die Hooks laden
nginx neu und kopieren das Zertifikat für Node-RED. **Prüfen:** Ausgabe zeigt Aussteller „Let's Encrypt“,
Ablaufdatum und „Erneuerung (Probelauf) klappt“. **Rückweg:** `05-zurueck.sh` (wieder das Hochschul-Zertifikat).

**Voraussetzung: Port 80 von außen erreichbar.** Aus dem VPN ist 80 offen; ob auch von außen, bitte ohne VPN prüfen
(z. B. Handy im Mobilnetz: `http://liebler.iui.hs-osnabrueck.de` muss auf HTTPS umleiten). Falls nicht:

1. **Port 80 bei der Hochschul-IT freigeben lassen (empfohlen).** nginx beantwortet dort nur die Prüfung und leitet alles andere auf HTTPS um.
2. **TLS-ALPN-01 über 443** (lego oder acme.sh): braucht vor nginx ein `stream`-Modul mit `ssl_preread`, das ALPN `acme-tls/1`
   an den ACME-Client weiterreicht – aufwendiger, aber ohne Port 80.
3. **DNS-01**, falls die Hochschule eine DNS-API bzw. eine delegierte Zone `_acme-challenge.liebler…` anbietet.
4. **Hochschul-Zertifikat behalten** (GEANT/HARICA über die DFN-PKI, jährlich manuell verlängern) – Schritt 05 dann auslassen.

---

## 06 · VibeVHS-Dienst

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/06-vibevhs-einrichten.sh"`

Dienstbenutzer `vibevhs`, Programm `/opt/vibevhs`, Daten `/var/lib/vibevhs`, Logs `/var/log/vibevhs`, Sicherungen
`/var/backups/vibevhs`, Umgebung `/etc/vibevhs/vibevhs.env` (Geheimnisse nur dort), Unit `systemd/vibevhs.service`:
Kestrel nur auf `127.0.0.1:5080`, `ASPNETCORE_ENVIRONMENT=Testsystem` (Pfad-Basis `/vibevhs`, Proxy-Header, Banner
„Testsystem“, Mock-Login mit Rollenwahl, Demodaten, Testbenutzer der vhs EGS, Mails nur im Postausgang), Härtung (`ProtectSystem=strict` …).

**Ausrollen** (jetzt und bei jedem Update, vom Entwicklungsrechner):

```powershell
.\deploy\server\deploy.ps1
```

Baut self-contained linux-x64, lädt Kit und Archiv hoch, sichert die Datenbank, ersetzt `/opt/vibevhs` (vorige Version
bleibt als `/opt/vibevhs.vorher`) und prüft `http://127.0.0.1:5080/vibevhs/gesund`. Antwortet die neue Version nicht, kommt die
vorige automatisch zurück. Von Hand zurück: `ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh --zurueck"`.

**Prüfen:** `https://liebler.iui.hs-osnabrueck.de/vibevhs/` fragt nach dem Zugangspasswort (Benutzer `vhs-test`), danach
die Testanmeldung mit dem Banner „Testsystem“.

**Ohne Passwort erreichbar** (`/etc/nginx/snippets/vibevhs-ohne-passwort.conf`, nach Änderung `sudo nginx -t && sudo systemctl reload nginx`):

| Pfad | Standard | Warum |
|---|---|---|
| `…/portal/*/kalender` (ICS-Abos) | frei | Kalender-Apps senden kein Passwort; geheimes Token in der Adresse |
| `…/checkin`, `…/k/` (Check-in, Kurs-QR) | frei | wird im Kursraum mit dem Handy gescannt; Token im Link, Rate-Limit |
| statische Dateien (css/js/Bilder/Schriften) | frei | sonst laden die freien Seiten unvollständig; keine Daten |
| `…/api/v1/shop`, `…/shop/` | Passwort | erst freigeben, wenn TYPO3 oder ein Webshop von außen bucht |
| `…/brief/`, `…/uebersetzen/` | Passwort | freigeben, wenn echte Briefe/Mails mit Übersetzungslink verschickt werden |
| `…/portal/` | Passwort | freigeben, wenn echte Teilnehmende/Kursleitungen die Portale nutzen |

**Sicherung:** täglich 01:30 Uhr Online-Sicherung nach `/var/backups/vibevhs` mit Wiederherstellungstest (Seite *Betrieb*),
dazu vor jedem Update `vor-update-*.db`. **Optional Litestream** (fortlaufende Replikation, Vorlage `litestream/litestream.yml`):
`apt install litestream` bzw. Paket von litestream.io, Konfiguration nach `/etc/litestream.yml`, `systemctl enable --now litestream`.

**Rücksicherung:** `sudo systemctl stop vibevhs` → gewünschte Sicherung als `/var/lib/vibevhs/vibevhs.db` einsetzen
(vorher `-wal`/`-shm` beiseitelegen, Besitzer `vibevhs`) → `sudo systemctl start vibevhs`. Details: `docs/betrieb.md`.

---

## 07 · Firewall

```powershell
ssh -t liebler "sudo VPN_NETZ=172.22.192.0/21 VPN_NETZ6=2001:638:513:fe02::/64 bash /tmp/vibevhs-kit/schritte/07-firewall.sh"
```

Die Netze stammen aus Klaus' eduVPN-Verbindung (Adapter `172.22.193.94/21`, `2001:638:513:fe02::15e/64`; der Server sah die
IPv6-Adresse). eduVPN kann weitere Adresspools haben – passt eine spätere Verbindung nicht, das Netz ergänzen oder großzügiger
das Hochschulnetz `2001:638:513::/48` nehmen.

ufw ist schon **aktiv** mit eigenen Regeln (z. B. MQTT 8883). Das Skript zeigt die bisherigen Regeln, **ergänzt** 443, 80 und SSH
aus dem VPN und fragt bei Regeln, die 22 oder 1880 für alle öffnen, einzeln nach, ob sie gelöscht werden sollen. Nichts wird
zurückgesetzt. **Rettungsanker:** nach 10 Minuten kommt der vorige Stand automatisch zurück, bis der Auftrag mit
`sudo atrm $(atq | awk '{print $1}')` entfernt wird. **Prüfen** (neues Fenster, im VPN): `ssh liebler`, `https://…/`.
**Rückweg:** `07-zurueck.sh` (vorige Regeln). Hinweis: Docker (snap) veröffentlicht Ports an ufw vorbei.

Die Hochschul-Firewall davor filtert zusätzlich: **Port 1880 dort schließen lassen**, 80 öffnen (falls Schritt 05 per HTTP-01).

---

## Danach

- **HSTS** einschalten, wenn alles über HTTPS läuft und Let's Encrypt erneuert: in `liebler.conf` die Zeile `Strict-Transport-Security` einkommentieren, `sudo nginx -t && sudo systemctl reload nginx`.
- Apache deinstallieren, wenn nichts mehr fehlt: `sudo apt purge apache2` (Konfiguration liegt gesichert unter `/root/vibevhs-sicherung/`).
- Zertifikatsablauf beobachten: `sudo certbot certificates`; Erneuerungs-Timer: `systemctl list-timers certbot.timer`.
- Logs: `journalctl -u vibevhs -f`, `/var/log/vibevhs/`, `/var/log/nginx/`.

## Offene Punkte

1. Wer spricht Node-RED heute direkt auf Port 1880 an (Geräte, Skripte)? Diese Adressen ändern sich auf `/nodered/…`.
2. Sollen 8181 (influxdb3), 9443/8000 (Docker) und 5666 (nagios) von außen erreichbar bleiben? (Heute filtert die Hochschul-Firewall.)
3. Wie kam das GEANT-Zertifikat in den Let's-Encrypt-Pfad? Schritt 05 ersetzt es nach Rückfrage.
