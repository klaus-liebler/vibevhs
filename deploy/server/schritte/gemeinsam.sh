# Gemeinsame Hilfen für die Schritt-Skripte (wird per „source“ eingebunden, nicht direkt starten).
# Alle Schritte: idempotent, Sicherung vorher (unter /root/vibevhs-sicherung/<Zeitstempel>), Prüfung danach.
set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST="liebler.iui.hs-osnabrueck.de"
ADMIN="srvadmin"
SICHERUNG="/root/vibevhs-sicherung/$(date +%Y%m%d-%H%M%S)-$(basename "$0" .sh)"

info()  { printf '\033[1;34m»\033[0m %s\n' "$*"; }
ok()    { printf '\033[1;32m✓\033[0m %s\n' "$*"; }
warn()  { printf '\033[1;33m!\033[0m %s\n' "$*"; }
fehler(){ printf '\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

als_root() { [[ $EUID -eq 0 ]] || fehler "Bitte mit sudo starten: sudo bash $0"; }

# Datei/Verzeichnis vor einer Änderung sichern (falls vorhanden)
sichern() {
    mkdir -p "$SICHERUNG"
    for p in "$@"; do
        if [[ -e "$p" ]]; then
            cp -a --parents "$p" "$SICHERUNG/"
            info "gesichert: $p → $SICHERUNG$p"
        fi
    done
}

# Datei aus dem Kit an ihren Platz bringen, nur wenn sie sich unterscheidet; liefert 0 bei Änderung
einspielen() {
    local quelle="$KIT/$1" ziel="$2" rechte="${3:-0644}"
    [[ -f "$quelle" ]] || fehler "Kit-Datei fehlt: $quelle"
    if [[ -f "$ziel" ]] && cmp -s "$quelle" "$ziel"; then
        ok "unverändert: $ziel"
        return 1
    fi

    sichern "$ziel"
    install -D -m "$rechte" "$quelle" "$ziel"
    ok "eingespielt: $ziel"
    return 0
}

frage() {
    local antwort
    read -r -p "$1 [j/N] " antwort </dev/tty
    [[ "$antwort" =~ ^[jJyY]$ ]]
}
