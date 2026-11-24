#!/usr/bin/env bash
# Schritt 09: PostgreSQL 17 für VibeVHS (Umstellung von SQLite, 24.11.2026) – Datenbank als Container, Werkzeuge pg_dump/pg_restore 17 auf dem Host.
# Vor dem ersten Ausrollen einer Version mit PostgreSQL ausführen (danach: .\deploy\server\deploy.ps1 -DbNeu).
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/09-postgresql.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/09-zurueck.sh (Container stoppen; Daten im Volume bleiben erhalten)
#
# - Container „vibevhs-db“ (postgres:17), nur auf 127.0.0.1:5441 erreichbar, Daten im Docker-Volume „vibevhs-db“, Neustart automatisch.
# - Zufälliges Passwort, nur in /etc/vibevhs/postgres.env (root, 0600) und als Verbindungszeichenfolge in /etc/vibevhs/vibevhs.env (root:vibevhs, 0640).
# - pg_dump/pg_restore in Version 17 für die Sicherung der Anwendung (läuft als Benutzer vibevhs, ohne Docker-Rechte): aus der Distribution,
#   sonst aus dem Paketarchiv von PostgreSQL (apt.postgresql.org). ⚑ prüfen, ob Ubuntu 26.04 postgresql-client-17 selbst führt.
source "$(dirname "$0")/gemeinsam.sh"
als_root

CONTAINER=vibevhs-db
PORT=5441
PGENV=/etc/vibevhs/postgres.env
APPENV=/etc/vibevhs/vibevhs.env

command -v docker >/dev/null || fehler "docker fehlt (auf liebler als snap installiert) – erst Docker einrichten."
[[ -d /etc/vibevhs ]] || fehler "Schritt 06 fehlt (/etc/vibevhs)."

# 1. Passwort (einmalig)
if [[ ! -f "$PGENV" ]]; then
    umask 077
    printf 'POSTGRES_PASSWORD=%s\n' "$(openssl rand -hex 24)" > "$PGENV"
    chmod 0600 "$PGENV"
    ok "Passwort erzeugt: $PGENV"
fi
# shellcheck disable=SC1090
source "$PGENV"

# 2. Container
if docker ps -a --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    docker start "$CONTAINER" >/dev/null
    ok "Container $CONTAINER läuft (vorhanden)"
else
    docker run -d --name "$CONTAINER" --restart unless-stopped \
        -p "127.0.0.1:$PORT:5432" \
        -v vibevhs-db:/var/lib/postgresql/data \
        -e POSTGRES_USER=vibevhs -e POSTGRES_PASSWORD="$POSTGRES_PASSWORD" -e POSTGRES_DB=vibevhs \
        postgres:17 >/dev/null
    ok "Container $CONTAINER angelegt (postgres:17, 127.0.0.1:$PORT)"
fi
for i in $(seq 1 60); do docker exec "$CONTAINER" pg_isready -U vibevhs >/dev/null 2>&1 && break; sleep 1; done
docker exec "$CONTAINER" pg_isready -U vibevhs >/dev/null || fehler "PostgreSQL antwortet nicht – docker logs $CONTAINER"
ok "PostgreSQL bereit: $(docker exec "$CONTAINER" psql -U vibevhs -d postgres -tAc 'SHOW server_version')"

# 3. Verbindung für die Anwendung (ersetzt eine frühere Zeile)
sichern "$APPENV"
sed -i '/^ConnectionStrings__VibeVHS=/d' "$APPENV"
printf 'ConnectionStrings__VibeVHS=Host=127.0.0.1;Port=%s;Database=vibevhs;Username=vibevhs;Password=%s\n' "$PORT" "$POSTGRES_PASSWORD" >> "$APPENV"
chown root:vibevhs "$APPENV"; chmod 0640 "$APPENV"
ok "Verbindung in $APPENV eingetragen"

# 3b. Unit mit Sicherung__PgBin und Kommentare__Altdatei (Schritt 06 spielt sie sonst nicht neu ein)
if einspielen systemd/vibevhs.service /etc/systemd/system/vibevhs.service; then systemctl daemon-reload; fi

# 4. pg_dump/pg_restore 17 auf dem Host
if ! command -v /usr/lib/postgresql/17/bin/pg_dump >/dev/null 2>&1; then
    apt-get update -qq
    if ! apt-cache show postgresql-client-17 >/dev/null 2>&1; then
        info "postgresql-client-17 nicht in der Distribution – Paketarchiv apt.postgresql.org einrichten"
        apt-get install -y -qq postgresql-common >/dev/null
        /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y >/dev/null
        apt-get update -qq
    fi
    apt-get install -y -qq postgresql-client-17 >/dev/null
fi
/usr/lib/postgresql/17/bin/pg_dump --version | grep -q " 17" || fehler "pg_dump 17 fehlt"
ok "Werkzeuge: $(/usr/lib/postgresql/17/bin/pg_dump --version)"

# 5. Probe als Dienstbenutzer: Verbindung und Sicherung ohne Docker-Rechte
sudo -u vibevhs env PGPASSWORD="$POSTGRES_PASSWORD" /usr/lib/postgresql/17/bin/pg_dump -h 127.0.0.1 -p "$PORT" -U vibevhs -d vibevhs --schema-only >/dev/null \
    && ok "Probe: vibevhs kann sichern" || fehler "Probe-Sicherung als vibevhs fehlgeschlagen"

ok "Schritt 09 fertig – jetzt ausrollen: .\\deploy\\server\\deploy.ps1 -DbNeu (alte Kommentare aus kommentare.db werden beim Start übernommen)"
