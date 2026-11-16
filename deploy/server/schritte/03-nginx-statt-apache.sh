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
eigene=$(ls /etc/apache2/sites-enabled 2>/dev/null | grep -vE '^(000-default|default-ssl)(-le-ssl)?\.conf$' || true)
inhalte=$(find /var/www -mindepth 1 -maxdepth 2 -not -path '/var/www/html' -not -path '/var/www/html/index.html' -not -path '/var/www/letsencrypt*' 2>/dev/null | head -10 || true)
if [[ -n "$eigene$inhalte" ]] || ! grep -qi "Apache2 Ubuntu Default Page\|It works" /var/www/html/index.html 2>/dev/null; then
    warn "Apache liefert offenbar mehr als die Standardseite aus:"
    [[ -n "$eigene" ]] && echo "$eigene" | sed 's/^/     Site: /'
    [[ -n "$inhalte" ]] && echo "$inhalte" | sed 's/^/     Inhalt: /'
    warn "nginx übernimmt diese Inhalte NICHT automatisch (siehe Runbook, Schritt 03 „Bisherige Inhalte“)."
    frage "Trotzdem auf nginx umstellen?" || fehler "Abgebrochen – Apache läuft unverändert weiter."
else
    ok "Apache liefert nur die Standardseite aus"
fi

# ---------- 2. Zertifikat: vorerst das bisherige von Apache ----------
zert_snippet=/etc/nginx/snippets/liebler-zertifikat.conf
if [[ -f "$zert_snippet" ]]; then
    ok "Zertifikat-Snippet existiert schon: $(grep -oE '/[^ ;]+' "$zert_snippet" | head -1)"
else
    datei=$(grep -rhoE '^\s*SSLCertificateFile\s+\S+' /etc/apache2/sites-enabled /etc/apache2/conf-enabled 2>/dev/null | awk '{print $2}' | head -1)
    schluessel=$(grep -rhoE '^\s*SSLCertificateKeyFile\s+\S+' /etc/apache2/sites-enabled /etc/apache2/conf-enabled 2>/dev/null | awk '{print $2}' | head -1)
    kette=$(grep -rhoE '^\s*SSLCertificateChainFile\s+\S+' /etc/apache2/sites-enabled /etc/apache2/conf-enabled 2>/dev/null | awk '{print $2}' | head -1 || true)
    [[ -f "$datei" && -f "$schluessel" ]] || fehler "Zertifikat von Apache nicht gefunden (SSLCertificateFile=$datei). Bitte erst Schritt 05 (Let's Encrypt) oder Pfade manuell in $zert_snippet eintragen."
    voll="$datei"
    if [[ -n "$kette" && -f "$kette" ]]; then
        # nginx erwartet Zertifikat + Zwischenzertifikate in einer Datei
        install -d -m 0750 /etc/nginx/ssl
        cat "$datei" "$kette" > /etc/nginx/ssl/liebler-fullchain.pem
        voll=/etc/nginx/ssl/liebler-fullchain.pem
    fi
    printf '# Vorläufig: Zertifikat wie bisher bei Apache (Schritt 03). Schritt 05 ersetzt das durch Let'"'"'s Encrypt.\nssl_certificate %s;\nssl_certificate_key %s;\n' "$voll" "$schluessel" > "$zert_snippet"
    ok "Zertifikat übernommen: $voll · Ablauf: $(openssl x509 -enddate -noout -in "$datei" | cut -d= -f2)"
fi

# ---------- 3. nginx-Konfiguration ----------
einspielen nginx/liebler.conf /etc/nginx/sites-available/liebler.conf || true
einspielen nginx/vibevhs-proxy.conf /etc/nginx/snippets/vibevhs-proxy.conf || true
# Die Ausnahmen pflegt Klaus ggf. selbst: nur anlegen, nie überschreiben
[[ -f /etc/nginx/snippets/vibevhs-ohne-passwort.conf ]] || einspielen nginx/vibevhs-ohne-passwort.conf /etc/nginx/snippets/vibevhs-ohne-passwort.conf || true
ln -sf /etc/nginx/sites-available/liebler.conf /etc/nginx/sites-enabled/liebler.conf
rm -f /etc/nginx/sites-enabled/default
install -d -m 0755 /var/www/letsencrypt

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
ss -ltnp | grep -E ':(80|443) ' | sed 's/^/   /'
cat <<HINWEIS

Im Browser prüfen: https://$HOST/  (Übersicht), Zertifikat wie bisher.
Node-RED ist bis Schritt 04 weiter direkt unter :1880 erreichbar; /nodered/ funktioniert erst nach Schritt 04.
HINWEIS
