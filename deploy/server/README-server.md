# Bereitstellung auf liebler.iui.hs-osnabrueck.de

Runbook für den Umzug von „Apache auf 443 + Node-RED auf 1880“ zu **nginx als Reverse Proxy** mit dem Hochschulzertifikat (GEANT/HARICA),
**Node-RED unter `/nodered`** und **VibeVHS unter `/vibevhs`**. Jeder Schritt, der root braucht, ist ein eigenes Skript in
`schritte/`: idempotent, mit Sicherung vorher (`/root/vibevhs-sicherung/<Zeit>-<Schritt>`), Prüfung danach und Rückweg.

| | |
|---|---|
| Server | Ubuntu 26.04.1 LTS, x86_64 · Benutzer `srvadmin` (sudo mit Passwort) |
| Adressen | 131.173.241.60 · 2001:638:513:f10::83ad:f13c (IPv6 – nginx und ufw gelten für beide) |
| Heute | Apache 2.4.66 auf 80/443 (statisch `/var/www/html`: `index.html`, `rgb/`, `simplesorter/`), Node-RED auf 1880 (HTTPS), OpenSSH 10.2 |
| Zertifikat | heute: GEANT (bis 07.02.2027), von Hand im Let's-Encrypt-Pfad abgelegt; certbot (snap) + Timer laufen ungenutzt mit · **künftig:** nur das Hochschulzertifikat in `/etc/ssl/liebler` für nginx und mosquitto, kein Let's Encrypt |
| Weitere Dienste | mosquitto (8883 TLS, bisher Zertifikat der eigenen Root-CA), influxdb3 (8181), nagios-nrpe (5666), Docker als snap (9443, 8000) – bleiben erreichbar |
| Firewall | ufw schon **aktiv** (Standard DROP) – Schritt 07 ergänzt nur, nichts wird zurückgesetzt |
| Ziel | 443 nginx (+80 nur Umleitung) · 22 nur aus dem VPN · 1880 zu · 8883/8181/5666/9443/8000 wie bisher |

Bestandsaufnahme (nur lesend, alle Befehle wörtlich): [`protokoll/2026-11-16-erkundung.md`](protokoll/2026-11-16-erkundung.md).

SSH-Host-Schlüssel des Servers (bei der ersten Verbindung vergleichen):

```
ED25519 SHA256:ToSJngSfrZMY5dHJPFAAcIIa91NQG3PZu7vznb/XB40
ECDSA   SHA256:Es80ZFNAUByLJLQ3McI9vbLR6loVR9aaMWktcjoPEvQ
RSA     SHA256:EkK0c7hZFTd/bvy/LcZgrAJkIDSQnMzTGvuGS1rVHCo
```

**Reihenfolge:** A → 01 → 02 → 03 → 04 → 05 → 06 → Ausrollen → 07 → (später) 08. **Jeden Schritt startet Klaus selbst** (bzw. gibt ihn frei). Bei jedem Schritt eine **zweite SSH-Sitzung offen
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

nginx, ufw, apache2-utils (htpasswd), at, sqlite3 (nur noch für Altdateien), curl, openssl und die passende `libicuNN` für .NET (kein certbot). nginx wird **nicht**
gestartet (Apache belegt 80/443 noch). **Prüfen:** Ausgabe zeigt Versionen und „Apache läuft weiter“. Rückweg: nicht nötig.

Die .NET-Runtime kommt mit **Schritt 09** einmalig für alle Apps auf den Server (siehe dort); bis dahin baut `deploy.ps1 -MitRuntime`
self-contained für linux-x64.

---

## 03 · nginx statt Apache

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/03-nginx-statt-apache.sh"`

1. **Bestand:** zeigt, was Apache ausliefert (Sites, Module, Inhalte unter `/var/www`), sichert `/etc/apache2` und `/etc/nginx`.
   Liefert Apache mehr als die Standardseite aus, fragt das Skript nach (**Bisherige Inhalte** unten).
2. **Zertifikat:** nimmt das Hochschulzertifikat, das Apache heute nutzt, prüft es (Aussteller GEANT/HARICA, gilt für den Hostnamen,
   nicht abgelaufen, **Schlüssel passt** per Vergleich der öffentlichen Schlüssel, Kette vorhanden) und legt es zentral ab:
   `/etc/ssl/liebler/fullchain.pem` und `privkey.pem` (root:ssl-cert, Schlüssel 0640). nginx nutzt nur noch diesen Ort;
   `/etc/letsencrypt` bleibt bis Schritt 08 unangetastet. Dazu die **Ablaufwarnung** (`zertifikat-ablauf.timer`, siehe unten).
3. **Konfiguration:** Grundgerüst `nginx/liebler.conf` (TLS, :80→:443, `/var/www/html`, `include /etc/nginx/liebler.d/*.conf;`)
   und die eigenen Dateien `nginx/liebler.d/vibevhs.conf` und `nodered.conf` nach `/etc/nginx/liebler.d/`, Standard-Site aus.
   **Gemeinsames Schema** (`schritte/liebler-nginx.sh`, gleich in den Kits von FraktionPerfekt und Podium): jede App pflegt nur
   ihre Datei in `liebler.d/` – ein erneuter Schritt 03 überschreibt die anderen Apps nicht mehr. Ältere include-Zeilen in
   `liebler.conf` zieht das Skript nach `liebler.d/` um. Jede Änderung: Sicherung → `nginx -t` → reload, sonst zurück.
4. **Zugangspasswort** für `/vibevhs` (Benutzer `vhs-test`, Passwort fragt `htpasswd`).
5. **Umschalten:** Apache stoppen und deaktivieren (nicht deinstallieren), nginx starten – startet nginx nicht, kommt Apache sofort zurück.

**Prüfen:** `https://liebler.iui.hs-osnabrueck.de/` zeigt die Übersicht; `http://…` leitet auf HTTPS um.
**Rückweg:** `sudo bash /tmp/vibevhs-kit/schritte/03-zurueck.sh` (nginx aus, Apache an).

**Bisherige Inhalte:** Apache liefert nur statische Seiten aus `/var/www/html` (`index.html`, `rgb/`, `simplesorter/`, kein PHP,
keine Proxys). nginx liefert dasselbe Verzeichnis unverändert aus (`root /var/www/html`); das Skript prüft `/rgb/` und `/simplesorter/`.

---

## 04 · Node-RED unter /nodered

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/04-nodered-lokal.sh"`

Node-RED läuft als `nodered.service` (Benutzer `nodered`, v4.1.0, `/home/nodered/.node-red/settings.js`) heute mit eigenem HTTPS
(`requireHttps`, Zertifikat aus `/etc/nodered/certs`, das der certbot-Hook kopiert) und `adminAuth`. Das Skript hängt (mit Sicherung)
einen markierten Block an `settings.js` an: `uiHost: "127.0.0.1"`, `httpAdminRoot`/`httpNodeRoot` = `"/nodered"`,
`requireHttps: false`, eigenes `https` aus. HTTPS nach außen macht nginx mit dem Hochschulzertifikat; nginx spricht Node-RED
lokal per `http://127.0.0.1:1880` an – **Node-RED braucht kein eigenes Zertifikat mehr**. Die Anmeldung (`adminAuth`) bleibt,
das Skript warnt, falls sie fehlt. Danach ist 1880 nur noch lokal.

**Achtung:** HTTP-Endpunkte der Flows (und ein Dashboard) heißen danach `https://liebler…/nodered/<pfad>` statt `…:1880/<pfad>`.
Laut Klaus spricht von außen niemand Node-RED direkt an. MQTT (mosquitto) ist nicht betroffen.

- **Prüfen:** `https://liebler.iui.hs-osnabrueck.de/nodered/` öffnet den Editor. HTTP-Endpunkte der Flows heißen jetzt `/nodered/<pfad>`.
- Findet das Skript `settings.js` nicht: `ssh -t liebler "sudo NODERED_SETTINGS=/pfad/settings.js bash /tmp/vibevhs-kit/schritte/04-nodered-lokal.sh"`.
- **Rückweg:** `sudo bash /tmp/vibevhs-kit/schritte/04-zurueck.sh`.

---

## 05 · mosquitto auf das Hochschulzertifikat

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/05-mosquitto-zertifikat.sh"`

mosquitto (8883) nutzt bisher ein Zertifikat der eigenen Root-CA („AAA Klaus Liebler personal Root CA“). Laut Klaus vertrauen alle
Geräte öffentlichen Wurzeln und akzeptieren damit das GEANT-/HARICA-Zertifikat.

1. Prüft das Hochschulzertifikat in `/etc/ssl/liebler` und zeigt die bisherigen TLS-Einstellungen und das bisherige Zertifikat.
2. Nach Rückfrage: sichert `/etc/mosquitto` komplett, nimmt den Benutzer `mosquitto` in die Gruppe `ssl-cert` auf und stellt
   `certfile`/`keyfile` auf `/etc/ssl/liebler` um. `cafile` bleibt (prüft ggf. Client-Zertifikate der Geräte).
3. **Test vor dem Neustart:** kann `mosquitto` die Dateien lesen? Probestart mit derselben TLS-Konfiguration auf `127.0.0.1:18883`
   und TLS-Handshake dagegen (mosquitto 2 hat keinen reinen Konfigurationstest). Scheitert das, wird die Konfiguration
   zurückgespielt, ohne dass der laufende Broker angefasst wurde.
4. Neustart, dann `openssl s_client -connect liebler.iui.hs-osnabrueck.de:8883 -servername liebler.iui.hs-osnabrueck.de`
   (Aussteller GEANT/HARICA, Ablaufdatum). Klappt das nicht, kommt die vorige Konfiguration sofort zurück.

**Prüfen:** Senden die Geräte weiter? (`journalctl -u mosquitto -f`, Node-RED-Flows)
**Rückweg:** `05-zurueck.sh` (vorige Konfiguration mit eigenem Zertifikat, Gruppe `ssl-cert` wieder entfernt).

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

Baut linux-x64 (framework-abhängig, rund 20 MB; `-MitRuntime` = self-contained) und lädt Kit und Archiv hoch. Auf dem Server (`schritte/ausrollen.sh`, seit 24.11.2026 „mitdenkend“):

1. **Prüflauf vor jeder Änderung:** Die neue Version läuft einmal als Dienstbenutzer mit der Umgebung des Dienstes
   (`VibeVHS.Web --datenbankstand`, ändert nichts) und meldet je Datenbank: *Aktuell*, *Neu* (fehlt/leer, wird angelegt),
   *Migrierbar* (ausstehende EF-Migrationen, sobald es welche gibt), *Veraltet* (Schema geändert, keine Migration) oder
   *KeineVerbindung*. Bei *Veraltet* endet das Ausrollen, bevor die laufende Version angehalten wird, und `deploy.ps1` fragt,
   ob mit neu aufgebauter Datenbank ausgerollt werden soll. Bei *KeineVerbindung* oder veralteter Kommentar-Datenbank endet es ebenso.
2. Sicherung (`pg_dump` beider Datenbanken), Austausch von `/opt/vibevhs` (vorige Version bleibt als `/opt/vibevhs.vorher`), Start.
3. **Warten mit Blick auf den Prozess:** `/gesund` wird jede Sekunde gefragt; beendet sich der Dienst oder startet systemd ihn neu,
   wird sofort abgebrochen statt bis zum Zeitlimit zu warten. Alle 10 s erscheint die letzte Logzeile (Erststart mit Demodaten gut 2 min).
4. **Log deuten:** Scheitert der Start, nennt das Skript die Ursache aus bekannten Meldungen (Schema veraltet, PostgreSQL nicht
   erreichbar, Anmeldung abgelehnt, Port 5080 belegt, Rechte, Speicher, Absturz) mit dem nächsten Schritt und holt die vorige Version zurück.
5. Läuft die neue Version, nennt es trotzdem Fehlermeldungen (ERR/FTL) aus dem Start.

Von Hand zurück: `ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh --zurueck"`.
Nur prüfen, ob die neue Version passt (ändert nichts): `.\deploy\server\deploy.ps1 -Pruefen`.

**Schemaänderung (solange es keine EF-Migrationen gibt):** Der Prüflauf erkennt sie vorab; `deploy.ps1` bietet dann den Neuaufbau an,
oder gleich:

```powershell
.\deploy\server\deploy.ps1 -DbNeu            # mit Rückfrage; -OhneRueckfrage für Skripte
```

`-DbNeu` benennt die Datenbank `vibevhs` im Container `vibevhs-db` in `vibevhs_alt_<zeitstempel>` um (nichts wird gelöscht), die
Anwendung baut die Datenbank aus Start- und Demodaten neu auf (Erststart gut 2 min, das Skript wartet bis 5 min; ist das Schema unverändert, weist es darauf hin, dass -DbNeu nicht nötig gewesen wäre). Scheitert der Start,
kommen vorige Version **und** vorige Datenbank zurück. Die Kommentar-Datenbank `vibevhs_kommentare` bleibt immer unberührt. Alte
Datenbanken gelegentlich löschen: `docker exec vibevhs-db psql -U vibevhs -d postgres -c 'DROP DATABASE "vibevhs_alt_…"'`.
(Vorfall dazu: `protokoll/2026-11-17-diagnose-502.md`.)

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

**Sicherung:** täglich 01:30 Uhr `pg_dump` nach `/var/backups/vibevhs` mit echtem Wiederherstellungstest (Zurückspielen in eine
Wegwerf-Datenbank, Seite *Betrieb*), dazu vor jedem Update `vor-update-*-vibevhs.dump` und `…-vibevhs_kommentare.dump`.

**Rücksicherung:** `sudo systemctl stop vibevhs` →
`docker exec vibevhs-db psql -U vibevhs -d postgres -c 'ALTER DATABASE vibevhs RENAME TO vibevhs_defekt'` →
`docker exec vibevhs-db createdb -U vibevhs vibevhs` →
`docker exec -i vibevhs-db pg_restore -U vibevhs -d vibevhs --no-owner < /var/backups/vibevhs/<sicherung>.dump` →
`sudo systemctl start vibevhs`. Details: `docs/betrieb.md`.

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

8883, 8181 und 5666 bleiben wie bisher erreichbar: vorhandene Regeln bleiben unverändert; fehlt eine, fragt das Skript, ob es
`ufw allow <port>/tcp` ergänzen soll (Docker 9443/8000 laufen ohnehin an ufw vorbei). Gelöscht wird nur nach Rückfrage
(offenes 22 bzw. 1880). Die Hochschul-Firewall davor filtert zusätzlich: **Port 1880 dort schließen lassen**.

---

## 08 · certbot und Let's Encrypt aufräumen (später)

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/08-certbot-aufraeumen.sh"`

Erst, wenn nginx das Hochschulzertifikat aus `/etc/ssl/liebler` ausliefert und Apache aus ist (das Skript prüft beides und sucht
restliche Verweise auf `/etc/letsencrypt` bzw. `/etc/nodered/certs` in nginx, mosquitto, Apache und Node-RED). Sichert
`/etc/letsencrypt` und `/etc/nodered/certs` komplett als tar und fragt **einzeln**: Timer `snap.certbot.renew.timer` abschalten ·
certbot-snap entfernen · `/etc/letsencrypt` löschen (inkl. Hook `nodered-cert-copy.sh`) · `/etc/nodered/certs` löschen.
Danach kommt Let's Encrypt auf dem Server nicht mehr vor. **Rückweg:** `08-zurueck.sh` (Dateien aus dem tar, certbot-snap und Timer wieder an).

---

## Zertifikat tauschen (jährlich) und Ablaufwarnung

Das Hochschulzertifikat (GEANT TCS über HARICA, beantragt bei der Hochschul-IT) wird **von Hand** erneuert:

```powershell
scp liebler.fullchain.pem liebler.key liebler:/tmp/
ssh -t liebler "sudo bash /tmp/vibevhs-kit/werkzeuge/zertifikat-tauschen.sh /tmp/liebler.fullchain.pem /tmp/liebler.key"
```

Fehlt die Kette in der Zertifikatsdatei, die Datei mit den Zwischenzertifikaten als drittes Argument angeben. Das Werkzeug prüft
(Hostname, Ablauf, Schlüssel passt, Kette), sichert das alte Zertifikat, legt das neue nach `/etc/ssl/liebler`, lädt nginx neu,
startet mosquitto neu und zeigt, was auf 443 und 8883 ausgeliefert wird. Danach die hochgeladenen Dateien löschen.
**Rückweg:** `zertifikat-tauschen.sh --zurueck`.

**Ablaufwarnung:** `zertifikat-ablauf.timer` prüft täglich um 07:30 und schreibt ins Journal (`journalctl -t zertifikat-ablauf`);
ab 30 Tagen vor Ablauf als Warnung. Mit `MAIL_AN=…` in `/etc/default/zertifikat-ablauf` zusätzlich per Mail – das braucht ein
Mailprogramm mit Relay auf dem Server (heute keines eingerichtet). Von Hand: `zertifikat-ablauf --kurz`.
Da `nagios-nrpe` läuft, bietet sich zusätzlich ein Check im Monitoring an, z. B. `check_ssl_cert -H liebler.iui.hs-osnabrueck.de -w 30`
bzw. `check_http -H liebler.iui.hs-osnabrueck.de -S --sni -C 30,14` (auch für Port 8883 mit `check_ssl_cert -p 8883`).

**Option für später (nicht umgesetzt):** GEANT TCS bei HARICA bietet nach unserem Kenntnisstand auch **ACME mit EAB-Zugangsdaten**
(External Account Binding) an. Stellt die Hochschul-IT solche Zugangsdaten bereit, könnte ein ACME-Client (z. B. certbot mit
`--server <HARICA-ACME-URL> --eab-kid … --eab-hmac-key …`) das **Hochschulzertifikat** automatisch erneuern – ohne Let's Encrypt.
Bei der Hochschul-IT nachfragen.

---

## Danach

- **HSTS** einschalten, wenn alles über HTTPS läuft: in `liebler.conf` die Zeile `Strict-Transport-Security` einkommentieren, `sudo nginx -t && sudo systemctl reload nginx`.
- Apache deinstallieren, wenn nichts mehr fehlt: `sudo apt purge apache2` (Konfiguration liegt gesichert unter `/root/vibevhs-sicherung/`).
- Zertifikatsablauf: `zertifikat-ablauf --kurz`, Timer: `systemctl list-timers zertifikat-ablauf.timer`.
- Logs: `journalctl -u vibevhs -f`, `/var/log/vibevhs/`, `/var/log/nginx/`.

## Offene Punkte

1. Mail auf dem Server für die Ablaufwarnung (Relay der Hochschule)? Sonst nur Journal und ggf. Nagios-Check.
2. ACME mit EAB bei HARICA über die Hochschul-IT (automatische Erneuerung des Hochschulzertifikats) – optional.

Geklärt (16.11.2026): Von außen kommt nur MQTT (8883) – Node-RED 1880 wird geschlossen. 8181, 9443, 8000, 5666, 8883 bleiben
erreichbar. Kein Let's Encrypt: Hochschulzertifikat für nginx und mosquitto; alle Geräte vertrauen öffentlichen Wurzeln.
Nur `srvadmin` meldet sich per SSH an (`AllowUsers srvadmin` bleibt).

---

## 09 · Gemeinsame .NET-Runtime (für VibeVHS, FraktionPerfekt, Podium, ProfOMat)

```powershell
ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/09-dotnet-runtime.sh"
```

Installiert die ASP.NET-Core-Runtime 11.0 (`DOTNET_KANAL=11.0`) einmalig für alle .NET-Apps. Danach bauen die Kits framework-abhängig
(Uploads rund 20 statt 70 MB) und `ausrollen.sh` prüft vor jeder Änderung, ob die nötige Runtime da ist (gemeinsames `liebler-dotnet.sh`).

- **Quelle:** zuerst das signierte Ubuntu-Archiv (`aspnetcore-runtime-11.0`). Liefert Ubuntu 26.04 die Version nicht, bricht der Schritt
  ohne Änderung ab und nennt die Optionen: `--backports` (Canonical-PPA `ppa:dotnet/backports`, bevorzugt) oder `--microsoft`
  (Microsofts `dotnet-install.sh` nach `/usr/share/dotnet`, Notlösung). **Auf liebler (Ubuntu 26.04) war `--backports` nötig**
  (eingerichtet am 23.11.2026, Runtime unter `/usr/lib/dotnet`); spätere Aufrufe ohne Option behalten diese Quelle. Meldet Launchpad
  „503 Service Unavailable“, ist das ein vorübergehender Ausfall des PPA: einige Minuten warten, erneut starten (nichts wird halb installiert).
- **Aktualisierung:** `dotnet-runtime-aktualisieren.timer` spielt nachts um 03:40 Patches ein und startet die .NET-Dienste nur bei einer
  neuen Runtime-Version neu. Sicherheitspatches von .NET brauchen damit kein neues Ausrollen mehr.
- **Prüfen:** Ausgabe von `dotnet --list-runtimes` am Ende; `systemctl list-timers dotnet-runtime-aktualisieren.timer`.
- **Rückweg:** `09-zurueck.sh` (Timer aus, Runtime bleibt); `09-zurueck.sh --entfernen` löscht sie nur, wenn keine App in `/opt` mehr
  framework-abhängig läuft – vorher die Apps mit `deploy.ps1 -MitRuntime` neu ausrollen.

---

## 10 · PostgreSQL (Umstellung von SQLite, 24.11.2026)

`ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/10-postgresql.sh"` – **vor** dem ersten Ausrollen einer Version mit PostgreSQL (auf liebler am 24.11.2026 noch als „Schritt 09“ gelaufen),
danach `.\deploy\server\deploy.ps1 -DbNeu`.

- Container `vibevhs-db` (`postgres:17`, `--restart unless-stopped`), nur `127.0.0.1:5441`, Daten im Docker-Volume `vibevhs-db`.
- Zufälliges Passwort in `/etc/vibevhs/postgres.env` (root, 0600); die Verbindungszeichenfolge trägt das Skript in `/etc/vibevhs/vibevhs.env` ein.
- `pg_dump`/`pg_restore` 17 auf dem Host (`postgresql-client-17`, notfalls aus apt.postgresql.org ⚑ prüfen), damit die Anwendung als
  Benutzer `vibevhs` ohne Docker-Rechte sichern kann; das Skript prüft das zum Schluss mit einer Probe-Sicherung.
- Beim ersten Start übernimmt die Anwendung die Kommentare aus `/var/lib/vibevhs/kommentare.db` in die Datenbank `vibevhs_kommentare`
  und benennt die Datei in `kommentare.db.uebernommen` um. Die alte `vibevhs.db` bleibt liegen (nur Testdaten) und kann später weg.
- Rückweg: `10-zurueck.sh` hält den Container an; eine ältere SQLite-Version mit `ausrollen.sh --zurueck` zurückholen.
