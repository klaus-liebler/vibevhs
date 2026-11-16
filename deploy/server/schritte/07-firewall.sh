#!/usr/bin/env bash
# Schritt 07: Firewall (ufw) – von außen nur 443 (und 80 für Let's Encrypt), SSH nur aus dem VPN-Netz, 1880 zu.
# Start (VPN-Netz der Hochschule eintragen!):
#   ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo VPN_NETZ=<a.b.c.d/nn> VPN_NETZ6=<präfix/nn> bash /tmp/vibevhs-kit/schritte/07-firewall.sh"
# Rettungsanker: ufw schaltet sich nach 10 Minuten von selbst wieder ab, wenn die Bestätigung ausbleibt.
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
ufw --force reset >/dev/null
ufw default deny incoming
ufw default allow outgoing
ufw allow 443/tcp comment 'HTTPS nginx'
ufw allow 80/tcp comment 'HTTP: Lets Encrypt und Umleitung'
ufw allow from "$VPN_NETZ" to any port 22 proto tcp comment 'SSH nur aus dem VPN'
[[ -n "${VPN_NETZ6:-}" ]] && ufw allow from "$VPN_NETZ6" to any port 22 proto tcp comment 'SSH nur aus dem VPN (IPv6)'
# 1880 (Node-RED) und alles andere: zu

echo "ufw disable" | at now + 10 minutes 2>/dev/null && warn "Rettungsanker: ufw schaltet sich in 10 Minuten wieder ab (atq zeigt den Auftrag)."
ufw --force enable
ufw status verbose | sed 's/^/   /'
cat <<HINWEIS

JETZT in einem NEUEN Fenster (aus dem VPN) prüfen:  ssh srvadmin@$HOST   und  https://$HOST/
Klappt alles, den Rettungsanker entfernen:  sudo atrm \$(atq | awk '{print \$1}')
Klappt es nicht: einfach 10 Minuten warten (ufw geht aus) oder  sudo bash $(dirname "$0")/07-zurueck.sh
Hinweis: Die Hochschul-Firewall davor filtert zusätzlich – Port 1880 dort ebenfalls schließen lassen.
HINWEIS
