#!/usr/bin/env bash
# Schritt 05: Let's-Encrypt-Zertifikat holen (HTTP-01 über Port 80) und nginx darauf umstellen; Erneuerung automatisch (certbot.timer).
# Voraussetzung: Schritt 03 (nginx läuft) und Port 80 ist VON AUSSEN erreichbar (sonst Runbook Schritt 05 „Ohne Port 80“).
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/05-zertifikat.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/05-zurueck.sh  (wieder das vorige Zertifikat)
source "$(dirname "$0")/gemeinsam.sh"
als_root
systemctl is-active --quiet nginx || fehler "nginx läuft nicht – erst Schritt 03."
live="/etc/letsencrypt/live/$HOST"

if [[ -f "$live/fullchain.pem" ]]; then
    ok "Let's-Encrypt-Zertifikat vorhanden, gültig bis $(openssl x509 -enddate -noout -in "$live/fullchain.pem" | cut -d= -f2)"
else
    info "Probelauf gegen die Test-Umgebung von Let's Encrypt (--dry-run, stellt nichts aus)"
    if ! certbot certonly --webroot -w /var/www/letsencrypt -d "$HOST" --dry-run; then
        warn "Probelauf fehlgeschlagen. Häufigste Ursache: Port 80 ist von außen nicht erreichbar."
        warn "Prüfen ohne VPN (z. B. Handy im Mobilnetz): http://$HOST/.well-known/acme-challenge/test"
        fehler "Abgebrochen – nginx läuft unverändert mit dem bisherigen Zertifikat. Alternativen: Runbook Schritt 05."
    fi

    info "Echtes Zertifikat anfordern (certbot fragt nach E-Mail für Ablaufwarnungen und nach Zustimmung zu den Nutzungsbedingungen)"
    certbot certonly --webroot -w /var/www/letsencrypt -d "$HOST" --deploy-hook "systemctl reload nginx" </dev/tty
fi

zert_snippet=/etc/nginx/snippets/liebler-zertifikat.conf
neu="# Let's Encrypt (Schritt 05); Erneuerung durch certbot.timer, danach lädt der deploy-hook nginx neu.
ssl_certificate $live/fullchain.pem;
ssl_certificate_key $live/privkey.pem;"
if [[ "$(cat "$zert_snippet" 2>/dev/null)" == "$neu" ]]; then
    ok "nginx nutzt schon Let's Encrypt"
else
    sichern "$zert_snippet"
    printf '%s\n' "$neu" > "$zert_snippet"
    nginx -t || { cp -a "$SICHERUNG$zert_snippet" "$zert_snippet"; fehler "nginx -t fehlgeschlagen – vorheriges Zertifikat wiederhergestellt"; }
    systemctl reload nginx
    ok "nginx auf Let's Encrypt umgestellt"
fi

# Erneuerungs-Hook auch für ältere Zertifikate sicherstellen
install -d /etc/letsencrypt/renewal-hooks/deploy
printf '#!/bin/sh\nsystemctl reload nginx\n' > /etc/letsencrypt/renewal-hooks/deploy/nginx-neu-laden.sh
chmod 755 /etc/letsencrypt/renewal-hooks/deploy/nginx-neu-laden.sh

info "Prüfung"
systemctl enable --now certbot.timer >/dev/null 2>&1 || true
systemctl list-timers certbot.timer --no-pager | sed 's/^/   /' | head -3
certbot renew --dry-run --quiet && ok "Erneuerung (Probelauf) klappt" || warn "certbot renew --dry-run meldet Fehler"
echo | openssl s_client -connect 127.0.0.1:443 -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer -enddate | sed 's/^/   /'
