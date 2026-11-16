#!/usr/bin/env bash
# Neues Hochschulzertifikat einspielen (jährlich): prüfen, nach /etc/ssl/liebler legen, nginx neu laden, mosquitto neu starten
# (falls es das Zertifikat nutzt), Ergebnis von außen prüfen. Das alte Zertifikat wird vorher gesichert.
# Vorher die Dateien hochladen, z. B.:  scp liebler.fullchain.pem liebler.key liebler:/tmp/
# Start:   ssh -t liebler "sudo bash /tmp/vibevhs-kit/werkzeuge/zertifikat-tauschen.sh /tmp/liebler.fullchain.pem /tmp/liebler.key"
#          Fehlt die Kette in der Zertifikatsdatei, als drittes Argument die Datei mit den Zwischenzertifikaten angeben.
# Rückweg: sudo bash /tmp/vibevhs-kit/werkzeuge/zertifikat-tauschen.sh --zurueck
source "$(dirname "$0")/../schritte/gemeinsam.sh"
als_root

neu_laden() {
    nginx -t && systemctl reload nginx && ok "nginx neu geladen"
    if mosquitto_nutzt_zert; then systemctl restart mosquitto && ok "mosquitto neu gestartet"; fi
    for p in 443 $(mosquitto_nutzt_zert && echo 8883 || true); do
        echo | openssl s_client -connect 127.0.0.1:$p -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer -enddate \
            | sed "s/^/   :$p /" || warn "Port $p liefert kein Zertifikat"
    done
}

if [[ "${1:-}" == "--zurueck" ]]; then
    letzte="$(ls -1d /root/vibevhs-sicherung/*-zertifikat-tauschen 2>/dev/null | tail -1)"
    [[ -n "$letzte" && -f "$letzte$ZERT_VOLL" ]] || fehler "Keine Sicherung eines vorigen Zertifikats gefunden."
    cp -a "$letzte$ZERT_DIR/." "$ZERT_DIR/"
    ok "vorheriges Zertifikat aus $letzte zurückgespielt"
    neu_laden; exit 0
fi

voll="${1:?Pfad zur Zertifikatsdatei (PEM) fehlt}"; key="${2:?Pfad zum privaten Schlüssel fehlt}"; kette="${3:-}"
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
cat "$voll" > "$tmp/fullchain.pem"; [[ -n "$kette" ]] && cat "$kette" >> "$tmp/fullchain.pem"
cp "$key" "$tmp/privkey.pem"
if zert_ablegen "$tmp/fullchain.pem" "$tmp/privkey.pem"; then
    neu_laden
    /usr/local/sbin/zertifikat-ablauf --kurz 2>/dev/null | sed 's/^/   Ablauf: /' || true
    warn "Die hochgeladenen Dateien (privater Schlüssel!) bitte löschen:  rm -f $voll $key ${kette}"
fi
