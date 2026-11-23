#!/usr/bin/env bash
# Rückweg zu Schritt 09: nächtliche Aktualisierung aus. Die Runtime selbst bleibt – sie wird erst mit --entfernen gelöscht,
# und auch dann nur, wenn keine App in /opt mehr framework-abhängig läuft (sonst startete sie nicht mehr).
# Start: sudo bash /tmp/vibevhs-kit/schritte/09-zurueck.sh [--entfernen]
source "$(dirname "$0")/gemeinsam.sh"
source "$(dirname "$0")/liebler-dotnet.sh"
als_root

systemctl disable --now dotnet-runtime-aktualisieren.timer 2>/dev/null || true
sichern /etc/systemd/system/dotnet-runtime-aktualisieren.service /etc/systemd/system/dotnet-runtime-aktualisieren.timer \
    /usr/local/sbin/dotnet-runtime-aktualisieren.sh /etc/default/dotnet-runtime-aktualisieren
rm -f /etc/systemd/system/dotnet-runtime-aktualisieren.service /etc/systemd/system/dotnet-runtime-aktualisieren.timer \
    /usr/local/sbin/dotnet-runtime-aktualisieren.sh
systemctl daemon-reload
ok "nächtliche Aktualisierung entfernt"

if [[ "${1:-}" != "--entfernen" ]]; then
    ok "Runtime bleibt installiert ($(dotnet_wurzel 2>/dev/null || echo 'keine')). Ganz entfernen: 09-zurueck.sh --entfernen"
    exit 0
fi

for cfg in /opt/*/*.runtimeconfig.json; do
    [[ -f "$cfg" ]] || continue
    if ! grep -q '"includedFrameworks"' "$cfg"; then
        fehler "$(dirname "$cfg") läuft framework-abhängig – erst mit deploy.ps1 -MitRuntime neu ausrollen. Runtime nicht entfernt."
    fi
done

art=$(sed -n 's/^ART=//p' "$SICHERUNG/etc/default/dotnet-runtime-aktualisieren" 2>/dev/null | tr -d '"' || true)
if [[ "$art" == microsoft ]]; then
    sichern /etc/dotnet/install_location
    rm -rf /usr/share/dotnet /usr/local/bin/dotnet
    if grep -qx /usr/share/dotnet /etc/dotnet/install_location 2>/dev/null; then rm -f /etc/dotnet/install_location; fi
else
    DEBIAN_FRONTEND=noninteractive apt-get remove -y -q 'aspnetcore-runtime-*' || true
fi
rm -f /etc/default/dotnet-runtime-aktualisieren
ok "Runtime entfernt"
