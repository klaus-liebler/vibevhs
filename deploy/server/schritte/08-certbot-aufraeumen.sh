#!/usr/bin/env bash
# Schritt 08 (später, nach Rückfrage): certbot/Let's Encrypt entfernen – das Hochschulzertifikat liegt in /etc/ssl/liebler.
# Entfernt nach Einzelrückfrage: snap.certbot.renew.timer, den certbot-snap, /etc/letsencrypt (inkl. Hook nodered-cert-copy.sh)
# und /etc/nodered/certs. Vorher wird alles komplett als tar gesichert.
# Start: ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/08-certbot-aufraeumen.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/08-zurueck.sh  (Dateien aus dem tar zurück, certbot-snap neu installieren)
source "$(dirname "$0")/gemeinsam.sh"
als_root

# ---------- Voraussetzungen: nichts nutzt mehr /etc/letsencrypt oder /etc/nodered/certs ----------
[[ -f "$ZERT_VOLL" ]] || fehler "$ZERT_VOLL fehlt – erst Schritt 03."
nutzer=$(grep -rlsE '/etc/letsencrypt|/etc/nodered/certs' /etc/nginx /etc/mosquitto /etc/apache2/sites-enabled /home/*/.node-red/settings.js 2>/dev/null || true)
if [[ -n "$nutzer" ]]; then
    warn "Diese Dateien verweisen noch auf /etc/letsencrypt bzw. /etc/nodered/certs:"; echo "$nutzer" | sed 's/^/   /'
    grep -qs 'liebler-kit Schritt 04' /home/*/.node-red/settings.js && info "(settings.js: der Verweis ist durch den Block aus Schritt 04 überschrieben – unkritisch, sofern nur dort)"
    frage "Trotzdem aufräumen?" || fehler "Abgebrochen."
fi
systemctl is-active --quiet apache2 && fehler "Apache läuft noch (nutzt /etc/letsencrypt) – erst Schritt 03."
echo | openssl s_client -connect 127.0.0.1:443 -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer | grep -qiE 'GEANT|HARICA|Hellenic' \
    && ok "nginx liefert das Hochschulzertifikat aus" || fehler "nginx liefert nicht das Hochschulzertifikat – nicht aufräumen."

# ---------- Sicherung ----------
mkdir -p "$SICHERUNG"
tar -czf "$SICHERUNG/letsencrypt-und-nodered-certs.tar.gz" -C / $(cd / && ls -d etc/letsencrypt etc/nodered/certs 2>/dev/null) 2>/dev/null \
    && ok "gesichert: $SICHERUNG/letsencrypt-und-nodered-certs.tar.gz ($(du -h "$SICHERUNG/letsencrypt-und-nodered-certs.tar.gz" | cut -f1))"
snap list certbot >/dev/null 2>&1 && snap list certbot > "$SICHERUNG/certbot-snap.txt"
systemctl list-timers --all --no-pager 2>/dev/null | grep -i certbot > "$SICHERUNG/certbot-timer.txt" || true

# ---------- Aufräumen, einzeln bestätigt ----------
if systemctl list-unit-files 2>/dev/null | grep -q '^snap.certbot.renew.timer' && frage "Erneuerungs-Timer snap.certbot.renew.timer abschalten?"; then
    systemctl disable --now snap.certbot.renew.timer && ok "Timer abgeschaltet"
fi
if snap list certbot >/dev/null 2>&1 && frage "certbot-snap entfernen (snap remove certbot)?"; then
    snap remove certbot && ok "certbot entfernt"
fi
if [[ -d /etc/letsencrypt ]] && frage "/etc/letsencrypt löschen (Sicherung liegt in $SICHERUNG)?"; then
    rm -rf /etc/letsencrypt && ok "/etc/letsencrypt gelöscht"
fi
if [[ -d /etc/nodered/certs ]] && frage "/etc/nodered/certs löschen (Node-RED braucht kein eigenes Zertifikat mehr)?"; then
    rm -rf /etc/nodered/certs && ok "/etc/nodered/certs gelöscht"
fi

info "Prüfung"
for u in https://$HOST/ https://$HOST/nodered/; do curl -sS -o /dev/null -w "   $u → HTTP %{http_code}\n" --resolve "$HOST:443:127.0.0.1" "$u" || warn "$u antwortet nicht"; done
/usr/local/sbin/zertifikat-ablauf --kurz 2>/dev/null | sed 's/^/   Hochschulzertifikat: /' || true
ok "Schritt 08 fertig – Let's Encrypt kommt auf dem Server nicht mehr vor."
