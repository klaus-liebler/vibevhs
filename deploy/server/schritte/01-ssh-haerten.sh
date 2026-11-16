#!/usr/bin/env bash
# Schritt 01: SSH nur noch mit Schlüssel, kein root-Login, nur srvadmin.
# Voraussetzung: Anmeldung mit Schlüssel klappt bereits (Runbook Schritt A). Bitte eine ZWEITE Sitzung offen lassen (Rettungsanker).
# Start: ssh -t srvadmin@liebler.iui.hs-osnabrueck.de "sudo bash /tmp/vibevhs-kit/schritte/01-ssh-haerten.sh"
# Rückweg: sudo bash /tmp/vibevhs-kit/schritte/01-zurueck.sh
source "$(dirname "$0")/gemeinsam.sh"
als_root

schluessel="/home/$ADMIN/.ssh/authorized_keys"
[[ -s "$schluessel" ]] || fehler "$schluessel fehlt oder ist leer – erst den öffentlichen Schlüssel übertragen (Runbook Schritt A)."
grep -qE '^(ssh-ed25519|ecdsa-sha2|ssh-rsa) ' "$schluessel" || fehler "In $schluessel steht kein gültiger öffentlicher Schlüssel."
ok "öffentlicher Schlüssel für $ADMIN vorhanden ($(grep -cE '^(ssh-|ecdsa)' "$schluessel") Eintrag/Einträge)"

# Rechte, sonst lehnt sshd den Schlüssel ab
chown -R "$ADMIN:$ADMIN" "/home/$ADMIN/.ssh"
chmod 700 "/home/$ADMIN/.ssh"
chmod 600 "$schluessel"

# Wurde in DIESER Sitzung mit Schlüssel angemeldet? (Hinweis, kein Zwang – die Prüfung erfolgt durch den Test unten)
if journalctl -u ssh --since "-2h" 2>/dev/null | grep -q "Accepted publickey for $ADMIN"; then
    ok "Anmeldung von $ADMIN mit Schlüssel in den letzten 2 Stunden gefunden"
else
    warn "Keine Schlüssel-Anmeldung von $ADMIN in den letzten 2 Stunden im Protokoll gefunden."
    frage "Trotzdem fortfahren (Passwort-Anmeldung wird abgeschaltet)?" || fehler "Abgebrochen."
fi

einspielen ssh/10-vibevhs.conf /etc/ssh/sshd_config.d/10-vibevhs.conf 0644 || true

info "Konfiguration prüfen (sshd -t)"
sshd -t || { warn "Fehler in der Konfiguration – nehme sie zurück."; rm -f /etc/ssh/sshd_config.d/10-vibevhs.conf; fehler "sshd -t fehlgeschlagen"; }
info "Wirksame Werte:"
sshd -T 2>/dev/null | grep -E '^(passwordauthentication|kbdinteractiveauthentication|permitrootlogin|pubkeyauthentication|allowusers) ' | sed 's/^/   /'

systemctl reload ssh 2>/dev/null || systemctl restart ssh
ok "sshd neu geladen. Bestehende Sitzungen bleiben offen."
cat <<HINWEIS

JETZT TESTEN (in einem NEUEN Fenster, diese Sitzung offen lassen):
  ssh -i \$env:USERPROFILE\\.ssh\\id_ed25519_liebler_iui_hs_osnabrueck $ADMIN@$HOST        → muss klappen (nur Passphrase)
  ssh -o PubkeyAuthentication=no $ADMIN@$HOST                           → muss abgelehnt werden
Falls die neue Anmeldung NICHT klappt: in dieser Sitzung  sudo bash $(dirname "$0")/01-zurueck.sh
HINWEIS
