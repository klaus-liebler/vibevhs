# Erkundung liebler.iui.hs-osnabrueck.de – 16.11.2026

Alle Befehle, die von diesem Entwicklungsrechner aus den Server berührt haben, **wörtlich** und in zeitlicher Reihenfolge.
Nur lesend: kein `sudo`, keine Schreibzugriffe, keine Dienste neu gestartet. Anmeldung als `srvadmin` mit Schlüssel
(`ssh liebler`, BatchMode – bricht ab statt nach Passwort/Passphrase zu fragen). Ausgaben gekürzt; Geheimnisse
(Passwort-Hashes, Schlüssel, Tokens) kamen in den Ausgaben nicht vor bzw. wurden nicht abgefragt (adminAuth nur „gesetzt ja/nein“,
privater Schlüssel des Node-RED-Hooks herausgefiltert).

Ab jetzt wird jeder weitere Befehl **vor** der Ausführung hier eingetragen.

## 1 · 07:48 (MEZ) · Host-Schlüssel lesen (ohne Anmeldung)

```bash
ssh-keyscan -T 8 -t ed25519,ecdsa,rsa liebler.iui.hs-osnabrueck.de
ssh-keygen -lf keyscan.txt
```

```
# liebler.iui.hs-osnabrueck.de:22 SSH-2.0-OpenSSH_10.2p1 Ubuntu-2ubuntu3.8
3072 SHA256:EkK0c7hZFTd/bvy/LcZgrAJkIDSQnMzTGvuGS1rVHCo (RSA)
256  SHA256:Es80ZFNAUByLJLQ3McI9vbLR6loVR9aaMWktcjoPEvQ (ECDSA)
256  SHA256:ToSJngSfrZMY5dHJPFAAcIIa91NQG3PZu7vznb/XB40 (ED25519)
```

Außerdem ohne Anmeldung: DNS-Abfragen (A/AAAA/CAA), TCP-Verbindungstest auf 80/443/22/1880/8080, TLS-Handshake auf 443
(Zertifikat lesen), `GET https://liebler.iui.hs-osnabrueck.de/` (Server-Kopfzeile).

## 2 · 08:01 · Erste Anmeldung: funktioniert BatchMode, welche Quelladresse sieht der Server?

```powershell
ssh -o BatchMode=yes -o ConnectTimeout=8 liebler 'echo ANGEMELDET; echo "Quelle: $SSH_CLIENT"'
```

```
ANGEMELDET
Quelle: 2001:638:513:fe02::15e 48731 22
```

## 3 · 08:02 · Bestandsaufnahme Teil 1

```bash
ssh -o BatchMode=yes -o ConnectTimeout=10 liebler 'bash -s' < inventar.sh
```

Inhalt von `inventar.sh`:

```bash
# Nur lesend, ohne sudo: Bestandsaufnahme für das Deploy-Kit
echo "=== Verbindung"; echo "SSH_CONNECTION=$SSH_CONNECTION"
echo "=== System"; . /etc/os-release; echo "$PRETTY_NAME $(uname -r)"; date; timedatectl 2>/dev/null | grep -E 'Time zone|synchronized'
df -h / /var /home 2>/dev/null | sort -u; free -h | head -2; nproc
echo "=== Netz"; ip -br addr | grep -v '^lo'; ip -6 route | head -3; ip route | head -3
echo "=== Lauschende Ports"; ss -ltnp 2>/dev/null | awk 'NR==1 || /LISTEN/' | sed -E 's/users:.*//'
echo "=== ufw"; command -v ufw && (ufw status 2>&1 | head -3); systemctl is-active ufw 2>&1; systemctl is-enabled ufw 2>&1
echo "=== Node-RED"
systemctl list-units --type=service --all --no-legend 2>/dev/null | grep -iE 'red|node'
for u in $(systemctl list-units --type=service --all --no-legend 2>/dev/null | awk '{print $1}' | grep -iE 'node-?red'); do
  echo "--- $u"; systemctl show "$u" -p User -p Group -p ExecStart -p WorkingDirectory -p Environment -p ActiveState -p UnitFileState -p FragmentPath 2>/dev/null
done
ps -eo user,pid,args | grep -iE 'node-red|red\.js' | grep -v grep
command -v node-red && node-red --version 2>/dev/null | head -2; command -v node && node --version; command -v pm2 && pm2 list 2>&1 | head -5
for f in /home/*/.node-red/settings.js /root/.node-red/settings.js /var/lib/node-red/settings.js /opt/node-red/settings.js /usr/lib/node_modules/node-red/settings.js; do
  [ -e "$f" ] || continue; echo "--- $f ($(stat -c '%U %a' "$f" 2>/dev/null))"
  if [ -r "$f" ]; then grep -nE '^\s*(//\s*)?(uiHost|uiPort|httpAdminRoot|httpNodeRoot|httpRoot|httpStatic)\s*:' "$f"; grep -nE '^\s*adminAuth\s*:' "$f" >/dev/null && echo "adminAuth: gesetzt" || echo "adminAuth: nicht gesetzt (oder auskommentiert)"; else echo "(nicht lesbar ohne sudo)"; fi
done
ls -ld /home/*/.node-red 2>/dev/null
echo "=== Apache"
apache2 -v 2>/dev/null | head -1
for d in sites-enabled conf-enabled mods-enabled; do echo "--- $d"; ls /etc/apache2/$d 2>/dev/null | tr '\n' ' '; echo; done
for f in /etc/apache2/sites-enabled/*; do echo "--- $f"; [ -r "$f" ] && grep -vE '^\s*#|^\s*$' "$f" | grep -viE 'SSLCertificateKeyFile' || echo "(nicht lesbar)"; done
echo "--- DocumentRoots/Alias/Proxy in conf-enabled"; grep -hiE '^\s*(DocumentRoot|Alias|ScriptAlias|ProxyPass|ProxyPassReverse|RewriteRule|RewriteCond|Redirect|<Directory|<Location|WSGI|SetHandler)' /etc/apache2/conf-enabled/*.conf 2>/dev/null | sort -u | head -30
echo "=== Webinhalte"
for r in /var/www /var/www/*; do [ -d "$r" ] && { echo "--- $r"; ls -la "$r" 2>/dev/null | head -25; }; done
find /var/www -maxdepth 3 -type d 2>/dev/null | head -40
echo "--- Dateitypen unter /var/www"; find /var/www -type f 2>/dev/null | sed -E 's/.*\.([A-Za-z0-9]+)$/\1/;t;s/.*/(ohne)/' | sort | uniq -c | sort -rn | head -12
echo "=== HTTP-Antworten lokal"
for p in / /index.html /nodered/ /vibevhs/; do printf '%-14s ' "$p"; curl -sk -o /dev/null -w '%{http_code} %{size_download}B\n' "https://localhost$p"; done
curl -sk https://localhost/ | grep -oiE 'href="[^"#]+"' | sort -u | head -30
echo "=== certbot/nginx/php"; command -v certbot nginx php php-fpm 2>/dev/null; ls /etc/letsencrypt 2>/dev/null | head; dpkg -l 2>/dev/null | awk '/^ii/ && $2 ~ /^(php|libapache2-mod|nginx|certbot|python3-certbot|ufw|at|sqlite3|libicu)/ {print $2, $3}'
```

Ausgabe (gekürzt):

```
=== Verbindung
SSH_CONNECTION=2001:638:513:fe02::15e 48732 2001:638:513:f10::83ad:f13c 22
=== System
Ubuntu 26.04.1 LTS 7.0.0-43-generic
Mo 16. Nov 08:02:14 CET 2026
                Time zone: Europe/Berlin (CET, +0100)
System clock synchronized: yes
/dev/sda2        59G   14G   43G  25% /
Filesystem      Size  Used Avail Use% Mounted on
               total        used        free      shared  buff/cache   available
Mem:           3,8Gi       694Mi       1,2Gi       1,9Mi       2,1Gi       3,1Gi
2
=== Netz
ens160           UP             131.173.241.60/24 2001:638:513:f10::83ad:f13c/64 fe80::250:56ff:feb2:c9fa/64 
docker0          UP             172.17.0.1/16 fe80::6cf1:34ff:fe37:24c/64 
veth9f4e7ed@if2  UP             fe80::dc6b:e2ff:fe86:3de6/64 
2001:638:513:f10::/64 dev ens160 proto kernel metric 256 pref medium
fe80::/64 dev ens160 proto kernel metric 256 pref medium
fe80::/64 dev veth9f4e7ed proto kernel metric 256 pref medium
default via 131.173.241.254 dev ens160 proto static 
131.173.241.0/24 dev ens160 proto kernel scope link src 131.173.241.60 
172.17.0.0/16 dev docker0 proto kernel scope link src 172.17.0.1 
=== Lauschende Ports
State  Recv-Q Send-Q Local Address:Port Peer Address:PortProcess
LISTEN 0      100        127.0.0.1:1883      0.0.0.0:*          
LISTEN 0      4096      127.0.0.54:53        0.0.0.0:*          
LISTEN 0      4096         0.0.0.0:22        0.0.0.0:*          
LISTEN 0      100          0.0.0.0:8883      0.0.0.0:*          
LISTEN 0      4096         0.0.0.0:9443      0.0.0.0:*          
LISTEN 0      4096   127.0.0.53%lo:53        0.0.0.0:*          
LISTEN 0      5            0.0.0.0:5666      0.0.0.0:*          
LISTEN 0      1024         0.0.0.0:8181      0.0.0.0:*          
LISTEN 0      511          0.0.0.0:1880      0.0.0.0:*          
LISTEN 0      4096         0.0.0.0:8000      0.0.0.0:*          
LISTEN 0      511                *:80              *:*          
LISTEN 0      4096            [::]:22           [::]:*          
LISTEN 0      511                *:443             *:*          
LISTEN 0      100             [::]:8883         [::]:*          
LISTEN 0      4096            [::]:9443         [::]:*          
LISTEN 0      5               [::]:5666         [::]:*          
LISTEN 0      4096            [::]:8000         [::]:*          
=== ufw
/usr/sbin/ufw
ERROR: You need to be root to run this script
active
enabled
=== Node-RED
  initrd-parse-etc.service                    loaded    inactive dead    Mountpoints Configured in the Real Root
  kmod-static-nodes.service                   loaded    active   exited  Create List of Static Device Nodes
  nodered.service                             loaded    active   running Node-RED graphical event wiring tool
  systemd-tmpfiles-setup-dev-early.service    loaded    active   exited  Create Static Device Nodes in /dev gracefully
  systemd-tmpfiles-setup-dev.service          loaded    active   exited  Create Static Device Nodes in /dev
  systemd-udev-load-credentials.service       loaded    active   exited  Load udev Rules from Credentials
--- nodered.service
ActiveState=active
FragmentPath=/usr/lib/systemd/system/nodered.service
UnitFileState=enabled
ExecStart={ path=/usr/bin/env ; argv[]=/usr/bin/env node-red-pi $NODE_OPTIONS $NODE_RED_OPTIONS ; ignore_errors=no ; start_time=[n/a] ; stop_time=[n/a] ; pid=0 ; code=(null) ; status=0/0 }
Environment=NODE_OPTIONS=--max_old_space_size=2048
WorkingDirectory=/home/nodered/.node-red
User=nodered
Group=nodered
nodered     1218 node-red
/usr/bin/node-red
Node-RED v4.1.0
Node.js v22.22.1
/usr/bin/node
v22.22.1
--- /home/nodered/.node-red/settings.js (nodered 644)
144:    uiPort: process.env.PORT || 1880,
151:    //uiHost: "127.0.0.1",
168:    //httpAdminRoot: '/admin',
187:    //httpNodeRoot: '/red-nodes',
225:    //httpStatic: '/home/nol/node-red-static/', //single static source
231:    //httpStatic: [
adminAuth: gesetzt
--- /usr/lib/node_modules/node-red/settings.js (root 644)
146:    uiPort: process.env.PORT || 1880,
153:    //uiHost: "127.0.0.1",
170:    //httpAdminRoot: '/admin',
194:    //httpNodeRoot: '/red-nodes',
232:    //httpStatic: '/home/nol/node-red-static/', //single static source
241:    //httpStatic: [
adminAuth: nicht gesetzt (oder auskommentiert)
drwxr-xr-x 4 nodered nodered 4096 Jun  5 21:44 /home/nodered/.node-red
=== Apache
Server version: Apache/2.4.66 (Ubuntu)
--- sites-enabled
000-default-le-ssl.conf 000-default.conf 
--- conf-enabled
… (59 weitere Zeilen gekürzt)
```

## 4 · 08:04 · Bestandsaufnahme Teil 2 (Zertifikat-Verwaltung, Dienste, Inhalte, Firewall)

```bash
ssh -o BatchMode=yes -o ConnectTimeout=10 liebler 'bash -s' < inventar2.sh
```

Inhalt von `inventar2.sh`:

```bash
# Nur lesend, ohne sudo – Teil 2
echo "=== Zertifikat-Verwaltung"
ls -la /etc/letsencrypt /etc/letsencrypt/renewal /etc/letsencrypt/renewal-hooks/* 2>&1 | head -30
for f in /etc/letsencrypt/renewal/*.conf; do [ -r "$f" ] && { echo "--- $f"; grep -vE '^\s*$' "$f" | grep -viE 'account|server' ; }; done
ls -la /etc/letsencrypt/live 2>&1 | head
systemctl list-timers --all --no-pager 2>/dev/null | grep -iE 'certbot|letsencrypt|acme'
systemctl list-units --all --no-legend 2>/dev/null | grep -iE 'certbot|letsencrypt|acme'
ls /etc/cron.d 2>/dev/null; grep -rl -iE 'certbot|letsencrypt|acme' /etc/cron* 2>/dev/null
echo | openssl s_client -connect localhost:443 -servername liebler.iui.hs-osnabrueck.de 2>/dev/null | openssl x509 -noout -issuer -enddate
grep -rhiE 'SSLCertificate(Key|Chain)?File' /etc/apache2/sites-enabled /etc/apache2/mods-enabled /etc/apache2/conf-enabled 2>/dev/null | sed 's/^\s*//' | sort -u
grep -hiE '^\s*Include' /etc/apache2/sites-enabled/* 2>/dev/null
echo "=== Dienste"
systemctl list-units --type=service --state=running --no-legend 2>/dev/null | awk '{print $1}' | tr '\n' ' '; echo
id
docker ps --format '{{.Names}}\t{{.Image}}\t{{.Ports}}' 2>&1 | head -10
echo "=== Webinhalte Unterseiten"
find /var/www/html -type f -printf '%p %s B %TY-%Tm-%Td\n' 2>/dev/null
grep -oiE '(href|src)="[^"#]+"' /var/www/html/index.html | sort -u | head -40
echo "=== Mosquitto/sonstige Ports"
ls /etc/mosquitto/conf.d 2>/dev/null; grep -hE '^\s*(listener|port|certfile|keyfile|cafile)' /etc/mosquitto/mosquitto.conf /etc/mosquitto/conf.d/*.conf 2>/dev/null
ls /etc/nagios /etc/nagios-plugins 2>/dev/null | head -3
echo "=== ufw-Konfiguration (Dateien, lesbar?)"
ls -la /etc/ufw/ 2>&1 | head; grep -hE '^(### tuple|-A ufw6?-user-input)' /etc/ufw/user.rules /etc/ufw/user6.rules 2>&1 | head -40
grep -E '^(IPV6|DEFAULT_INPUT_POLICY)' /etc/default/ufw 2>/dev/null
echo "=== Anmeldungen (Quelladressen srvadmin, letzte)"
last -i -n 15 srvadmin 2>/dev/null | head -15
```

Ausgabe (gekürzt):

```
=== Zertifikat-Verwaltung
/etc/letsencrypt:
total 36
drwxr-xr-x   7 root root  4096 Nov 16 06:36 .
drwxr-xr-x 114 root root 12288 Nov 15 13:50 ..
drwx------   5 root root  4096 Sep 18  2025 accounts
drwx------   3 root root  4096 Sep 18  2025 archive
drwx------   3 root root  4096 Sep 18  2025 live
drwxr-xr-x   2 root root  4096 Nov 16 06:36 renewal
drwxr-xr-x   5 root root  4096 Sep 18  2025 renewal-hooks

/etc/letsencrypt/renewal:
total 12
drwxr-xr-x 2 root root 4096 Nov 16 06:36 .
drwxr-xr-x 7 root root 4096 Nov 16 06:36 ..
-rw-r--r-- 1 root root  662 Nov 16 06:36 liebler.iui.hs-osnabrueck.de.conf

/etc/letsencrypt/renewal-hooks/deploy:
total 12
drwxr-xr-x 2 root root 4096 Sep 18  2025 .
drwxr-xr-x 5 root root 4096 Sep 18  2025 ..
-rwxr-xr-x 1 root root  580 Sep 19  2025 nodered-cert-copy.sh

/etc/letsencrypt/renewal-hooks/post:
total 8
drwxr-xr-x 2 root root 4096 Sep 18  2025 .
drwxr-xr-x 5 root root 4096 Sep 18  2025 ..

/etc/letsencrypt/renewal-hooks/pre:
total 8
drwxr-xr-x 2 root root 4096 Sep 18  2025 .
--- /etc/letsencrypt/renewal/liebler.iui.hs-osnabrueck.de.conf
version = 5.7.0
archive_dir = /etc/letsencrypt/archive/liebler.iui.hs-osnabrueck.de
cert = /etc/letsencrypt/live/liebler.iui.hs-osnabrueck.de/cert.pem
privkey = /etc/letsencrypt/live/liebler.iui.hs-osnabrueck.de/privkey.pem
chain = /etc/letsencrypt/live/liebler.iui.hs-osnabrueck.de/chain.pem
fullchain = /etc/letsencrypt/live/liebler.iui.hs-osnabrueck.de/fullchain.pem
[renewalparams]
authenticator = webroot
key_type = ecdsa
webroot_path = /var/www/html,
[[webroot_map]]
[acme_renewal_info]
ari_retry_after = 2026-11-16T12:36:08
ls: cannot open directory '/etc/letsencrypt/live': Permission denied
Mon 2026-11-16 23:26:00 CET      15h Mon 2026-11-16 06:36:07 CET 1h 26min ago snap.certbot.renew.timer       snap.certbot.renew.service
  dev-disk-by\x2dloop\x2dref-\x5cx2fvar\x5cx2flib\x5cx2fsnapd\x5cx2fsnaps\x5cx2fcertbot_5911.snap.device                                    loaded    active   plugged   /dev/disk/by-loop-ref/\x2fvar\x2flib\x2fsnapd\x2fsnaps\x2fcertbot_5911.snap
  dev-disk-by\x2dloop\x2dref-\x5cx2fvar\x5cx2flib\x5cx2fsnapd\x5cx2fsnaps\x5cx2fcertbot_5940.snap.device                                    loaded    active   plugged   /dev/disk/by-loop-ref/\x2fvar\x2flib\x2fsnapd\x2fsnaps\x2fcertbot_5940.snap
  snap-certbot-5911.mount                                                                                                                   loaded    active   mounted   Mount unit for certbot, revision 5911
  snap-certbot-5940.mount                                                                                                                   loaded    active   mounted   Mount unit for certbot, revision 5940
  snap.certbot.renew.service                                                                                                                loaded    inactive dead      Service for snap application certbot.renew
  snap.certbot.renew.timer                                                                                                                  loaded    active   waiting   Timer renew for snap application certbot.renew
e2scrub_all
issuer=C=GR, O=Hellenic Academic and Research Institutions CA, CN=GEANT TLS ECC 1
notAfter=Feb  7 18:51:59 2027 GMT
=== Dienste
apache2.service cron.service dbus.service getty@tty1.service influxdb3.service mosquitto.service multipathd.service nagios-nrpe-server.service nodered.service open-vm-tools.service polkit.service rsyslog.service snap.docker.dockerd.service snapd.service ssh.service systemd-journald.service systemd-logind.service systemd-networkd.service systemd-resolved.service systemd-timedated.service systemd-timesyncd.service systemd-udevd.service unattended-upgrades.service user@1000.service vgauth.service 
uid=1000(srvadmin) gid=1000(srvadmin) groups=1000(srvadmin),4(adm),24(cdrom),27(sudo),30(dip),46(plugdev),116(lxd)
permission denied while trying to connect to the docker API at unix:///var/run/docker.sock
=== Webinhalte Unterseiten
/var/www/html/rgb/index.html 1228606 B 2026-05-28
/var/www/html/simplesorter/index.html 1002644 B 2026-10-29
/var/www/html/index.html 127388 B 2026-09-24
=== Mosquitto/sonstige Ports
README
default.conf
listener 1883 localhost
listener 8883
cafile /etc/mosquitto/ca_certificates/rootCA.pem.crt
certfile /etc/mosquitto/certs/publicserver.pem.crt
keyfile /etc/mosquitto/certs/publicserver.pem.prvtkey
/etc/nagios:
nrpe.cfg
nrpe.d
=== ufw-Konfiguration (Dateien, lesbar?)
total 64
drwxr-xr-x   3 root root  4096 Nov 15 13:42 .
drwxr-xr-x 114 root root 12288 Nov 15 13:50 ..
-rw-r-----   1 root root  1126 Apr 23  2020 after.init
-rw-r-----   1 root root  1004 Apr  2  2020 after.rules
-rw-r-----   1 root root   915 Apr  2  2020 after6.rules
drwxr-xr-x   3 root root  4096 Nov 15 13:42 applications.d
-rw-r-----   1 root root  1130 Apr 23  2020 before.init
-rw-r-----   1 root root  2537 Apr  2  2020 before.rules
-rw-r-----   1 root root  6700 Apr  2  2020 before6.rules
grep: /etc/ufw/user.rules: Permission denied
grep: /etc/ufw/user6.rules: Permission denied
IPV6=yes
DEFAULT_INPUT_POLICY="DROP"
… (1 weitere Zeilen gekürzt)
```

## 5 · 08:05 · Node-RED-HTTPS und certbot-Herkunft

```bash
ssh -o BatchMode=yes -o ConnectTimeout=10 liebler 'f=/home/nodered/.node-red/settings.js; grep -nE "^\s*(https|requireHttps|httpServerOptions)\s*:|^\s*(key|cert)\s*:|readFileSync" $f | head; sed -n "/^\s*https\s*:/,/^\s*}/p" $f | head -12; echo "--- hook"; cat /etc/letsencrypt/renewal-hooks/deploy/nodered-cert-copy.sh 2>&1 | grep -viE "privkey.*cat|BEGIN"; echo "--- 1880"; curl -sk -o /dev/null -w "https %{http_code}\n" https://127.0.0.1:1880/; curl -s -o /dev/null -w "http %{http_code}\n" http://127.0.0.1:1880/; echo "--- certbot snap"; ls -l /usr/bin/certbot /snap/bin/certbot 2>&1; snap list 2>/dev/null | grep -iE "certbot|docker"; echo "--- apache2-utils/at/sqlite3"; dpkg -l apache2-utils at sqlite3 nginx 2>/dev/null | awk "/^ii/{print \$2,\$3}"'
```

```
93:    https: {
94:      key: require("fs").readFileSync("/etc/nodered/certs/privkey.pem"),
95:      cert: require("fs").readFileSync("/etc/nodered/certs/fullchain.pem")
117:    requireHttps: true,
--- hook  (kopiert fullchain.pem/privkey.pem aus /etc/letsencrypt/live/… nach /etc/nodered/certs, startet nodered.service neu)
--- 1880
https 200
http 000
--- certbot snap
/usr/bin/certbot -> /snap/bin/certbot
certbot  5.10.0  5940  latest/stable  certbot-eff**  classic
docker   29.8.0  3618  latest/stable  canonical**    -
--- apache2-utils/at/sqlite3
apache2-utils 2.4.66-2ubuntu2.8
```

## Ergebnis (Kurzfassung)

- **Quelle im VPN:** Der Server sieht `2001:638:513:fe02::15e` (eduVPN, IPv6). Lokal hat der eduVPN-Adapter `172.22.193.94/21` und
  `2001:638:513:fe02::15e/64` → Vorschlag für SSH: `172.22.192.0/21` und `2001:638:513:fe02::/64`.
- **System:** Ubuntu 26.04.1, 2 CPU, 3,8 GiB RAM, 43 GB frei, Zeitzone Europe/Berlin (synchron).
- **Dienste:** apache2, nodered, mosquitto (1883 lokal, 8883 öffentlich mit TLS), influxdb3 (8181), nagios-nrpe (5666),
  Docker als snap (u. a. 9443, 8000), ssh. ufw **aktiv** (Standard DROP, Regeln ohne sudo nicht lesbar).
- **Apache:** nur `000-default` und `000-default-le-ssl`, DocumentRoot `/var/www/html` mit statischen Seiten
  (`index.html`, `rgb/index.html`, `simplesorter/index.html`), keine Alias/Proxy/PHP. Zertifikatspfad `/etc/letsencrypt/live/…`,
  ausgeliefert wird aber ein GEANT-Zertifikat (bis 07.02.2027).
- **certbot:** als snap, webroot `/var/www/html`, `snap.certbot.renew.timer` aktiv (zweimal täglich), deploy-Hook
  `nodered-cert-copy.sh` kopiert das Zertifikat für Node-RED.
- **Node-RED:** `nodered.service`, Benutzer `nodered`, v4.1.0, `/home/nodered/.node-red/settings.js`, HTTPS mit `requireHttps`,
  `adminAuth` gesetzt, `uiHost`/`httpAdminRoot`/`httpNodeRoot` noch nicht gesetzt.
