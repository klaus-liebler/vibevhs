#!/usr/bin/env bash
# Neue VibeVHS-Version einspielen (von deploy.ps1 aufgerufen): vorige Version bleibt als /opt/vibevhs.vorher (Rückweg),
# Datenbank-Sicherung vorher, Gesundheitsprüfung danach; bei Fehler automatisch zurück.
# Aufruf: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh /tmp/vibevhs.tar.gz
# Rückweg von Hand: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh --zurueck
source "$(dirname "$0")/gemeinsam.sh"
als_root

gesund() { for i in $(seq 1 90); do curl -fsS -o /dev/null http://127.0.0.1:5080/vibevhs/gesund && return 0; sleep 1; done; return 1; }

if [[ "${1:-}" == "--zurueck" ]]; then
    [[ -d /opt/vibevhs.vorher ]] || fehler "Keine vorige Version vorhanden."
    systemctl stop vibevhs
    rm -rf /opt/vibevhs.defekt && mv /opt/vibevhs /opt/vibevhs.defekt && mv /opt/vibevhs.vorher /opt/vibevhs
    systemctl start vibevhs && gesund && ok "vorige Version läuft wieder" || fehler "auch die vorige Version startet nicht – journalctl -u vibevhs"
    exit 0
fi

archiv="${1:?Pfad zum Archiv fehlt}"
[[ -f "$archiv" ]] || fehler "Archiv $archiv fehlt"
neu=$(mktemp -d /opt/vibevhs.neu.XXXX)
tar -xzf "$archiv" -C "$neu" --strip-components=1
[[ -f "$neu/VibeVHS.Web" ]] || { rm -rf "$neu"; fehler "Archiv enthält kein VibeVHS.Web"; }
# Unter Windows gebaute Archive tragen keine Unix-Rechte (alles 666/777): Rechte hier festlegen
chown -R root:root "$neu"
find "$neu" -type d -exec chmod 755 {} +
find "$neu" -type f -exec chmod 644 {} +
chmod 755 "$neu/VibeVHS.Web"; [[ -f "$neu/createdump" ]] && chmod 755 "$neu/createdump"

# Datenbank vor dem Update sichern (Migrationen laufen beim Start)
if [[ -f /var/lib/vibevhs/vibevhs.db ]]; then
    ziel="/var/backups/vibevhs/vor-update-$(date +%Y%m%d-%H%M%S).db"
    sqlite3 /var/lib/vibevhs/vibevhs.db ".backup '$ziel'" && chown vibevhs:vibevhs "$ziel" && ok "Datenbank gesichert: $ziel"
fi

systemctl stop vibevhs 2>/dev/null || true
rm -rf /opt/vibevhs.vorher
[[ -d /opt/vibevhs ]] && mv /opt/vibevhs /opt/vibevhs.vorher
mv "$neu" /opt/vibevhs
systemctl start vibevhs
if gesund; then
    ok "neue Version läuft · $(curl -fsS http://127.0.0.1:5080/vibevhs/gesund)"
    rm -f "$archiv"
else
    warn "neue Version antwortet nicht – zurück zur vorigen"
    journalctl -u vibevhs -n 30 --no-pager | sed 's/^/   /'
    bash "$0" --zurueck
    fehler "Ausrollen fehlgeschlagen (vorige Version läuft wieder; Datenbank-Sicherung in /var/backups/vibevhs)"
fi
