#!/usr/bin/env bash
# Schritt 09: gemeinsame ASP.NET-Core-Runtime für alle .NET-Apps des Servers (VibeVHS, FraktionPerfekt, Podium, ProfOMat)
# einmalig installieren und nachts aktuell halten. Danach rollen die Kits framework-abhängig aus (kleinere Uploads),
# Sicherheitspatches der Runtime kommen ohne neues Ausrollen.
#
# Start (Standard: Paket aus den signierten Ubuntu-Quellen):
#   ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/09-dotnet-runtime.sh"
# Liefert Ubuntu die Version nicht, eine der beiden bewusst gewählten Quellen:
#   … 09-dotnet-runtime.sh --backports   Canonical-PPA ppa:dotnet/backports (apt, signiert, Updates über apt)
#   … 09-dotnet-runtime.sh --microsoft   Microsofts dotnet-install.sh nach /usr/share/dotnet (Updates über das Skript)
# Andere Version: DOTNET_KANAL=12.0 vor den Aufruf setzen (sudo DOTNET_KANAL=12.0 bash …); mehrere Versionen dürfen nebeneinander liegen.
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/09-zurueck.sh   (Timer aus; Runtime bleibt, solange eine App sie braucht)
source "$(dirname "$0")/gemeinsam.sh"
source "$(dirname "$0")/liebler-dotnet.sh"
als_root

KANAL="${DOTNET_KANAL:-11.0}"
[[ "$KANAL" =~ ^[0-9]+\.[0-9]+$ ]] || fehler "DOTNET_KANAL muss wie 11.0 aussehen."
PAKET="aspnetcore-runtime-$KANAL"
DIENSTE="vibevhs fraktionperfekt podium profomat"
EINSTELLUNG=/etc/default/dotnet-runtime-aktualisieren

art=apt
case "${1:-}" in
    --backports) art=backports ;;
    --microsoft) art=microsoft ;;
    "") ;;
    *) fehler "Unbekannte Option ${1} (erlaubt: --backports, --microsoft)" ;;
esac
# Bei erneutem Aufruf ohne Option die einmal gewählte Quelle behalten
if [[ -z "${1:-}" && -f "$EINSTELLUNG" ]]; then
    art=$(sed -n 's/^ART=//p' "$EINSTELLUNG"); art=${art:-apt}
fi

. /etc/os-release
info "System: $PRETTY_NAME ($(uname -m)) · gewünscht: ASP.NET Core $KANAL · Quelle: $art"

if dotnet_hat "$KANAL"; then
    ok "ASP.NET Core $KANAL ist schon installiert ($(dotnet_wurzel))"
else
    case "$art" in
        apt|backports)
            if [[ "$art" == backports ]]; then
                command -v add-apt-repository >/dev/null || DEBIAN_FRONTEND=noninteractive apt-get install -y -q software-properties-common
                add-apt-repository -y ppa:dotnet/backports
            fi
            apt-get update -q
            # LC_ALL=C: apt antwortet sonst in der Systemsprache („Installationskandidat:“ statt „Candidate:“)
            # erst einlesen, dann prüfen: grep -q in der Pipe kann apt-cache unter pipefail per SIGPIPE scheitern lassen
            politik=$(LC_ALL=C apt-cache policy "$PAKET")
            if grep -qE 'Candidate: [0-9]' <<<"$politik"; then
                DEBIAN_FRONTEND=noninteractive apt-get install -y -q -o Acquire::Retries=5 "$PAKET"
            else
                info "Vorhandene ASP.NET-Core-Runtime-Pakete:"
                apt-cache search --names-only '^aspnetcore-runtime' | sed 's/^/   /'
                LC_ALL=C apt-cache policy "$PAKET" | sed 's/^/   /'
                fehler "$PAKET gibt es in den Paketquellen nicht. Erneut starten mit --backports (Canonical-PPA, bevorzugt) oder --microsoft. Nichts installiert."
            fi
            ;;
        microsoft)
            werkzeug=$(mktemp)
            curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$werkzeug"
            bash "$werkzeug" --channel "$KANAL" --runtime aspnetcore --install-dir /usr/share/dotnet --no-path
            rm -f "$werkzeug"
            # Programmstarter (apphost) der Apps finden die Runtime über install_location
            if [[ ! -f /etc/dotnet/install_location ]]; then
                install -d -m 0755 /etc/dotnet
                echo /usr/share/dotnet > /etc/dotnet/install_location
            fi
            [[ -e /usr/local/bin/dotnet ]] || ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
            ;;
    esac
    dotnet_hat "$KANAL" || fehler "Installation abgeschlossen, aber ASP.NET Core $KANAL nicht gefunden – bitte prüfen."
    ok "ASP.NET Core $KANAL installiert ($(dotnet_wurzel))"
fi

# Nächtliche Aktualisierung (Patches) und Neustart der betroffenen Dienste
if [[ -f "$EINSTELLUNG" ]]; then sichern "$EINSTELLUNG"; fi
printf '# Schritt 09 (vibevhs-Kit): Quelle und Version der gemeinsamen .NET-Runtime\nKANAL="%s"\nART="%s"\nDIENSTE="%s"\n' \
    "$KANAL" "$art" "$DIENSTE" > "$EINSTELLUNG"
einspielen werkzeuge/dotnet-runtime-aktualisieren.sh /usr/local/sbin/dotnet-runtime-aktualisieren.sh 0755 || true
neu=0
if einspielen systemd/dotnet-runtime-aktualisieren.service /etc/systemd/system/dotnet-runtime-aktualisieren.service; then neu=1; fi
if einspielen systemd/dotnet-runtime-aktualisieren.timer /etc/systemd/system/dotnet-runtime-aktualisieren.timer; then neu=1; fi
if [[ $neu -eq 1 ]]; then systemctl daemon-reload; fi
systemctl enable --now dotnet-runtime-aktualisieren.timer >/dev/null 2>&1
ok "nächtliche Aktualisierung aktiv (03:40 Uhr; startet nur bei neuer Runtime-Version die Dienste neu)"

info "Prüfung"
"$(dotnet_wurzel)/dotnet" --list-runtimes | sed 's/^/   /'
ok "Schritt 09 fertig – die Kits können jetzt framework-abhängig ausrollen (deploy.ps1 ohne -MitRuntime)"
