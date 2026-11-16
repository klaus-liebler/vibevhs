#!/usr/bin/env bash
# Ablaufwarnung für das Hochschulzertifikat (täglich per zertifikat-ablauf.timer). Ziel: /usr/local/sbin/zertifikat-ablauf
# Schreibt ins Journal (journalctl -t zertifikat-ablauf); ab WARNTAGE vor Ablauf als Warnung und – wenn MAIL_AN gesetzt und
# ein Mailprogramm vorhanden ist – zusätzlich per Mail. Einstellungen: /etc/default/zertifikat-ablauf
# Aufruf von Hand: zertifikat-ablauf          (Prüfung wie der Timer)
#                  zertifikat-ablauf --kurz   (nur eine Zeile, z. B. „noch 83 Tage (bis 07.02.2027)“)
set -euo pipefail
DATEIEN="/etc/ssl/liebler/fullchain.pem"
WARNTAGE=30
MAIL_AN=""
[[ -r /etc/default/zertifikat-ablauf ]] && . /etc/default/zertifikat-ablauf

rc=0
for f in $DATEIEN; do
    if [[ ! -r "$f" ]]; then logger -t zertifikat-ablauf -p user.err "Zertifikat fehlt: $f"; echo "fehlt: $f"; rc=2; continue; fi
    ende=$(openssl x509 -noout -enddate -in "$f" | cut -d= -f2)
    tage=$(( ( $(date -d "$ende" +%s) - $(date +%s) ) / 86400 ))
    text="$f: noch $tage Tage (bis $(date -d "$ende" +%d.%m.%Y))"
    if [[ "${1:-}" == "--kurz" ]]; then echo "noch $tage Tage (bis $(date -d "$ende" +%d.%m.%Y))"; continue; fi
    if (( tage < WARNTAGE )); then
        logger -t zertifikat-ablauf -p user.warning "Zertifikat läuft bald ab – $text. Tauschen: sudo bash /tmp/vibevhs-kit/werkzeuge/zertifikat-tauschen.sh <fullchain> <schlüssel>"
        echo "WARNUNG $text"
        if [[ -n "$MAIL_AN" ]]; then
            betreff="Zertifikat $(hostname -f) läuft in $tage Tagen ab"
            if command -v mail >/dev/null; then echo "$text" | mail -s "$betreff" "$MAIL_AN"
            elif command -v sendmail >/dev/null; then printf 'To: %s\nSubject: %s\n\n%s\n' "$MAIL_AN" "$betreff" "$text" | sendmail -t
            else logger -t zertifikat-ablauf -p user.notice "MAIL_AN gesetzt, aber kein Mailprogramm (mail/sendmail) installiert"; fi
        fi
        rc=1
    else
        logger -t zertifikat-ablauf -p user.info "$text"
        echo "ok $text"
    fi
done
exit $rc
