#!/usr/bin/env bash
# Rückweg zu Schritt 05: mosquitto-Konfiguration von vor Schritt 05 (eigenes Zertifikat) wiederherstellen; Gruppe ssl-cert wieder entfernen.
source "$(dirname "$0")/gemeinsam.sh"
als_root
letzte="$(ls -1d /root/vibevhs-sicherung/*-05-mosquitto-zertifikat 2>/dev/null | tail -1)"
[[ -n "$letzte" && -d "$letzte/etc/mosquitto" ]] || fehler "Keine Sicherung von Schritt 05 gefunden."
cp -a "$letzte/etc/mosquitto/." /etc/mosquitto/
gpasswd -d mosquitto ssl-cert >/dev/null 2>&1 || true
systemctl restart mosquitto
systemctl is-active --quiet mosquitto && ok "mosquitto läuft wieder mit der vorigen Konfiguration (aus $letzte)" || fehler "mosquitto startet nicht: journalctl -u mosquitto"
echo | openssl s_client -connect 127.0.0.1:8883 2>/dev/null | openssl x509 -noout -subject -issuer | sed 's/^/   /'
