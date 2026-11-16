#!/usr/bin/env bash
# Schritt 04: Node-RED auf /nodered umstellen, nur noch lokal (127.0.0.1:1880) und ohne eigenes TLS – HTTPS macht nginx.
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
# Statt einzelne Zeilen per sed umzuschreiben (https: {…} ist mehrzeilig) wird am Dateiende ein markierter Block angehängt,
# der die Werte überschreibt. Idempotent über die Marke; Rückweg = Block entfernen bzw. 04-zurueck.sh.
marke_anf="// >>> liebler-kit Schritt 04: hinter nginx unter /nodered, nur lokal, ohne eigenes TLS"
marke_end="// <<< liebler-kit Schritt 04"
sed -i "\|^$marke_anf|,\|^$marke_end|d" "$datei"
cat >> "$datei" <<JS
$marke_anf
// nginx nimmt HTTPS mit dem Hochschulzertifikat an und leitet /nodered/ an http://127.0.0.1:1880 weiter.
Object.assign(module.exports, { uiHost: "127.0.0.1", httpAdminRoot: "/nodered", httpNodeRoot: "/nodered", requireHttps: false });
delete module.exports.https;
$marke_end
JS

info "Syntax und wirksame Werte prüfen"
werte=$(node -e "const s=require('$datei'); console.log(JSON.stringify({uiHost:s.uiHost,httpAdminRoot:s.httpAdminRoot,httpNodeRoot:s.httpNodeRoot,https:!!s.https,requireHttps:s.requireHttps,adminAuth:!!s.adminAuth}))") \
    || { warn "settings.js fehlerhaft – spiele Sicherung zurück"; cp -a "$SICHERUNG$datei" "$datei"; fehler "Abgebrochen"; }
echo "   $werte"
grep -q '"adminAuth":true' <<<"$werte" || warn "adminAuth ist NICHT gesetzt – der Editor wäre über /nodered/ ohne Anmeldung erreichbar!"

[[ -n "$dienst" ]] && systemctl restart "$dienst" && sleep 4
if curl -fsS -o /dev/null "http://127.0.0.1:1880/nodered/"; then ok "Node-RED antwortet lokal unter http://127.0.0.1:1880/nodered/"; else warn "Node-RED antwortet (noch) nicht unter http://127.0.0.1:1880/nodered/ – journalctl -u $dienst"; fi
curl -fsS -o /dev/null --resolve "$HOST:443:127.0.0.1" "https://$HOST/nodered/" && ok "über nginx erreichbar: https://$HOST/nodered/" || warn "über nginx noch nicht erreichbar"
if ss -ltn | grep -qE '(0\.0\.0\.0|\[::\]|\*):1880 '; then warn "Port 1880 lauscht noch auf allen Schnittstellen"; else ok "Port 1880 nur noch lokal"; fi
[[ -d /etc/nodered/certs ]] && info "/etc/nodered/certs wird nicht mehr gebraucht – Schritt 08 räumt es (nach Rückfrage) mit auf."
cat <<HINWEIS

Prüfen im Browser: https://$HOST/nodered/  (Editor, Anmeldung wie bisher über adminAuth).
ACHTUNG: HTTP-Endpunkte der Flows (und ein Dashboard) heißen jetzt https://$HOST/nodered/<pfad> statt https://$HOST:1880/<pfad>.
(Laut Klaus spricht von außen niemand Node-RED direkt an – nur MQTT über mosquitto.)
MQTT (mosquitto 8883) ist davon nicht betroffen.
HINWEIS
