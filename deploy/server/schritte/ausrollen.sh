#!/usr/bin/env bash
# Neue VibeVHS-Version einspielen (von deploy.ps1 aufgerufen): vorige Version bleibt als /opt/vibevhs.vorher (Rückweg),
# Datenbank-Sicherung vorher, Gesundheitsprüfung danach; bei Fehler automatisch zurück.
# Aufruf: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh [--db-neu] /tmp/vibevhs.tar.gz
#   --db-neu  Datenbank (samt -wal/-shm) vor dem Start nach /var/lib/vibevhs/beiseite/ verschieben; die Anwendung baut
#             sie aus Start- und Demodaten neu auf. Nötig, solange es keine EF-Migrationen gibt und sich das Schema ändert
#             („Datenbankschema veraltet“). Scheitert der Start, kommen vorige Version UND vorige Datenbank zurück.
# Rückweg von Hand: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh --zurueck
source "$(dirname "$0")/gemeinsam.sh"
als_root

DB=/var/lib/vibevhs/vibevhs.db
BEISEITE=/var/lib/vibevhs/beiseite

# Wartet auf /gesund; Erststart mit Demodaten (--db-neu) braucht deutlich länger (gemessen ~95 s)
gesund() { local s="${1:-90}"; for i in $(seq 1 "$s"); do curl -fsS -o /dev/null http://127.0.0.1:5080/vibevhs/gesund && return 0; sleep 1; done; return 1; }

db_neu=0
if [[ "${1:-}" == "--db-neu" ]]; then db_neu=1; shift; fi

if [[ "${1:-}" == "--zurueck" ]]; then
    [[ -d /opt/vibevhs.vorher ]] || fehler "Keine vorige Version vorhanden."
    systemctl stop vibevhs
    rm -rf /opt/vibevhs.defekt && mv /opt/vibevhs /opt/vibevhs.defekt && mv /opt/vibevhs.vorher /opt/vibevhs
    systemctl start vibevhs && gesund && ok "vorige Version läuft wieder" || fehler "auch die vorige Version startet nicht – journalctl -u vibevhs"
    exit 0
fi

# Voraussetzungen aus Schritt 06 (Dienstbenutzer, Verzeichnisse, Unit) – sonst vor jeder Änderung abbrechen
id vibevhs >/dev/null 2>&1 && [[ -f /etc/systemd/system/vibevhs.service && -d /var/lib/vibevhs && -d /var/backups/vibevhs ]]     || fehler "Schritt 06 fehlt (Dienstbenutzer vibevhs, /var/lib/vibevhs, vibevhs.service) – erst einrichten, dann erneut ausrollen. Nichts geändert."

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

# Datenbank vor dem Update sichern (Online-Backup, konsistent auch bei laufendem Dienst)
if [[ -f "$DB" ]]; then
    ziel="/var/backups/vibevhs/vor-update-$(date +%Y%m%d-%H%M%S).db"
    sqlite3 "$DB" ".backup '$ziel'" && chown vibevhs:vibevhs "$ziel" && ok "Datenbank gesichert: $ziel"
fi
# Kommentare zu Spezifikation/Doku (eigene Datei, wird von --db-neu nie angefasst) ebenfalls sichern
if [[ -f /var/lib/vibevhs/kommentare.db ]]; then
    ziel="/var/backups/vibevhs/vor-update-$(date +%Y%m%d-%H%M%S)-kommentare.db"
    sqlite3 /var/lib/vibevhs/kommentare.db ".backup '$ziel'" && chown vibevhs:vibevhs "$ziel" && ok "Kommentare gesichert: $ziel"
fi

systemctl stop vibevhs 2>/dev/null || true

# --db-neu: Datei samt WAL beiseitelegen (nicht löschen); Ordner nur für den Dienstbenutzer lesbar
beiseite=()
if (( db_neu )); then
    ts=$(date +%Y%m%d-%H%M%S)
    install -d -o vibevhs -g vibevhs -m 0700 "$BEISEITE"
    for f in "$DB" "$DB-wal" "$DB-shm"; do
        if [[ -e "$f" ]]; then
            mv "$f" "$BEISEITE/$(basename "$f").$ts"
            beiseite+=("$f")
            ok "beiseitegelegt: $f → $BEISEITE/$(basename "$f").$ts"
        fi
    done
    (( ${#beiseite[@]} )) || info "--db-neu: keine Datenbank vorhanden, wird ohnehin neu angelegt"
fi

rm -rf /opt/vibevhs.vorher
[[ -d /opt/vibevhs ]] && mv /opt/vibevhs /opt/vibevhs.vorher
mv "$neu" /opt/vibevhs
start=$(date +%s)
systemctl start vibevhs
if (( db_neu )); then info "Datenbank wird aus Start- und Demodaten aufgebaut (bis 5 min) …"; fi
if gesund $(( db_neu ? 300 : 90 )); then
    ok "neue Version läuft · $(curl -fsS http://127.0.0.1:5080/vibevhs/gesund)"
    rm -f "$archiv"
else
    warn "neue Version antwortet nicht – zurück zur vorigen"
    journalctl -u vibevhs --since "@$start" -n 30 --no-pager | sed 's/^/   /'
    # erst einsammeln, dann suchen (grep -q in einer Pipe würde unter pipefail per SIGPIPE als Fehler gelten)
    startlog=$(journalctl -u vibevhs --since "@$start" --no-pager -o cat 2>/dev/null || true)
    schema_veraltet=0
    [[ "$startlog" == *"Datenbankschema veraltet"* ]] && schema_veraltet=1
    systemctl stop vibevhs
    if (( ${#beiseite[@]} )); then
        # neu angelegte Datei verwerfen, vorige Datenbank zurück (passt zur vorigen Version)
        rm -f "$DB" "$DB-wal" "$DB-shm"
        for f in "${beiseite[@]}"; do mv "$BEISEITE/$(basename "$f").$ts" "$f"; done
        ok "vorige Datenbank zurückgelegt"
    fi
    # Hinweis vor dem Rückweg ausgeben (scheitert der, endet das Skript dort)
    if (( schema_veraltet && ! db_neu )); then
        warn "Ursache: Datenbankschema veraltet – erneut ausrollen mit: .\deploy\server\deploy.ps1 -DbNeu"
    fi
    bash "$0" --zurueck
    fehler "Ausrollen fehlgeschlagen (vorige Version läuft wieder; Datenbank-Sicherung in /var/backups/vibevhs)"
fi
