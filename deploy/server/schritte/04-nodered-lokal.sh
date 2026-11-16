#!/usr/bin/env bash
# Schritt 04: Node-RED auf /nodered umstellen und nur noch lokal (127.0.0.1:1880) lauschen lassen.
# Erst NACH Schritt 03 (nginx) ausführen – sonst ist Node-RED zwischendurch nicht erreichbar.
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/04-nodered-lokal.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/04-zurueck.sh  (spielt die gesicherte settings.js zurück)
source "$(dirname "$0")/gemeinsam.sh"
als_root

# settings.js finden (Benutzerinstallation oder Dienst)
mapfile -t kandidaten < <(ls -1 /home/*/.node-red/settings.js /root/.node-red/settings.js /var/lib/node-red/settings.js /opt/node-red/settings.js 2>/dev/null || true)
[[ ${#kandidaten[@]} -gt 0 ]] || fehler "Keine settings.js gefunden. Bitte Pfad nennen: NODERED_SETTINGS=/pfad/settings.js sudo -E bash $0"
datei="${NODERED_SETTINGS:-${kandidaten[0]}}"
[[ ${#kandidaten[@]} -gt 1 && -z "${NODERED_SETTINGS:-}" ]] && warn "Mehrere settings.js gefunden (${kandidaten[*]}), nehme $datei"
info "Node-RED-Einstellungen: $datei"

dienst="$(systemctl list-units --type=service --all --no-legend | awk '{print $1}' | grep -iE '^node-?red' | head -1 || true)"
[[ -n "$dienst" ]] && info "Dienst: $dienst" || warn "Kein Node-RED-systemd-Dienst gefunden (pm2/Handstart?) – Neustart dann bitte selbst."

sichern "$datei"
setze() { # setze <schlüssel> <wert-js>
    local k="$1" v="$2"
    if grep -qE "^\s*$k\s*:" "$datei"; then
        sed -i -E "s|^(\s*)$k\s*:.*$|\1$k: $v,|" "$datei"
    elif grep -qE "^\s*//\s*$k\s*:" "$datei"; then
        sed -i -E "0,/^(\s*)\/\/\s*$k\s*:.*$/s||\1$k: $v,|" "$datei"
    else
        sed -i -E "0,/module\.exports\s*=\s*\{/s||module.exports = {\n    $k: $v,|" "$datei"
    fi
}
setze uiHost '"127.0.0.1"'
setze httpAdminRoot '"/nodered"'
setze httpNodeRoot '"/nodered"'

info "Syntax prüfen"
node -e "require('$datei')" || { warn "settings.js fehlerhaft – spiele Sicherung zurück"; cp -a "$SICHERUNG$datei" "$datei"; fehler "Abgebrochen"; }
grep -nE '^\s*(uiHost|httpAdminRoot|httpNodeRoot)\s*:' "$datei" | sed 's/^/   /'

[[ -n "$dienst" ]] && systemctl restart "$dienst" && sleep 3
if curl -fsS -o /dev/null "http://127.0.0.1:1880/nodered/"; then ok "Node-RED antwortet lokal unter /nodered/"; else warn "Node-RED antwortet (noch) nicht unter http://127.0.0.1:1880/nodered/"; fi
if ss -ltn | grep -qE '(0\.0\.0\.0|\[::\]|\*):1880 '; then warn "Port 1880 lauscht noch auf allen Schnittstellen"; else ok "Port 1880 nur noch lokal"; fi
cat <<HINWEIS

Prüfen im Browser: https://$HOST/nodered/  (Editor) – Flows mit HTTP-Endpunkten sind jetzt unter /nodered/<pfad> erreichbar.
Empfehlung: adminAuth für den Editor setzen (siehe $KIT/nodered/settings-ausschnitt.js).
HINWEIS
