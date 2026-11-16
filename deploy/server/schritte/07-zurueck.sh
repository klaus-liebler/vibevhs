#!/usr/bin/env bash
# Rückweg zu Schritt 07: vorigen Firewall-Stand (Sicherung aus Schritt 07) wiederherstellen. ufw bleibt dabei aktiv wie vorher.
source "$(dirname "$0")/gemeinsam.sh"
als_root
letzte="$(ls -1d /root/vibevhs-sicherung/*-07-firewall 2>/dev/null | tail -1)"
[[ -n "$letzte" && -d "$letzte/etc/ufw" ]] || fehler "Keine Sicherung von Schritt 07 gefunden."
cp -a "$letzte/etc/ufw/." /etc/ufw/
ufw reload
atrm $(atq | awk '{print $1}') 2>/dev/null || true
ok "Firewall-Regeln wie vor Schritt 07 (aus $letzte)."
ufw status numbered | sed 's/^/   /'
