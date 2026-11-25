#!/usr/bin/env bash
# Schritt 02: Pakete installieren (nginx, ufw, htpasswd, at, sqlite3, ICU für .NET) – kein certbot (Hochschulzertifikat). Ändert noch nichts am laufenden Apache.
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/02-pakete.sh"
# Rückweg: nicht nötig (installierte Pakete stören nicht; nginx bleibt bis Schritt 04 aus).
source "$(dirname "$0")/gemeinsam.sh"
als_root

. /etc/os-release
info "System: $PRETTY_NAME ($(uname -r))"

# ICU für .NET (Kulturen de-DE): Paketname hängt von der Ubuntu-Version ab (libicu74, libicu76 …)
icu="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)"
[[ -n "$icu" ]] || warn "Kein libicuNN-Paket gefunden – .NET läuft dann nur mit InvariantGlobalization."

pakete=(nginx ufw apache2-utils at sqlite3 curl ca-certificates openssl ${icu:+$icu})
fehlend=()
for p in "${pakete[@]}"; do dpkg -s "$p" >/dev/null 2>&1 || fehlend+=("$p"); done

if [[ ${#fehlend[@]} -eq 0 ]]; then
    ok "alle Pakete schon installiert: ${pakete[*]}"
else
    info "installiere: ${fehlend[*]}"
    # nginx soll beim Installieren NICHT starten (Apache belegt 80/443) – Dienststarts während apt unterdrücken
    printf '#!/bin/sh\nexit 101\n' > /usr/sbin/policy-rc.d && chmod 755 /usr/sbin/policy-rc.d
    trap 'rm -f /usr/sbin/policy-rc.d' EXIT
    apt-get update -q
    DEBIAN_FRONTEND=noninteractive apt-get install -y -q "${fehlend[@]}"
    rm -f /usr/sbin/policy-rc.d
    trap - EXIT
fi

# nginx bleibt bis zum Wechsel (Schritt 03) aus – aber NUR, solange Apache noch ausliefert. Ist der Wechsel schon erfolgt
# (Apache aus, nginx in Betrieb), bleibt nginx unangetastet: sonst wären Website und alle Apps dahinter weg.
if systemctl is-active --quiet apache2; then
    systemctl disable --now nginx >/dev/null 2>&1 || true
elif systemctl is-active --quiet nginx; then
    ok "nginx ist schon in Betrieb (Schritt 03 erledigt) – bleibt unverändert"
fi
systemctl enable --now atd >/dev/null 2>&1 || true

info "Prüfung"
nginx -v 2>&1 | sed 's/^/   /'
ufw version | head -1 | sed 's/^/   /'
if systemctl is-active --quiet apache2; then ok "Apache läuft weiter (unverändert)"
elif systemctl is-active --quiet nginx; then ok "nginx läuft (unverändert)"
else warn "Weder Apache noch nginx laufen – Website nicht erreichbar!"; fi
ok "Schritt 02 fertig"
