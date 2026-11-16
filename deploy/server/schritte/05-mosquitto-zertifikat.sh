#!/usr/bin/env bash
# Schritt 05: mosquitto (8883) auf das Hochschulzertifikat aus /etc/ssl/liebler umstellen.
# Bisher nutzt mosquitto ein Zertifikat der eigenen Root-CA („AAA Klaus Liebler personal Root CA“). Laut Klaus vertrauen alle
# Geräte öffentlichen Wurzeln – sie akzeptieren damit auch das GEANT-/HARICA-Zertifikat.
# Start: ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/05-mosquitto-zertifikat.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/05-zurueck.sh  (vorige mosquitto-Konfiguration, eigenes Zertifikat)
source "$(dirname "$0")/gemeinsam.sh"
als_root
[[ -f "$ZERT_VOLL" ]] || fehler "$ZERT_VOLL fehlt – erst Schritt 03."
zert_pruefen "$ZERT_VOLL" "$ZERT_KEY"

mapfile -t dateien < <(grep -lE '^\s*certfile\s' /etc/mosquitto/mosquitto.conf /etc/mosquitto/conf.d/*.conf 2>/dev/null || true)
[[ ${#dateien[@]} -gt 0 ]] || fehler "Keine mosquitto-Konfiguration mit certfile gefunden."
if mosquitto_nutzt_zert; then ok "mosquitto nutzt schon $ZERT_VOLL"; exit 0; fi

info "Bisherige TLS-Einstellungen:"
grep -nE '^\s*(listener|port|cafile|capath|certfile|keyfile|require_certificate|use_identity_as_username|tls_version)\b' "${dateien[@]}" | sed 's/^/   /'
alt=$(grep -hoE '^\s*certfile\s+\S+' "${dateien[@]}" | awk '{print $2}' | head -1)
[[ -f "$alt" ]] && info "Bisheriges Zertifikat: $(openssl x509 -noout -subject -issuer -enddate -in "$alt" | tr '\n' ' ')"
if grep -qsE '^\s*require_certificate\s+true' "${dateien[@]}"; then
    info "require_certificate true: Geräte melden sich mit Client-Zertifikaten an – deren CA (cafile) bleibt UNVERÄNDERT."
fi
frage "mosquitto jetzt auf $ZERT_VOLL umstellen (kurzer Neustart, Geräte verbinden sich neu)?" || fehler "Abgebrochen – mosquitto unverändert."

sichern /etc/mosquitto
getent group ssl-cert >/dev/null && usermod -aG ssl-cert mosquitto && ok "Benutzer mosquitto in Gruppe ssl-cert (liest $ZERT_KEY)"
for f in "${dateien[@]}"; do
    sed -i -E "s|^(\s*)certfile\s+.*$|\1certfile $ZERT_VOLL|; s|^(\s*)keyfile\s+.*$|\1keyfile $ZERT_KEY|" "$f"
    ok "umgestellt: $f"
done

info "Prüfen: darf der Benutzer mosquitto Zertifikat und Schlüssel lesen?"
if ! sudo -u mosquitto -g ssl-cert test -r "$ZERT_KEY" || ! sudo -u mosquitto test -r "$ZERT_VOLL"; then
    cp -a "$SICHERUNG/etc/mosquitto/." /etc/mosquitto/; fehler "mosquitto kann $ZERT_KEY nicht lesen – Konfiguration zurückgespielt, nichts neu gestartet."
fi
ok "lesbar"
# mosquitto 2.x hat keinen reinen Konfigurationstest; Ersatz: Probestart auf einem freien lokalen Port mit derselben TLS-Konfiguration
probe=$(mktemp -d); chown mosquitto "$probe"
{ echo "per_listener_settings false"; echo "listener 18883 127.0.0.1"
  grep -hE '^\s*(cafile|capath|certfile|keyfile|require_certificate|use_identity_as_username|tls_version|ciphers)' "${dateien[@]}"; } > "$probe/probe.conf"
chown mosquitto "$probe/probe.conf"
sudo -u mosquitto -g ssl-cert timeout 4 mosquitto -c "$probe/probe.conf" >"$probe/log" 2>&1 & sleep 2
if echo | openssl s_client -connect 127.0.0.1:18883 -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer >/dev/null 2>&1; then
    ok "Probestart mit neuer TLS-Konfiguration klappt (Port 18883, nur lokal)"
else
    sed 's/^/   /' "$probe/log"; cp -a "$SICHERUNG/etc/mosquitto/." /etc/mosquitto/; rm -rf "$probe"
    fehler "Probestart fehlgeschlagen – Konfiguration zurückgespielt, mosquitto läuft unverändert."
fi
wait 2>/dev/null || true; rm -rf "$probe"

systemctl restart mosquitto; sleep 2
if systemctl is-active --quiet mosquitto && echo | openssl s_client -connect 127.0.0.1:8883 -servername "$HOST" 2>/dev/null | openssl x509 -noout -issuer | grep -qiE 'GEANT|HARICA|Hellenic'; then
    ok "mosquitto läuft mit dem Hochschulzertifikat (Prüfung über den Hostnamen):"
    echo | openssl s_client -connect "$HOST:8883" -servername "$HOST" 2>/dev/null | openssl x509 -noout -subject -issuer -enddate | sed 's/^/   /'
else
    warn "mosquitto startet nicht oder liefert das neue Zertifikat nicht – zurück zur vorigen Konfiguration"
    journalctl -u mosquitto -n 20 --no-pager | sed 's/^/   /'
    cp -a "$SICHERUNG/etc/mosquitto/." /etc/mosquitto/ && systemctl restart mosquitto
    fehler "Abgebrochen – mosquitto läuft wieder wie vorher."
fi
cat <<HINWEIS

Jetzt prüfen, ob die Geräte wieder Daten senden (z. B. Node-RED-Flows, journalctl -u mosquitto -f).
Wenn nicht:  sudo bash $(dirname "$0")/05-zurueck.sh
Künftige Zertifikatswechsel (werkzeuge/zertifikat-tauschen.sh) starten mosquitto automatisch mit neu.
HINWEIS
