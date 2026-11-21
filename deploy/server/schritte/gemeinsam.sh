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

# ---------- Hochschulzertifikat (GEANT/HARICA), zentral für nginx und mosquitto ----------
ZERT_DIR="/etc/ssl/liebler"
ZERT_VOLL="$ZERT_DIR/fullchain.pem"   # Serverzertifikat + Zwischenzertifikate
ZERT_KEY="$ZERT_DIR/privkey.pem"      # root:ssl-cert 0640

# zert_pruefen <fullchain> <schlüssel>: Hostname, Ablauf, Schlüssel passt; gibt Aussteller und Ablauf aus
zert_pruefen() {
    local voll="$1" key="$2"
    [[ -s "$voll" && -s "$key" ]] || fehler "Zertifikat oder Schlüssel fehlt: $voll / $key"
    openssl x509 -noout -in "$voll" >/dev/null 2>&1 || fehler "$voll ist kein PEM-Zertifikat"
    local a b
    a=$(openssl x509 -noout -pubkey -in "$voll" | openssl pkey -pubin -outform DER 2>/dev/null | sha256sum)
    b=$(openssl pkey -in "$key" -pubout -outform DER 2>/dev/null | sha256sum)
    [[ -n "$a" && "$a" == "$b" ]] || fehler "Der Schlüssel $key passt NICHT zum Zertifikat $voll"
    openssl x509 -noout -checkhost "$HOST" -in "$voll" | grep -q "does match" || fehler "Zertifikat gilt nicht für $HOST"
    openssl x509 -noout -checkend 0 -in "$voll" >/dev/null || fehler "Zertifikat ist abgelaufen"
    [[ $(grep -c 'BEGIN CERTIFICATE' "$voll") -ge 2 ]] || warn "$voll enthält keine Zwischenzertifikate (Kette) – manche Clients lehnen das ab"
    ok "Zertifikat passt: $(openssl x509 -noout -subject -in "$voll" | sed 's/^subject=//')"
    info "   Aussteller: $(openssl x509 -noout -issuer -in "$voll" | sed 's/^issuer=//') · gültig bis $(openssl x509 -noout -enddate -in "$voll" | cut -d= -f2)"
}

# zert_ablegen <fullchain> <schlüssel>: prüfen und nach $ZERT_DIR legen (vorher sichern); liefert 0 bei Änderung
zert_ablegen() {
    zert_pruefen "$1" "$2"
    getent group ssl-cert >/dev/null || groupadd --system ssl-cert
    install -d -o root -g ssl-cert -m 0750 "$ZERT_DIR"
    if [[ -f "$ZERT_VOLL" ]] && cmp -s "$1" "$ZERT_VOLL" && cmp -s "$2" "$ZERT_KEY"; then ok "unverändert: $ZERT_DIR"; return 1; fi
    sichern "$ZERT_DIR"
    install -o root -g ssl-cert -m 0644 "$1" "$ZERT_VOLL.neu" && mv -f "$ZERT_VOLL.neu" "$ZERT_VOLL"
    install -o root -g ssl-cert -m 0640 "$2" "$ZERT_KEY.neu" && mv -f "$ZERT_KEY.neu" "$ZERT_KEY"
    ok "abgelegt: $ZERT_VOLL, $ZERT_KEY (root:ssl-cert, Schlüssel 0640)"
    return 0
}

# Nutzt mosquitto das Hochschulzertifikat? (dann nach einem Tausch neu starten)
mosquitto_nutzt_zert() { grep -qsE "^\s*certfile\s+$ZERT_VOLL\s*$" /etc/mosquitto/mosquitto.conf /etc/mosquitto/conf.d/*.conf; }
