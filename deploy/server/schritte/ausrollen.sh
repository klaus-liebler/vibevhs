#!/usr/bin/env bash
# Neue VibeVHS-Version einspielen (von deploy.ps1 aufgerufen): vorige Version bleibt als /opt/vibevhs.vorher (Rückweg),
# Datenbank-Sicherung vorher, Gesundheitsprüfung danach; bei Fehler automatisch zurück.
# Aufruf: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh [--db-neu | --nur-pruefen] /tmp/vibevhs.tar.gz
#   --db-neu       Datenbank „vibevhs“ im Container vibevhs-db vor dem Start in „vibevhs_alt_<Zeitstempel>“ umbenennen; die Anwendung baut
#                  sie aus Start- und Demodaten neu auf. Nötig, solange es keine EF-Migrationen gibt und sich das Schema ändert.
#                  Scheitert der Start, kommen vorige Version UND vorige Datenbank zurück. „vibevhs_kommentare“ bleibt immer unberührt.
#   --nur-pruefen  nur den Prüflauf der neuen Version zeigen (Datenbankstand), nichts ändern.
# Rückweg von Hand: sudo bash /tmp/vibevhs-kit/schritte/ausrollen.sh --zurueck
#
# Ablauf (seit 24.11.2026 „mitdenkend“):
#   1. Prüflauf: Die NEUE Version läuft einmal als Dienstbenutzer mit der Umgebung des Dienstes und meldet je Datenbank, was ein Start
#      täte (aktuell / neu / Migration / veraltet / keine Verbindung) – solange die alte Version noch läuft. Passt es nicht, endet das
#      Ausrollen hier mit einem konkreten Hinweis, und nichts ist geändert (Exitcode 3 = Neuaufbau nötig, deploy.ps1 fragt dann nach).
#   2. Sicherung, Austausch, Start.
#   3. Warten auf /gesund mit Blick auf den Prozess: beendet sich der Dienst oder startet systemd ihn neu, wird nicht weiter gewartet;
#      alle 10 s erscheint die letzte Logzeile, damit ein langer Erststart nicht wie ein Hänger aussieht.
#   4. Scheitert der Start, deutet das Skript das Log (bekannte Meldungen → Ursache und nächster Schritt) und geht zurück.
#   5. Läuft die neue Version, nennt es Fehlermeldungen (ERR/FTL) aus dem Start, falls es welche gab.
source "$(dirname "$0")/gemeinsam.sh"
source "$(dirname "$0")/liebler-dotnet.sh"
als_root

DBC=vibevhs-db            # PostgreSQL-Container (Schritt 10)
DB=vibevhs                # Datenbank der Anwendung
GESUND=http://127.0.0.1:5080/vibevhs/gesund
EXIT_NEUAUFBAU=3          # deploy.ps1 bietet dann -DbNeu an
psql_admin() { docker exec "$DBC" psql -U vibevhs -d postgres -v ON_ERROR_STOP=1 -tAc "$1"; }
db_vorhanden() { [[ "$(psql_admin "SELECT 1 FROM pg_database WHERE datname = '$1'")" == "1" ]]; }
dienst() { systemctl show vibevhs -p "$1" --value; }

# Prüflauf der Version in $1 als Dienstbenutzer, mit Umgebung und Umgebungsdatei des Dienstes; ändert nichts (VibeVHS.Web --datenbankstand).
datenbankstand() {
    systemd-run --quiet --wait --pipe --collect -p User=vibevhs -p Group=vibevhs -p WorkingDirectory="$1" -p RuntimeMaxSec=120 \
        -p EnvironmentFile=/etc/vibevhs/vibevhs.env -p "Environment=$(dienst Environment)" \
        "$1/VibeVHS.Web" --datenbankstand
}

# Bekannte Meldungen im Startlog → Ursache und nächster Schritt (Text auf stdin).
deuten() {
    local log; log=$(cat)
    local -a hinweise=()
    [[ "$log" == *"Datenbankschema veraltet"* ]] && hinweise+=("Schema geändert, keine Migration → erneut mit: .\\deploy\\server\\deploy.ps1 -DbNeu")
    [[ "$log" == *"Kommentar-Datenbank"*"Schemaversion"* ]] && hinweise+=("Kommentar-Datenbank passt nicht → per Skript anpassen, NICHT neu aufbauen (echte Kommentare)")
    [[ "$log" =~ (address\ already\ in\ use|Failed\ to\ bind) ]] && hinweise+=("Port 5080 belegt → sudo ss -ltnp | grep 5080 (läuft noch ein alter Prozess?)")
    [[ "$log" =~ (Failed\ to\ connect\ to\ 127\.0\.0\.1:5441|Connection\ refused) ]] && hinweise+=("PostgreSQL nicht erreichbar → sudo docker ps -a | grep $DBC; sudo docker start $DBC (Schritt 10)")
    [[ "$log" == *"password authentication failed"* ]] && hinweise+=("Anmeldung an PostgreSQL abgelehnt → ConnectionStrings__VibeVHS in /etc/vibevhs/vibevhs.env prüfen (Schritt 10 setzt sie)")
    [[ "$log" == *"ConnectionStrings:VibeVHS"* ]] && hinweise+=("Verbindungszeichenfolge fehlt oder ist unvollständig → /etc/vibevhs/vibevhs.env (Schritt 10)")
    [[ "$log" =~ (Access\ to\ the\ path|Permission\ denied|UnauthorizedAccess) ]] && hinweise+=("Rechte → Besitzer von /var/lib/vibevhs, /var/log/vibevhs, /var/backups/vibevhs muss vibevhs sein (Schritt 06)")
    [[ "$log" =~ (OutOfMemory|oom-kill|Killed) ]] && hinweise+=("Speicher knapp → free -m; andere Dienste prüfen")
    [[ "$log" == *"status=203/EXEC"* ]] && hinweise+=("Programm nicht ausführbar → Archiv beschädigt oder falsche Plattform (linux-x64)")
    [[ "$log" == *"Unhandled exception"* ]] && hinweise+=("Absturz beim Start → erste Ausnahme im Log oben lesen")
    if (( ${#hinweise[@]} )); then
        for h in "${hinweise[@]}"; do warn "Ursache: $h"; done
    else
        warn "Keine bekannte Meldung erkannt – letzte Fehlerzeilen:"
        grep -E '(FTL|ERR)\]|Exception|exited' <<<"$log" | tail -n 8 | sed 's/^/   /' || true
    fi
}

warten_auf_start() {   # $1 = Sekunden, $2 = Startzeit (epoch), $3 = Neustarts vor dem Start
    local letzte="" i zeile
    for (( i = 1; i <= $1; i++ )); do
        curl -fsS -o /dev/null "$GESUND" && return 0
        if [[ "$(dienst ActiveState)" =~ ^(failed|inactive)$ || "$(dienst SubState)" == "auto-restart" ]] || (( $(dienst NRestarts) > $3 )); then
            warn "Der Dienst hat sich nach ${i} s beendet ($(dienst ActiveState)/$(dienst SubState), Exitcode $(dienst ExecMainStatus)) – warte nicht weiter."
            return 1
        fi
        if (( i % 10 == 0 )); then
            zeile=$(journalctl -u vibevhs --since "@$2" -n 1 -o cat --no-pager 2>/dev/null || true)
            [[ -n "$zeile" && "$zeile" != "$letzte" ]] && info "  ${i} s: ${zeile:0:160}"
            letzte=$zeile
        fi
        sleep 1
    done
    warn "Keine Antwort von /gesund nach $1 s."
    return 1
}

db_neu=0; nur_pruefen=0
case "${1:-}" in
    --db-neu) db_neu=1; shift ;;
    --nur-pruefen) nur_pruefen=1; shift ;;
esac

if [[ "${1:-}" == "--zurueck" ]]; then
    [[ -d /opt/vibevhs.vorher ]] || fehler "Keine vorige Version vorhanden."
    systemctl stop vibevhs
    rm -rf /opt/vibevhs.defekt && mv /opt/vibevhs /opt/vibevhs.defekt && mv /opt/vibevhs.vorher /opt/vibevhs
    n0=$(dienst NRestarts); start=$(date +%s)
    systemctl start vibevhs && warten_auf_start 300 "$start" "$n0" && ok "vorige Version läuft wieder" \
        || { journalctl -u vibevhs --since "@$start" --no-pager -o cat | deuten; fehler "auch die vorige Version startet nicht – journalctl -u vibevhs"; }
    exit 0
fi

# Voraussetzungen aus Schritt 06 (Dienstbenutzer, Verzeichnisse, Unit) und 10 (PostgreSQL) – sonst vor jeder Änderung abbrechen
id vibevhs >/dev/null 2>&1 && [[ -f /etc/systemd/system/vibevhs.service && -d /var/lib/vibevhs && -d /var/backups/vibevhs ]] \
    || fehler "Schritt 06 fehlt (Dienstbenutzer vibevhs, /var/lib/vibevhs, vibevhs.service) – erst einrichten, dann erneut ausrollen. Nichts geändert."
docker exec "$DBC" pg_isready -U vibevhs >/dev/null 2>&1 \
    || fehler "Schritt 10 fehlt oder Container $DBC läuft nicht (PostgreSQL) – sudo docker start $DBC bzw. Schritt 10, dann erneut ausrollen. Nichts geändert."

archiv="${1:?Pfad zum Archiv fehlt}"
[[ -f "$archiv" ]] || fehler "Archiv $archiv fehlt"
neu=$(mktemp -d /opt/vibevhs.neu.XXXX)
trap '[[ -n "${neu:-}" && -d "$neu" ]] && rm -rf "$neu"' EXIT
tar -xzf "$archiv" -C "$neu" --strip-components=1
[[ -f "$neu/VibeVHS.Web" ]] || { rm -rf "$neu"; fehler "Archiv enthält kein VibeVHS.Web"; }
# Framework-abhängiges Paket: passende gemeinsame Runtime vorhanden? (VibeVHS-Schritt 09) – sonst nichts ändern
dotnet_pruefen "$neu" "VibeVHS.Web" || { rm -rf "$neu"; fehler "Ausrollen abgebrochen: Runtime fehlt (siehe oben). Nichts geändert."; }
# Unter Windows gebaute Archive tragen keine Unix-Rechte (alles 666/777): Rechte hier festlegen
chown -R root:root "$neu"
find "$neu" -type d -exec chmod 755 {} +
find "$neu" -type f -exec chmod 644 {} +
chmod 755 "$neu/VibeVHS.Web"; [[ -f "$neu/createdump" ]] && chmod 755 "$neu/createdump"

# 1. Prüflauf der neuen Version (die laufende bleibt unberührt)
info "Prüflauf der neuen Version: Was würde ein Start mit den Datenbanken tun?"
set +e; stand=$(datenbankstand "$neu" 2>&1); code=$?; set -e
zeilen=$(grep '^DATENBANKSTAND|' <<<"$stand" || true)
alle_aktuell=1
while IFS='|' read -r _ rolle name zustand ist soll hinweis; do
    [[ -z "$rolle" ]] && continue
    [[ "$zustand" == "Aktuell" ]] || alle_aktuell=0
    printf '   %-16s %-24s %-15s %s\n' "$rolle" "$name" "$zustand" "$hinweis"
done <<<"$zeilen"
case "$code" in
    0) ok "Datenbanken passen zur neuen Version" ;;
    3) if (( db_neu )); then
           info "Schema geändert – wird wie verlangt neu aufgebaut (-DbNeu)"
       elif (( ! nur_pruefen )); then
           warn "Die neue Version passt nicht zur Datenbank (Schema geändert, keine Migration). Nichts geändert, die laufende Version bleibt."
           warn "Weiter mit: .\\deploy\\server\\deploy.ps1 -DbNeu (Datenbank wird beiseitegelegt und neu aus Start- und Demodaten aufgebaut)"
           exit "$EXIT_NEUAUFBAU"
       fi ;;
    4) fehler "Kommentar-Datenbank passt nicht zur neuen Version – sie enthält echte Kommentare und wird NICHT neu aufgebaut. Nichts geändert." ;;
    5) fehler "Die neue Version erreicht PostgreSQL nicht (siehe Hinweis oben; Verbindung in /etc/vibevhs/vibevhs.env). Nichts geändert." ;;
    *) [[ -z "$zeilen" ]] && tail -n 20 <<<"$stand" | sed 's/^/   /'
       deuten <<<"$stand"
       fehler "Prüflauf der neuen Version scheitert (Exitcode $code) – sie würde so auch nicht starten. Nichts geändert." ;;
esac
if (( db_neu && alle_aktuell )); then
    warn "Hinweis: Das Schema ist unverändert – -DbNeu wäre nicht nötig gewesen; die Datenbank wird trotzdem wie verlangt neu aufgebaut."
fi
if (( nur_pruefen )); then
    ok "Nur geprüft – nichts geändert."
    exit "$code"
fi

# 2. Datenbanken vor dem Update sichern (pg_dump, konsistent auch bei laufendem Dienst)
for d in "$DB" "${DB}_kommentare"; do
    if db_vorhanden "$d"; then
        ziel="/var/backups/vibevhs/vor-update-$(date +%Y%m%d-%H%M%S)-$d.dump"
        docker exec "$DBC" pg_dump -U vibevhs -d "$d" --format=custom > "$ziel" && chown vibevhs:vibevhs "$ziel" && ok "Datenbank $d gesichert: $ziel"
    fi
done
# Alte SQLite-Dateien (bis 11/2026) ebenfalls sichern, solange es sie gibt; kommentare.db übernimmt die Anwendung beim ersten Start
for f in /var/lib/vibevhs/vibevhs.db /var/lib/vibevhs/kommentare.db; do
    [[ -f "$f" ]] && cp -a "$f" "/var/backups/vibevhs/vor-update-$(date +%Y%m%d-%H%M%S)-$(basename "$f")" && ok "SQLite-Datei gesichert: $f"
done

systemctl stop vibevhs 2>/dev/null || true

# --db-neu: Datenbank umbenennen (nicht löschen); die Anwendung legt sie neu an
beiseite=""
if (( db_neu )); then
    ts=$(date +%Y%m%d_%H%M%S)
    if db_vorhanden "$DB"; then
        psql_admin "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DB'" >/dev/null
        psql_admin "ALTER DATABASE \"$DB\" RENAME TO \"${DB}_alt_$ts\"" >/dev/null
        beiseite="${DB}_alt_$ts"
        ok "beiseitegelegt: Datenbank $DB → $beiseite"
    else
        info "--db-neu: keine Datenbank vorhanden, wird ohnehin neu angelegt"
    fi
fi

rm -rf /opt/vibevhs.vorher
[[ -d /opt/vibevhs ]] && mv /opt/vibevhs /opt/vibevhs.vorher
mv "$neu" /opt/vibevhs; neu=""

# 3. Start und Warten mit Blick auf den Prozess
n0=$(dienst NRestarts)
start=$(date +%s)
systemctl start vibevhs
if (( db_neu )); then info "Datenbank wird aus Start- und Demodaten aufgebaut (gemessen gut 2 min, höchstens 5 min) …"; fi
if warten_auf_start $(( db_neu ? 300 : 120 )) "$start" "$n0"; then
    ok "neue Version läuft nach $(( $(date +%s) - start )) s · $(curl -fsS "$GESUND")"
    rm -f "$archiv"
    # 5. Fehler beim Start trotzdem nennen
    fehlerzeilen=$(journalctl -u vibevhs --since "@$start" --no-pager -o cat 2>/dev/null | grep -E '(FTL|ERR)\]' || true)
    if [[ -n "$fehlerzeilen" ]]; then
        warn "$(wc -l <<<"$fehlerzeilen") Fehlermeldung(en) seit dem Start (die Anwendung läuft trotzdem):"
        head -n 5 <<<"$fehlerzeilen" | cut -c1-200 | sed 's/^/   /'
    fi
else
    # 4. Log deuten, dann zurück
    startlog=$(journalctl -u vibevhs --since "@$start" --no-pager -o cat 2>/dev/null || true)
    tail -n 25 <<<"$startlog" | cut -c1-220 | sed 's/^/   /'
    deuten <<<"$startlog"
    systemctl stop vibevhs
    if [[ -n "$beiseite" ]]; then
        # neu angelegte Datenbank verwerfen, vorige zurück (passt zur vorigen Version)
        psql_admin "DROP DATABASE IF EXISTS \"$DB\" WITH (FORCE)" >/dev/null
        psql_admin "ALTER DATABASE \"$beiseite\" RENAME TO \"$DB\"" >/dev/null
        ok "vorige Datenbank zurückgelegt"
    fi
    bash "$0" --zurueck
    fehler "Ausrollen fehlgeschlagen (vorige Version läuft wieder; Datenbank-Sicherung in /var/backups/vibevhs)"
fi
