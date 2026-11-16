#!/usr/bin/env bash
# Rückweg zu Schritt 01: SSH-Härtung entfernen (Passwort-Anmeldung wieder wie vorher).
source "$(dirname "$0")/gemeinsam.sh"
als_root
if [[ -f /etc/ssh/sshd_config.d/10-haertung.conf ]]; then
    sichern /etc/ssh/sshd_config.d/10-haertung.conf
    rm -f /etc/ssh/sshd_config.d/10-haertung.conf
fi

sshd -t && (systemctl reload ssh 2>/dev/null || systemctl restart ssh)
ok "SSH-Härtung entfernt; es gilt wieder die vorherige Konfiguration."
