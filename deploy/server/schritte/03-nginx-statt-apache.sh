#!/usr/bin/env bash
# Schritt 03: nginx übernimmt 80/443 von Apache – zunächst mit dem vorhandenen Hochschul-Zertifikat (kein Ausfall beim Wechsel).
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/03-nginx-statt-apache.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/03-zurueck.sh  (nginx aus, Apache wieder an)
source "$(dirname "$0")/gemeinsam.sh"
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

# ---------- 2. Zertifikat: vorerst das bisherige von Apache ----------
zert_snippet=/etc/nginx/snippets/liebler-zertifikat.conf
if [[ -f "$zert_snippet" ]]; then
    ok "Zertifikat-Snippet existiert schon: $(grep -oE '/[^ ;]+' "$zert_snippet" | head -1)"
else
    datei=$(grep -hoE '^\s*SSLCertificateFile\s+\S+' /etc/apache2/sites-enabled/* /etc/apache2/conf-enabled/* 2>/dev/null | awk '{print $2}' | head -1)
    schluessel=$(grep -hoE '^\s*SSLCertificateKeyFile\s+\S+' /etc/apache2/sites-enabled/* /etc/apache2/conf-enabled/* 2>/dev/null | awk '{print $2}' | head -1)
    kette=$(grep -hoE '^\s*SSLCertificateChainFile\s+\S+' /etc/apache2/sites-enabled/* /etc/apache2/conf-enabled/* 2>/dev/null | awk '{print $2}' | head -1 || true)
    [[ -f "$datei" && -f "$schluessel" ]] || fehler "Zertifikat von Apache nicht gefunden (SSLCertificateFile=$datei). Bitte erst Schritt 05 (Let's Encrypt) oder Pfade manuell in $zert_snippet eintragen."
    voll="$datei"
    if [[ -n "$kette" && -f "$kette" ]]; then
        # nginx erwartet Zertifikat + Zwischenzertifikate in einer Datei
        install -d -m 0750 /etc/nginx/ssl
        cat "$datei" "$kette" > /etc/nginx/ssl/liebler-fullchain.pem
        voll=/etc/nginx/ssl/liebler-fullchain.pem
    fi
    printf '# Zertifikat wie bisher bei Apache (Schritt 03; die certbot-Erneuerung aktualisiert die Dateien, der Hook lädt nginx neu).\nssl_certificate %s;\nssl_certificate_key %s;\n' "$voll" "$schluessel" > "$zert_snippet"
    ok "Zertifikat übernommen: $voll · Ablauf: $(openssl x509 -enddate -noout -in "$datei" | cut -d= -f2)"
fi

# ---------- 3. nginx-Konfiguration ----------
einspielen nginx/liebler.conf /etc/nginx/sites-available/liebler.conf || true
einspielen nginx/vibevhs-proxy.conf /etc/nginx/snippets/vibevhs-proxy.conf || true
# Die Ausnahmen pflegt Klaus ggf. selbst: nur anlegen, nie überschreiben
[[ -f /etc/nginx/snippets/vibevhs-ohne-passwort.conf ]] || einspielen nginx/vibevhs-ohne-passwort.conf /etc/nginx/snippets/vibevhs-ohne-passwort.conf || true
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
# Erneuerung soll nach dem Wechsel nginx neu laden (zusätzlich zum vorhandenen Node-RED-Hook)
install -d /etc/letsencrypt/renewal-hooks/deploy
printf '#!/bin/sh\nsystemctl reload nginx\n' > /etc/letsencrypt/renewal-hooks/deploy/nginx-neu-laden.sh && chmod 755 /etc/letsencrypt/renewal-hooks/deploy/nginx-neu-laden.sh
ok "certbot-Hook: nginx wird nach jeder Erneuerung neu geladen"
ss -ltnp | grep -E ':(80|443) ' | sed 's/^/   /'
cat <<HINWEIS

Im Browser prüfen: https://$HOST/  (Übersicht), Zertifikat wie bisher.
Node-RED ist bis Schritt 04 weiter direkt unter :1880 erreichbar; /nodered/ funktioniert erst nach Schritt 04.
HINWEIS
