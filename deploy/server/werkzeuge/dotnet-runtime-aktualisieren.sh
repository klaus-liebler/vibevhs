#!/usr/bin/env bash
# Nächtlich (dotnet-runtime-aktualisieren.timer, 03:40): Patches der gemeinsamen ASP.NET-Core-Runtime einspielen und – nur wenn
# eine neuere Version installiert ist, als die Dienste zuletzt geladen haben – die .NET-Dienste neu starten.
# Ziel: /usr/local/sbin/dotnet-runtime-aktualisieren.sh (VibeVHS-Schritt 09). Einstellungen: /etc/default/dotnet-runtime-aktualisieren.
set -euo pipefail
# shellcheck source=/dev/null
. /etc/default/dotnet-runtime-aktualisieren   # KANAL, ART, DIENSTE
STAND=/var/lib/dotnet-runtime-aktualisieren/geladen

wurzel() {
    local w
    for w in "$(head -1 /etc/dotnet/install_location 2>/dev/null || true)" /usr/lib/dotnet /usr/share/dotnet; do
        if [[ -n "$w" && -d "$w/shared/Microsoft.AspNetCore.App" ]]; then echo "$w"; return 0; fi
    done
    return 1
}
neueste() { ls "$(wurzel)/shared/Microsoft.AspNetCore.App" 2>/dev/null | grep "^$KANAL\." | sort -V | tail -1; }

case "$ART" in
    apt|backports)
        apt-get update -q >/dev/null
        DEBIAN_FRONTEND=noninteractive apt-get install -y -q -o Acquire::Retries=5 --only-upgrade "aspnetcore-runtime-$KANAL" >/dev/null
        ;;
    microsoft)
        werkzeug=$(mktemp)
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$werkzeug"
        bash "$werkzeug" --channel "$KANAL" --runtime aspnetcore --install-dir /usr/share/dotnet --no-path >/dev/null
        rm -f "$werkzeug"
        ;;
esac

aktuell=$(neueste)
[[ -n "$aktuell" ]] || { echo "keine ASP.NET-Core-Runtime $KANAL gefunden" >&2; exit 1; }
install -d -m 0755 "$(dirname "$STAND")"
zuletzt=$(cat "$STAND" 2>/dev/null || true)
if [[ "$aktuell" == "$zuletzt" ]]; then
    echo "ASP.NET Core $aktuell – unverändert"
    exit 0
fi

echo "ASP.NET Core ${zuletzt:-?} → $aktuell: Dienste neu starten"
for d in $DIENSTE; do
    if systemctl is-active --quiet "$d"; then
        systemctl restart "$d" && echo "  neu gestartet: $d"
    fi
done
echo "$aktuell" > "$STAND"
