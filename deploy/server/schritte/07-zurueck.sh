#!/usr/bin/env bash
# Rückweg zu Schritt 07: Firewall auf dem Server abschalten (Regeln bleiben gespeichert, „ufw enable“ schaltet sie wieder ein).
source "$(dirname "$0")/gemeinsam.sh"
als_root
ufw disable
atrm $(atq | awk '{print $1}') 2>/dev/null || true
ok "ufw ist aus; es gilt nur noch die Hochschul-Firewall."
