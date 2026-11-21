#!/usr/bin/env bash
# Rückweg zu Schritt 04: letzte gesicherte Node-RED-settings.js zurückspielen und Node-RED neu starten.
source "$(dirname "$0")/gemeinsam.sh"
als_root
letzte="$(ls -1d /root/vibevhs-sicherung/*-04-nodered-lokal 2>/dev/null | tail -1)"
[[ -n "$letzte" ]] || fehler "Keine Sicherung von Schritt 04 gefunden."
datei="$(cd "$letzte" && find . -name settings.js | head -1 | sed 's|^\.||')"
[[ -n "$datei" ]] || fehler "In $letzte liegt keine settings.js."
cp -a "$letzte$datei" "$datei"
ok "zurückgespielt: $datei (aus $letzte)"
dienst="$(systemctl list-units --type=service --all --no-legend | awk '{print $1}' | grep -iE '^node-?red' | head -1 || true)"
[[ -n "$dienst" ]] && systemctl restart "$dienst" && ok "$dienst neu gestartet"
