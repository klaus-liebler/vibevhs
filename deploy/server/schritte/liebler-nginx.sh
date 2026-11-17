# Gemeinsames nginx-Schema für liebler.iui.hs-osnabrueck.de – IDENTISCH in den Kits von vibevhs, fraktionperfekt und podium
# (wird per „source“ eingebunden). Bei Änderungen in allen drei Repos gleich halten.
#
#   /etc/nginx/sites-available/liebler.conf   Grundgerüst: TLS (Hochschulzertifikat), :80 → :443, /var/www/html (statische Seiten,
#                                             z. B. /rgb, /simplesorter, /factorycontrol) und die eine Zeile
#                                                 include /etc/nginx/liebler.d/*.conf;
#   /etc/nginx/liebler.d/<app>.conf          je App genau EINE Datei mit ihren location-Blöcken (vibevhs, nodered, fraktionperfekt,
#                                             podium …). Jedes Kit schreibt nur seine eigene Datei – niemand überschreibt die
#                                             Einträge der anderen.
#
# Alle Änderungen laufen transaktional: Sicherung → Änderung → nginx -t → reload; scheitert der Test, kommt der vorige Stand zurück.
# Erwartet aus gemeinsam.sh: info, ok, warn, fehler, sichern.

LIEBLER_SITE="${LIEBLER_SITE:-/etc/nginx/sites-available/liebler.conf}"
LIEBLER_D="${LIEBLER_D:-/etc/nginx/liebler.d}"
LIEBLER_INCLUDE="include /etc/nginx/liebler.d/*.conf;"
LIEBLER_ANKER="include /etc/nginx/snippets/liebler-zertifikat.conf;"
LIEBLER_KOMMENTAR="# ---------- Apps: je eine Datei in /etc/nginx/liebler.d/ (vibevhs, nodered, fraktionperfekt, podium …) ----------"

# Stand vor einer Änderung festhalten (für den Rückweg innerhalb dieses Laufs)
_liebler_stand=""
_liebler_merken() {
    _liebler_stand=$(mktemp -d)
    [[ -f "$LIEBLER_SITE" ]] && cp -a "$LIEBLER_SITE" "$_liebler_stand/site"
    [[ -d "$LIEBLER_D" ]] && cp -a "$LIEBLER_D" "$_liebler_stand/d"
    sichern "$LIEBLER_SITE" "$LIEBLER_D"
}
_liebler_zurueck() {
    [[ -f "$_liebler_stand/site" ]] && cp -a "$_liebler_stand/site" "$LIEBLER_SITE"
    rm -rf "$LIEBLER_D"
    [[ -d "$_liebler_stand/d" ]] && cp -a "$_liebler_stand/d" "$LIEBLER_D"
    return 0
}
_liebler_abschliessen() {
    if nginx -t 2>/dev/null; then
        if systemctl is-active --quiet nginx; then systemctl reload nginx && ok "nginx neu geladen"; fi
        rm -rf "$_liebler_stand"
        return 0
    fi
    nginx -t || true
    _liebler_zurueck
    rm -rf "$_liebler_stand"
    nginx -t 2>/dev/null && systemctl is-active --quiet nginx && systemctl reload nginx
    fehler "nginx-Konfiguration fehlerhaft – vorigen Stand wiederhergestellt (Sicherung: $SICHERUNG)"
}

# Die include-Zeile für liebler.d/ in den HTTPS-Server-Block setzen (direkt nach der Zertifikatszeile), falls sie fehlt.
_liebler_include_setzen() {
    install -d -m 0755 "$LIEBLER_D"
    grep -qF "$LIEBLER_INCLUDE" "$LIEBLER_SITE" && return 0
    grep -qF "$LIEBLER_ANKER" "$LIEBLER_SITE" || fehler "Zeile „$LIEBLER_ANKER“ fehlt in $LIEBLER_SITE – bitte „$LIEBLER_INCLUDE“ von Hand in den 443-Server-Block setzen."
    awk -v anker="$LIEBLER_ANKER" -v kom="    $LIEBLER_KOMMENTAR" -v inc="    $LIEBLER_INCLUDE" '
        { print }
        !gesetzt && index($0, anker) { print ""; print kom; print inc; gesetzt = 1 }' "$LIEBLER_SITE" > "$LIEBLER_SITE.neu"
    mv "$LIEBLER_SITE.neu" "$LIEBLER_SITE"
    ok "liebler.conf bindet jetzt $LIEBLER_D/*.conf ein"
}

# Ältere Einbindungen (eine include-Zeile je App direkt in liebler.conf, Stand vor liebler.d) nach liebler.d/ umziehen:
# „include /etc/nginx/snippets/<name>(-locations).conf;“ → liebler.d/<name>.conf mit genau dieser Zeile.
_liebler_altlasten_umziehen() {
    local zeile datei name
    while IFS= read -r zeile; do
        datei=$(sed -E 's/.*include[[:space:]]+(\S+);.*/\1/' <<<"$zeile")
        name=$(basename "$datei" .conf); name=${name%-locations}
        if [[ ! -f "$LIEBLER_D/$name.conf" ]]; then
            printf '# aus liebler.conf übernommen (frühere Einbindung)\ninclude %s;\n' "$datei" > "$LIEBLER_D/$name.conf"
            ok "Einbindung von $name nach $LIEBLER_D/$name.conf umgezogen"
        fi
        grep -vF -- "$zeile" "$LIEBLER_SITE" > "$LIEBLER_SITE.neu" && mv "$LIEBLER_SITE.neu" "$LIEBLER_SITE"
    done < <(grep -E '^\s*include\s+/etc/nginx/snippets/\S+\.conf;' "$LIEBLER_SITE" \
             | grep -vE 'liebler-zertifikat\.conf|vibevhs-(ohne-passwort|proxy)\.conf|-proxy\.conf' || true)
    # zugehörige alte Abschnittskommentare entfernen
    sed -i -E '/^\s*# ---------- (FraktionPerfekt \(Kit fraktionperfekt\)|Podium \(Live-Quiz\).*) ----------\s*$/d; /^\s*# ---------- Podium \(Live-Quiz\)/d' "$LIEBLER_SITE"
}

# liebler_app_setzen <app> <quelldatei>: eigene Datei nach liebler.d/<app>.conf (nur bei Änderung), Rest unangetastet.
liebler_app_setzen() {
    local app="$1" quelle="$2"
    [[ -f "$LIEBLER_SITE" ]] || fehler "$LIEBLER_SITE fehlt – erst das nginx-Grundgerüst einrichten (VibeVHS-Kit, Schritt 03)."
    [[ -f "$quelle" ]] || fehler "Vorlage fehlt: $quelle"
    if grep -qF "$LIEBLER_INCLUDE" "$LIEBLER_SITE" && [[ -f "$LIEBLER_D/$app.conf" ]] && cmp -s "$quelle" "$LIEBLER_D/$app.conf" \
       && ! grep -qE '^\s*include\s+/etc/nginx/snippets/\S+-locations\.conf;|snippets/podium\.conf;' "$LIEBLER_SITE"; then
        ok "nginx: $LIEBLER_D/$app.conf unverändert"; return 0
    fi
    _liebler_merken
    _liebler_include_setzen
    _liebler_altlasten_umziehen
    install -m 0644 "$quelle" "$LIEBLER_D/$app.conf"
    ok "nginx: $LIEBLER_D/$app.conf eingespielt"
    _liebler_abschliessen
}

# liebler_app_entfernen <app>: eigene Datei entfernen (Rückweg).
liebler_app_entfernen() {
    local app="$1"
    [[ -f "$LIEBLER_D/$app.conf" ]] || { ok "nginx: $app war nicht eingebunden"; return 0; }
    _liebler_merken
    rm -f "$LIEBLER_D/$app.conf"
    ok "nginx: $LIEBLER_D/$app.conf entfernt"
    _liebler_abschliessen
}

# liebler_grundgeruest <vorlage> [<app>=<quelldatei> …]: Grundgerüst einspielen bzw. aktualisieren, optional zusammen mit
# eigenen App-Dateien (in EINER Transaktion, damit die Pfade keinen Moment fehlen). Weil Apps nur noch in liebler.d/ stehen,
# enthält liebler.conf keine fremden Einträge; ältere Einbindungen werden vorher nach liebler.d/ umgezogen – nichts geht verloren.
liebler_grundgeruest() {
    local vorlage="$1" paar app quelle; shift
    [[ -f "$vorlage" ]] || fehler "Vorlage fehlt: $vorlage"
    _liebler_merken
    install -d -m 0755 "$LIEBLER_D"
    if [[ -f "$LIEBLER_SITE" ]]; then _liebler_altlasten_umziehen; fi
    if [[ -f "$LIEBLER_SITE" ]] && cmp -s "$vorlage" "$LIEBLER_SITE"; then ok "unverändert: $LIEBLER_SITE"
    else install -D -m 0644 "$vorlage" "$LIEBLER_SITE"; ok "eingespielt: $LIEBLER_SITE (Grundgerüst)"; fi
    _liebler_include_setzen
    for paar in "$@"; do
        app="${paar%%=*}"; quelle="${paar#*=}"
        [[ -f "$quelle" ]] || { _liebler_zurueck; fehler "Vorlage fehlt: $quelle"; }
        if [[ -f "$LIEBLER_D/$app.conf" ]] && cmp -s "$quelle" "$LIEBLER_D/$app.conf"; then ok "nginx: $LIEBLER_D/$app.conf unverändert"
        else install -m 0644 "$quelle" "$LIEBLER_D/$app.conf"; ok "nginx: $LIEBLER_D/$app.conf eingespielt"; fi
    done
    _liebler_abschliessen
}
