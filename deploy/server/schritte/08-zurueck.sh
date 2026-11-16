#!/usr/bin/env bash
# Rückweg zu Schritt 08: /etc/letsencrypt und /etc/nodered/certs aus der Sicherung zurück, certbot-snap und Timer wieder an.
source "$(dirname "$0")/gemeinsam.sh"
als_root
letzte="$(ls -1d /root/vibevhs-sicherung/*-08-certbot-aufraeumen 2>/dev/null | tail -1)"
[[ -n "$letzte" && -f "$letzte/letsencrypt-und-nodered-certs.tar.gz" ]] || fehler "Keine Sicherung von Schritt 08 gefunden."
tar -xzf "$letzte/letsencrypt-und-nodered-certs.tar.gz" -C / && ok "Dateien zurück (aus $letzte)"
if [[ -f "$letzte/certbot-snap.txt" ]] && ! snap list certbot >/dev/null 2>&1; then snap install certbot --classic && ok "certbot-snap installiert"; fi
systemctl enable --now snap.certbot.renew.timer 2>/dev/null && ok "Timer wieder an" || true
