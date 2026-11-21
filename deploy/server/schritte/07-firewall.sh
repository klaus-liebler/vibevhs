#!/usr/bin/env bash
# Schritt 07: Firewall (ufw) – 443 und 80 frei, SSH nur aus dem VPN, 1880 zu; 8883/8181/5666 (und Docker 9443/8000) bleiben wie bisher. ufw ist auf diesem Server SCHON AKTIV
# (Standard DROP, eigene Regeln z. B. für MQTT 8883): Es wird nichts zurückgesetzt, nur ergänzt; Löschen nur nach Rückfrage.
# Start (eduVPN der Hochschule, aus Klaus' Verbindung ermittelt):
#   ssh -t liebler "sudo VPN_NETZ=172.22.192.0/21 VPN_NETZ6=2001:638:513:fe02::/64 bash /tmp/vibevhs-kit/schritte/07-firewall.sh"
# Rettungsanker: nach 10 Minuten kommt der vorige Firewall-Stand automatisch zurück, wenn der at-Auftrag nicht entfernt wird.
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/07-zurueck.sh
source "$(dirname "$0")/gemeinsam.sh"
als_root
[[ -n "${VPN_NETZ:-}" ]] || fehler "VPN_NETZ fehlt (z. B. VPN_NETZ=131.173.x.0/24). Ohne ihn würde SSH von überall gesperrt."
[[ "$VPN_NETZ" =~ ^[0-9.]+/[0-9]+$ ]] || fehler "VPN_NETZ sieht nicht wie ein IPv4-Netz aus: $VPN_NETZ"

# Kommt die aktuelle Sitzung aus dem VPN-Netz? (sonst würde sie gleich gesperrt)
quelle="${SSH_CLIENT%% *}"
info "Diese Sitzung kommt von: ${quelle:-unbekannt}"
python3 - "$quelle" "$VPN_NETZ" "${VPN_NETZ6:-}" <<'PY' || { warn "Die aktuelle Verbindung liegt NICHT im angegebenen VPN-Netz."; frage "Trotzdem fortfahren?" || fehler "Abgebrochen."; }
import ipaddress, sys
q = sys.argv[1]
netze = [n for n in sys.argv[2:] if n]
sys.exit(0 if q and any(ipaddress.ip_address(q) in ipaddress.ip_network(n, strict=False) for n in netze) else 1)
PY

sichern /etc/ufw
info "Bisherige Regeln:"
ufw status numbered | sed 's/^/   /'

# Rettungsanker VOR jeder Änderung: der gesicherte Stand wird in 10 Minuten automatisch wiederhergestellt
command -v at >/dev/null || fehler "at fehlt – erst Schritt 02."
echo "cp -a $SICHERUNG/etc/ufw/. /etc/ufw/ && ufw reload" | at now + 10 minutes 2>/dev/null \
    && warn "Rettungsanker: in 10 Minuten wird der vorige Firewall-Stand wiederhergestellt (atq zeigt den Auftrag)."

# Ergänzen (ufw überspringt vorhandene gleiche Regeln)
ufw allow 443/tcp comment 'HTTPS nginx'
ufw allow 80/tcp comment 'HTTP: Umleitung auf HTTPS'
ufw allow from "$VPN_NETZ" to any port 22 proto tcp comment 'SSH nur aus dem VPN'
[[ -n "${VPN_NETZ6:-}" ]] && ufw allow from "$VPN_NETZ6" to any port 22 proto tcp comment 'SSH nur aus dem VPN (IPv6)'

# Regeln, die SSH von überall bzw. Node-RED 1880 öffnen: einzeln nachfragen und löschen (von hinten, damit die Nummern stimmen)
mapfile -t weg < <(ufw status numbered | grep -E '^\[ *[0-9]+\]' | grep -E '(^|\s)(22|22/tcp|OpenSSH|1880|1880/tcp)(\s|\(v6\))' | grep -E 'Anywhere\s*(\(v6\))?\s*$|Anywhere \(v6\)\s*$' || true)
for ((i=${#weg[@]}-1; i>=0; i--)); do
    nr=$(sed -E 's/^\[ *([0-9]+)\].*/\1/' <<<"${weg[$i]}")
    if frage "Regel löschen? ${weg[$i]}"; then ufw --force delete "$nr"; fi
done

# Weitere Dienste bleiben wie bisher erreichbar (Klaus, 16.11.2026): nichts löschen; fehlt eine Regel, nur nach Rückfrage ergänzen.
# Docker (snap) veröffentlicht 9443/8000 an ufw vorbei – dort ist keine ufw-Regel nötig.
status="$(ufw status)"
for port in 8883 8181 5666; do
    if grep -qE "^$port(/tcp)?\s" <<<"$status"; then ok "Port $port: Regel vorhanden (bleibt unverändert)"
    elif frage "Port $port hat keine ufw-Regel. 'ufw allow $port/tcp' ergänzen (dann von überall erreichbar)?"; then ufw allow "$port/tcp" comment 'wie bisher erreichbar'
    else warn "Port $port bleibt ohne eigene Regel"; fi
done

ufw reload
ufw status verbose | sed 's/^/   /'
cat <<HINWEIS

JETZT in einem NEUEN Fenster (aus dem VPN) prüfen:  ssh srvadmin@$HOST   und  https://$HOST/
Klappt alles, den Rettungsanker entfernen:  sudo atrm \$(atq | awk '{print \$1}')
Klappt es nicht: 10 Minuten warten (voriger Stand kommt zurück) oder  sudo bash $(dirname "$0")/07-zurueck.sh
Hinweise:
- Die Hochschul-Firewall davor filtert zusätzlich – Port 1880 dort schließen lassen.
- Docker (snap) veröffentlicht Ports an ufw vorbei (z. B. 9443/8000); ufw schützt diese NICHT.
HINWEIS
