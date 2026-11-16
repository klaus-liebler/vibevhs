#!/usr/bin/env bash
# Rückweg zu Schritt 05: vorheriges Zertifikat-Snippet (z. B. Hochschul-Zertifikat) wiederherstellen.
source "$(dirname "$0")/gemeinsam.sh"
als_root
letzte="$(ls -1d /root/vibevhs-sicherung/*-05-zertifikat 2>/dev/null | tail -1)"
[[ -n "$letzte" && -f "$letzte/etc/nginx/snippets/liebler-zertifikat.conf" ]] || fehler "Keine Sicherung des Zertifikat-Snippets gefunden."
cp -a "$letzte/etc/nginx/snippets/liebler-zertifikat.conf" /etc/nginx/snippets/liebler-zertifikat.conf
nginx -t && systemctl reload nginx
ok "Vorheriges Zertifikat wieder aktiv. (Das Let's-Encrypt-Zertifikat bleibt in /etc/letsencrypt; löschen mit: certbot delete --cert-name $HOST)"
