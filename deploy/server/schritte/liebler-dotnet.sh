# Gemeinsame .NET-Runtime auf liebler.iui.hs-osnabrueck.de – IDENTISCH in den Kits von vibevhs, fraktionperfekt, podium und
# profomat (wird per „source“ eingebunden). Bei Änderungen in allen Repos gleich halten.
#
# Die Apps werden framework-abhängig ausgerollt (ohne eigene Runtime: rund 20 statt 70 MB je Upload). Die ASP.NET-Core-Runtime
# installiert einmalig VibeVHS-Schritt 09 (09-dotnet-runtime.sh); Patches spielt dotnet-runtime-aktualisieren.timer nachts ein.
# Ein self-contained Paket (deploy.ps1 -MitRuntime) bringt seine Runtime weiterhin selbst mit und braucht nichts davon.
# Erwartet aus gemeinsam.sh: info, ok, warn, fehler.

# Wurzel der gemeinsamen Runtime – in derselben Reihenfolge, wie der Programmstarter (apphost) sie sucht.
dotnet_wurzel() {
    local w
    for w in "${DOTNET_ROOT:-}" "$(head -1 /etc/dotnet/install_location 2>/dev/null || true)" /usr/lib/dotnet /usr/share/dotnet; do
        if [[ -n "$w" && -d "$w/shared/Microsoft.AspNetCore.App" ]]; then
            echo "$w"
            return 0
        fi
    done
    return 1
}

# Ist eine ASP.NET-Core-Runtime dieser Version (z. B. „11.0“) installiert?
dotnet_hat() {
    local w
    w=$(dotnet_wurzel) || return 1
    compgen -G "$w/shared/Microsoft.AspNetCore.App/$1.*" >/dev/null
}

# Welche ASP.NET-Core-Version braucht ein Build (aus <app>.runtimeconfig.json), z. B. „11.0“; leer bei self-contained.
dotnet_bedarf() {   # <programmordner> <app>
    local cfg="$1/$2.runtimeconfig.json"
    [[ -f "$cfg" ]] || return 0
    if grep -q '"includedFrameworks"' "$cfg"; then
        return 0   # self-contained: Runtime liegt im Paket
    fi
    grep -A2 '"Microsoft.AspNetCore.App"' "$cfg" | sed -n 's/.*"version": *"\([0-9]*\.[0-9]*\).*/\1/p' | head -1
}

# Vor dem Ausrollen: passt die Runtime auf dem Server zum Paket? 0 = ja (oder self-contained), sonst Meldung und 1.
dotnet_pruefen() {  # <programmordner> <app>
    local bedarf
    bedarf=$(dotnet_bedarf "$1" "$2")
    if [[ -z "$bedarf" ]]; then
        ok "Paket bringt seine Runtime selbst mit (self-contained)"
        return 0
    fi
    if dotnet_hat "$bedarf"; then
        ok "gemeinsame ASP.NET-Core-Runtime $bedarf vorhanden ($(dotnet_wurzel))"
        return 0
    fi
    warn "Die ASP.NET-Core-Runtime $bedarf fehlt auf dem Server. Einmalig einrichten (VibeVHS-Kit):"
    warn "  .\\deploy\\server\\deploy.ps1 -NurKit   (im vibevhs-Repo), dann: ssh -t liebler \"sudo bash /tmp/vibevhs-kit/schritte/09-dotnet-runtime.sh\""
    warn "Oder dieses Paket mit eigener Runtime bauen: deploy.ps1 -MitRuntime"
    return 1
}
