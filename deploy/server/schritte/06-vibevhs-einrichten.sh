#!/usr/bin/env bash
# Schritt 06: VibeVHS als systemd-Dienst einrichten (Benutzer, Verzeichnisse, Umgebung, Unit). Die Anwendung selbst bringt deploy.ps1.
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/06-vibevhs-einrichten.sh"
# Rückweg: sudo systemctl disable --now vibevhs  (Daten in /var/lib/vibevhs bleiben erhalten)
source "$(dirname "$0")/gemeinsam.sh"
als_root

id vibevhs >/dev/null 2>&1 || { useradd --system --home-dir /var/lib/vibevhs --shell /usr/sbin/nologin vibevhs; ok "Dienstbenutzer vibevhs angelegt"; }
install -d -o root    -g root    -m 0755 /opt/vibevhs
install -d -o vibevhs -g vibevhs -m 0750 /var/lib/vibevhs /var/log/vibevhs /var/backups/vibevhs
install -d -o root    -g vibevhs -m 0750 /etc/vibevhs
if [[ ! -f /etc/vibevhs/vibevhs.env ]]; then
    install -o root -g vibevhs -m 0640 "$KIT/vibevhs.env.beispiel" /etc/vibevhs/vibevhs.env
    ok "/etc/vibevhs/vibevhs.env angelegt (Geheimnisse dort eintragen, nie ins Repository)"
fi

if einspielen systemd/vibevhs.service /etc/systemd/system/vibevhs.service; then systemctl daemon-reload; fi
systemctl enable vibevhs >/dev/null 2>&1

if [[ -x /opt/vibevhs/VibeVHS.Web ]]; then
    systemctl restart vibevhs
    for i in $(seq 1 60); do curl -fsS -o /dev/null http://127.0.0.1:5080/vibevhs/gesund && break; sleep 1; done
    curl -fsS http://127.0.0.1:5080/vibevhs/gesund >/dev/null && ok "VibeVHS läuft: http://127.0.0.1:5080/vibevhs/gesund → ok" \
        || { journalctl -u vibevhs -n 30 --no-pager | sed 's/^/   /'; fehler "VibeVHS antwortet nicht"; }
else
    warn "Noch keine Anwendung in /opt/vibevhs – jetzt vom Entwicklungsrechner ausrollen:  .\\deploy\\server\\deploy.ps1"
fi
ok "Schritt 06 fertig"
