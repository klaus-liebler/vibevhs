#!/usr/bin/env bash
# Rückweg zu Schritt 03: nginx stoppen, Apache wieder starten (Konfiguration von Apache ist unverändert).
source "$(dirname "$0")/gemeinsam.sh"
als_root
systemctl stop nginx 2>/dev/null || true
systemctl disable nginx >/dev/null 2>&1 || true
systemctl enable apache2 >/dev/null 2>&1 || true
systemctl start apache2
systemctl is-active --quiet apache2 && ok "Apache läuft wieder auf 80/443" || fehler "Apache startet nicht: journalctl -u apache2"
warn "Node-RED: falls Schritt 04 schon lief, auch 04-zurueck.sh ausführen (sonst nur lokal erreichbar)."
