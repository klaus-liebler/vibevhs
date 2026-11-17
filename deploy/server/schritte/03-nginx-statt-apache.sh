#!/usr/bin/env bash
# Schritt 03: nginx übernimmt 80/443 von Apache – mit dem Hochschulzertifikat, das ab jetzt zentral in /etc/ssl/liebler liegt.
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/03-nginx-statt-apache.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/03-zurueck.sh  (nginx aus, Apache wieder an)
source "$(dirname "$0")/gemeinsam.sh"
source "$(dirname "$0")/liebler-nginx.sh"
als_root
command -v nginx >/dev/null || fehler "nginx fehlt – erst Schritt 02."

# ---------- 1. Was liefert Apache heute aus? ----------
info "Apache-Bestand (wird gesichert nach $SICHERUNG):"
sichern /etc/apache2 /etc/nginx
apache2ctl -S 2>&1 | sed 's/^/   /' | head -30 || true
echo "   aktive Sites: $(ls /etc/apache2/sites-enabled 2>/dev/null | tr '\n' ' ')"
echo "   aktive Module (Auswahl): $(apache2ctl -M 2>/dev/null | grep -oE '(proxy|rewrite|php|wsgi|cgi|dav|auth)[a-z_]*' | sort -u | tr '\n' ' ')"
# Erwartet (Bestandsaufnahme 16.11.2026): nur 000-default(-le-ssl), DocumentRoot /var/www/html mit statischen Seiten.
# nginx liefert /var/www/html genauso aus; anderes (weitere Sites, Alias, Proxy, PHP) würde nginx NICHT übernehmen.
eigene=$(ls /etc/apache2/sites-enabled 2>/dev/null | grep -vE '^(000-default|default-ssl)(-le-ssl)?\.conf$' || true)
besonderes=$(grep -hiE '^\s*(Alias|ProxyPass|RewriteRule|Redirect|DocumentRoot)\b' /etc/apache2/sites-enabled/* 2>/dev/null | grep -viE 'DocumentRoot\s+/var/www/html\s*$' || true)
fremd=$(find /var/www -mindepth 1 -maxdepth 1 -not -path /var/www/html 2>/dev/null || true)
php=$(find /var/www/html -name '*.php' 2>/dev/null | head -3 || true)
echo "   Inhalte unter /var/www/html (werden von nginx ausgeliefert): $(find /var/www/html -type f | wc -l) Dateien, $(ls /var/www/html | tr '\n' ' ')"
if [[ -n "$eigene$besonderes$fremd$php" ]]; then
    warn "Apache macht mehr als statische Seiten aus /var/www/html auszuliefern:"
    [[ -n "$eigene" ]] && echo "$eigene" | sed 's/^/     Site: /'
    [[ -n "$besonderes" ]] && echo "$besonderes" | sed 's/^/     Regel: /'
    [[ -n "$fremd" ]] && echo "$fremd" | sed 's/^/     Verzeichnis: /'
    [[ -n "$php" ]] && echo "$php" | sed 's/^/     PHP: /'
    warn "Das übernimmt nginx NICHT automatisch (siehe Runbook, Schritt 03 „Bisherige Inhalte“)."
    frage "Trotzdem auf nginx umstellen?" || fehler "Abgebrochen – Apache läuft unverändert weiter."
else
    ok "Apache liefert nur statische Seiten aus /var/www/html – nginx übernimmt sie unverändert"
fi

# ---------- 2. Zertifikat: Hochschulzertifikat zentral nach /etc/ssl/liebler ----------
# Heute liegt das GEANT-Zertifikat (von Hand) im Let's-Encrypt-Pfad, den Apache nutzt. Es wird geprüft und nach $ZERT_DIR kopiert;
# nginx (und nach Schritt 05 mosquitto) nutzen nur noch diesen Ort. /etc/letsencrypt bleibt bis Schritt 08 unangetastet.
if [[ -f "$ZERT_VOLL" && -f "$ZERT_KEY" ]]; then
    zert_pruefen "$ZERT_VOLL" "$ZERT_KEY"
else
    datei=$(grep -hoE '^\s*SSLCertificateFile\s+\S+' /etc/apache2/sites-enabled/* 2>/dev/null | awk '{print $2}' | head -1)
    schluessel=$(grep -hoE '^\s*SSLCertificateKeyFile\s+\S+' /etc/apache2/sites-enabled/* 2>/dev/null | awk '{print $2}' | head -1)
    kette=$(grep -hoE '^\s*SSLCertificateChainFile\s+\S+' /etc/apache2/sites-enabled/* 2>/dev/null | awk '{print $2}' | head -1 || true)
    [[ -f "$datei" && -f "$schluessel" ]] || fehler "Zertifikat von Apache nicht gefunden (SSLCertificateFile=$datei). Hochschulzertifikat bitte mit werkzeuge/zertifikat-tauschen.sh einspielen."
    info "Apache nutzt: $datei (→ $(readlink -f "$datei"))"
    tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
    cat "$datei" > "$tmp/fullchain.pem"
    # Kette anhängen, falls Apache sie getrennt führt und sie nicht schon in der Datei steckt
    if [[ -n "$kette" && -f "$kette" ]] && [[ $(grep -c 'BEGIN CERTIFICATE' "$datei") -lt 2 ]]; then cat "$kette" >> "$tmp/fullchain.pem"; fi
    cp "$schluessel" "$tmp/privkey.pem"
    aussteller=$(openssl x509 -noout -issuer -in "$tmp/fullchain.pem")
    grep -qiE "GEANT|HARICA|Hellenic|DFN|Sectigo" <<<"$aussteller" || { warn "Unerwarteter Aussteller: $aussteller"; frage "Dieses Zertifikat trotzdem als Hochschulzertifikat übernehmen?" || fehler "Abgebrochen – Apache läuft unverändert weiter."; }
    zert_ablegen "$tmp/fullchain.pem" "$tmp/privkey.pem" || true
    rm -rf "$tmp"; trap - EXIT
fi
zert_snippet=/etc/nginx/snippets/liebler-zertifikat.conf
neu="# Hochschulzertifikat (GEANT/HARICA), zentral in $ZERT_DIR – tauschen mit werkzeuge/zertifikat-tauschen.sh
ssl_certificate $ZERT_VOLL;
ssl_certificate_key $ZERT_KEY;"
if [[ "$(cat "$zert_snippet" 2>/dev/null)" != "$neu" ]]; then
    sichern "$zert_snippet"; install -d /etc/nginx/snippets; printf '%s\n' "$neu" > "$zert_snippet"; ok "nginx nutzt $ZERT_VOLL"
fi
# nginx-Worker (www-data) lesen den Schlüssel nicht selbst (das macht der root-Masterprozess) – keine Gruppenmitgliedschaft nötig.

# Tägliche Ablaufwarnung (ab 30 Tagen vor Ablauf ins Journal, optional per Mail – siehe /etc/default/zertifikat-ablauf)
einspielen werkzeuge/zertifikat-ablauf.sh /usr/local/sbin/zertifikat-ablauf 0755 || true
[[ -f /etc/default/zertifikat-ablauf ]] || install -m 0644 "$KIT/werkzeuge/zertifikat-ablauf.default" /etc/default/zertifikat-ablauf
geaendert=0
einspielen systemd/zertifikat-ablauf.service /etc/systemd/system/zertifikat-ablauf.service && geaendert=1 || true
einspielen systemd/zertifikat-ablauf.timer /etc/systemd/system/zertifikat-ablauf.timer && geaendert=1 || true
[[ $geaendert -eq 1 ]] && systemctl daemon-reload
systemctl enable --now zertifikat-ablauf.timer >/dev/null 2>&1 && ok "Ablaufwarnung aktiv: $(/usr/local/sbin/zertifikat-ablauf --kurz)"

# ---------- 3. nginx-Konfiguration ----------
# Grundgerüst liebler.conf + eigene Dateien in /etc/nginx/liebler.d/ (gemeinsames Schema, schritte/liebler-nginx.sh).
# Andere Apps (FraktionPerfekt, Podium …) pflegen ihre eigenen Dateien dort – sie bleiben unberührt; ältere include-Zeilen
# in liebler.conf werden nach liebler.d/ umgezogen. Erst die Snippets, die vibevhs.conf einbindet (nginx -t braucht sie).
einspielen nginx/vibevhs-proxy.conf /etc/nginx/snippets/vibevhs-proxy.conf || true
# Die Ausnahmen pflegt Klaus ggf. selbst: nur anlegen, nie überschreiben
[[ -f /etc/nginx/snippets/vibevhs-ohne-passwort.conf ]] || einspielen nginx/vibevhs-ohne-passwort.conf /etc/nginx/snippets/vibevhs-ohne-passwort.conf || true
liebler_grundgeruest "$KIT/nginx/liebler.conf" vibevhs="$KIT/nginx/liebler.d/vibevhs.conf" nodered="$KIT/nginx/liebler.d/nodered.conf"
ln -sf /etc/nginx/sites-available/liebler.conf /etc/nginx/sites-enabled/liebler.conf
rm -f /etc/nginx/sites-enabled/default

if [[ ! -s /etc/nginx/vibevhs.htpasswd ]]; then
    info "Gemeinsames Zugangspasswort für /vibevhs anlegen (Benutzername für alle Tester: vhs-test)"
    htpasswd -c -B /etc/nginx/vibevhs.htpasswd vhs-test </dev/tty
    chown root:www-data /etc/nginx/vibevhs.htpasswd && chmod 640 /etc/nginx/vibevhs.htpasswd
fi

info "nginx -t"
nginx -t

# ---------- 4. Umschalten ----------
if systemctl is-active --quiet nginx; then
    systemctl reload nginx && ok "nginx neu geladen"
else
    info "Apache stoppen, nginx starten …"
    systemctl stop apache2
    if ! systemctl start nginx; then
        warn "nginx startet nicht – Apache wieder an"
        journalctl -u nginx -n 20 --no-pager | sed 's/^/   /'
        systemctl start apache2
        fehler "Abgebrochen, Apache läuft wieder."
    fi
    systemctl disable apache2 >/dev/null 2>&1 || true
    systemctl enable nginx >/dev/null 2>&1
    ok "nginx läuft auf 80/443, Apache gestoppt und deaktiviert (nicht deinstalliert)"
fi

# ---------- 5. Prüfen ----------
curl -fsS -o /dev/null -w '   https://%{url.host}/ → HTTP %{http_code}\n' --resolve "$HOST:443:127.0.0.1" "https://$HOST/" || warn "HTTPS antwortet nicht"
curl -sS -o /dev/null -w '   http://%{url.host}/  → HTTP %{http_code} (erwartet 301)\n' --resolve "$HOST:80:127.0.0.1" "http://$HOST/" || true
curl -sS -o /dev/null -w '   /vibevhs/ → HTTP %{http_code} (erwartet 401 ohne Passwort; 502 bis Schritt 06)\n' --resolve "$HOST:443:127.0.0.1" "https://$HOST/vibevhs/" || true
for s in /rgb/ /simplesorter/; do curl -sS -o /dev/null -w "   $s → HTTP %{http_code}\n" --resolve "$HOST:443:127.0.0.1" "https://$HOST$s" || true; done
ss -ltnp | grep -E ':(80|443) ' | sed 's/^/   /'
cat <<HINWEIS

Im Browser prüfen: https://$HOST/  (bisherige Seiten), Zertifikat wie bisher (GEANT).
Node-RED ist bis Schritt 04 weiter direkt unter :1880 erreichbar; /nodered/ funktioniert erst nach Schritt 04.
HINWEIS
