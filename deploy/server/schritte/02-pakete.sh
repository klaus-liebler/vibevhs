#!/usr/bin/env bash
# Schritt 02: Pakete installieren (nginx, certbot, ufw, htpasswd, at, sqlite3, ICU für .NET). Ändert noch nichts am laufenden Apache.
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/02-pakete.sh"
# Rückweg: nicht nötig (installierte Pakete stören nicht; nginx bleibt bis Schritt 04 aus).
source "$(dirname "$0")/gemeinsam.sh"
als_root

. /etc/os-release
info "System: $PRETTY_NAME ($(uname -r))"

# ICU für .NET (Kulturen de-DE): Paketname hängt von der Ubuntu-Version ab (libicu74, libicu76 …)
icu="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)"
[[ -n "$icu" ]] || warn "Kein libicuNN-Paket gefunden – .NET läuft dann nur mit InvariantGlobalization."

pakete=(nginx ufw apache2-utils at sqlite3 curl ca-certificates ${icu:+$icu})
# certbot ist auf diesem Server als snap installiert (mit snap.certbot.renew.timer) – KEIN zweites certbot per apt
if snap list certbot >/dev/null 2>&1; then
    ok "certbot (snap) vorhanden: $(snap list certbot | awk 'NR==2{print $2}')"
else
    pakete+=(certbot)
fi
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

# nginx bleibt bis zum Wechsel (Schritt 04) aus
systemctl disable --now nginx >/dev/null 2>&1 || true
systemctl enable --now atd >/dev/null 2>&1 || true

info "Prüfung"
nginx -v 2>&1 | sed 's/^/   /'
certbot --version 2>&1 | sed 's/^/   /'
ufw version | head -1 | sed 's/^/   /'
systemctl is-active apache2 >/dev/null && ok "Apache läuft weiter (unverändert)" || warn "Apache läuft nicht?"
timer=$(systemctl list-timers --all --no-legend 2>/dev/null | grep -oE '(snap\.certbot\.renew|certbot)\.timer' | head -1 || true)
[[ -n "$timer" ]] && ok "Erneuerung aktiv: $timer" || warn "kein certbot-Timer gefunden – siehe Runbook Schritt 05"
ok "Schritt 02 fertig"
