#!/usr/bin/env bash
# Schritt 05: Let's-Encrypt-Zertifikat holen (HTTP-01 über Port 80) und nginx darauf umstellen; Erneuerung automatisch (certbot.timer).
# Voraussetzung: Schritt 03 (nginx läuft) und Port 80 ist von außen erreichbar (laut Klaus: ja).
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/05-zertifikat.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/05-zurueck.sh  (wieder das vorige Zertifikat)
source "$(dirname "$0")/gemeinsam.sh"
als_root
systemctl is-active --quiet nginx || fehler "nginx läuft nicht – erst Schritt 03."
live="/etc/letsencrypt/live/$HOST"

# Bestand 16.11.2026: certbot (snap) mit webroot /var/www/html und snap.certbot.renew.timer existiert seit 09/2025;
# ausgeliefert wurde aber ein GEANT-Zertifikat aus dem Let's-Encrypt-Pfad. Hier wird geklärt und auf Let's Encrypt gebracht.
certbot certificates 2>/dev/null | sed 's/^/   /' | grep -vE '^\s*$' | head -12
if [[ -f "$live/fullchain.pem" ]]; then
    aussteller=$(openssl x509 -issuer -noout -in "$live/fullchain.pem")
    info "Zertifikat im Let's-Encrypt-Pfad: $aussteller · bis $(openssl x509 -enddate -noout -in "$live/fullchain.pem" | cut -d= -f2)"
    if ! grep -qiE "Let's Encrypt|ISRG" <<<"$aussteller"; then
        warn "Im Let's-Encrypt-Pfad liegt KEIN Let's-Encrypt-Zertifikat (vermutlich das Hochschul-Zertifikat, von Hand hineinkopiert)."
        info "Probelauf (--dry-run, stellt nichts aus)"
        certbot renew --cert-name "$HOST" --dry-run || fehler "Probelauf fehlgeschlagen – Port 80 von außen und /.well-known/acme-challenge prüfen."
        if frage "Jetzt ein echtes Let's-Encrypt-Zertifikat holen (ersetzt das GEANT-Zertifikat im Pfad)?"; then
            certbot renew --cert-name "$HOST" --force-renewal
        fi
    fi
else
    info "Probelauf gegen die Test-Umgebung von Let's Encrypt (--dry-run, stellt nichts aus)"
    if ! certbot certonly --webroot -w /var/www/html -d "$HOST" --dry-run; then
        warn "Probelauf fehlgeschlagen. Häufigste Ursache: Port 80 ist von außen nicht erreichbar."
        warn "Prüfen ohne VPN (z. B. Handy im Mobilnetz): http://$HOST/.well-known/acme-challenge/test"
        fehler "Abgebrochen – nginx läuft unverändert mit dem bisherigen Zertifikat. Alternativen: Runbook Schritt 05."
    fi

    info "Echtes Zertifikat anfordern (certbot fragt nach E-Mail für Ablaufwarnungen und nach Zustimmung zu den Nutzungsbedingungen)"
    certbot certonly --webroot -w /var/www/html -d "$HOST" </dev/tty
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
systemctl list-timers --all --no-pager 2>/dev/null | grep -E 'certbot' | sed 's/^/   /' || warn "kein certbot-Timer gefunden"
ls /etc/letsencrypt/renewal-hooks/deploy | sed 's/^/   deploy-hook: /'
certbot renew --dry-run --quiet && ok "Erneuerung (Probelauf) klappt" || warn "certbot renew --dry-run meldet Fehler"
echo | openssl s_client -connect 127.0.0.1:443 -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer -enddate | sed 's/^/   /'
