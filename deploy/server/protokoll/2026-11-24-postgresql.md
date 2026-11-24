# 2026-11-24 – Schritt 09: PostgreSQL auf liebler

Anlass: Klaus gibt am 24.11.2026 (22:16) das Go für „PostgreSQL auf liebler“ (Schritt 09, danach `deploy.ps1 -DbNeu`).

Vorab ergänzt: Schritt 09 spielt jetzt auch `systemd/vibevhs.service` neu ein (`Sicherung__PgBin`, `Kommentare__Altdatei`),
sonst bliebe die Unit aus Schritt 06 ohne diese Einträge.

## Befehl 1 (lesend, ohne sudo)

```
ssh liebler 'lsb_release -ds; df -h / | tail -1; free -m | sed -n 2p; command -v docker; docker --version 2>&1; systemctl is-active vibevhs snap.docker.dockerd; ls -la /var/lib/vibevhs 2>&1 | head; apt-cache policy postgresql-client-17 2>&1 | head -4; ss -ltn | grep -E ":5441\b" || echo "5441 frei"'
```

Ausgabe (gekürzt): Ubuntu 26.04.1 LTS, / 41 GB frei, 3,9 GB RAM, Docker 29.8.0 (snap), vibevhs und dockerd aktiv,
`apt-cache policy postgresql-client-17` leer (nicht in der Distribution), 5441 frei.

## Befehl 2 (lesend, ohne sudo)

```
ssh liebler 'lsb_release -cs; apt-cache policy postgresql-client postgresql-client-18 2>&1 | head -12; ls /usr/lib/postgresql 2>&1'
```

Ausgabe: Codename `resolute`; Distribution führt nur `postgresql-client-18` (18.6), kein 17; `/usr/lib/postgresql` fehlt.

## Befehl 3 (lesend, ohne sudo)

```
ssh liebler 'curl -sI https://apt.postgresql.org/pub/repos/apt/dists/resolute-pgdg/Release | head -1; curl -s https://apt.postgresql.org/pub/repos/apt/dists/resolute-pgdg/main/binary-amd64/Packages.gz | gzip -dc | grep -c "^Package: postgresql-client-17$"'
```

Ausgabe: `HTTP/2 200`, 3 Einträge `postgresql-client-17` → apt.postgresql.org führt Ubuntu 26.04; Schritt 09 kann so laufen.

## Befehl 4 (Kit nach /tmp hochladen, ohne sudo)

```
.\deploy\server\deploy.ps1 -NurKit
```

Ausgabe: Kit hochgeladen; geprüft: `09-postgresql.sh` mit Abschnitt 3b, Unit mit `Sicherung__PgBin`, Skripte mit LF.
(Vorher in der Arbeitskopie `ausrollen.sh` und `vibevhs.service` mit CRLF – auf LF zurückgesetzt, im Repo waren sie schon LF.)

## Befehl 5 und 6 (ändernd, mit sudo – führt Klaus aus, da sudo das Passwort von srvadmin braucht)

```
ssh -t liebler "sudo bash /tmp/vibevhs-kit/schritte/09-postgresql.sh"
.\deploy\server\deploy.ps1 -DbNeu
```

## Befehl 7 (ändernd, mit sudo – Klaus' Go 24.11.2026 22:22, führt Klaus aus): Info-Monitore ohne Zugangspasswort

Snippet aus dem Kit einspielen, vorher sichern; bei Fehler in `nginx -t` die Sicherung zurück, sonst nginx neu laden.

```
ssh -t liebler "sudo bash -c 'f=/etc/nginx/snippets/vibevhs-ohne-passwort.conf; cp -a \$f \$f.vor-monitor && install -m 0644 /tmp/vibevhs-kit/nginx/vibevhs-ohne-passwort.conf \$f && if nginx -t; then systemctl reload nginx && echo OK; else cp -a \$f.vor-monitor \$f; echo ZURUECK; fi'"
```

Vorab lesend verglichen (`diff` Server ↔ Kit): nur Zusätze – die Monitor-Zeile und auskommentierte Impressum-Zeilen; keine Änderung auf dem Server geht verloren.

Ergebnis: In PowerShell wurde `$f` trotz `\` vor dem Senden ersetzt (leer) → `cp: missing destination file operand`; wegen `&&` lief
nichts weiter, der Server blieb unverändert (lesend geprüft: Datei vom 16.11., ohne Monitor-Zeile). Neue Fassung ohne Variablen:

```
ssh -t liebler "sudo cp -a /etc/nginx/snippets/vibevhs-ohne-passwort.conf /etc/nginx/snippets/vibevhs-ohne-passwort.conf.vor-monitor && sudo install -m 0644 /tmp/vibevhs-kit/nginx/vibevhs-ohne-passwort.conf /etc/nginx/snippets/vibevhs-ohne-passwort.conf && sudo nginx -t && sudo systemctl reload nginx && echo OK"
```

Ergebnis (24.11.2026 22:24): Klaus meldet OK. Lesend geprüft: Monitor-Zeile vorhanden, Sicherung `.vor-monitor` liegt da; `/vibevhs/monitor/test` → 200 ohne Passwort, `/vibevhs/` → 401 (Passwort weiter aktiv).

## Befehl 8 (lesend, ohne sudo): Zustand nach Klaus' Ausrollen

```
ssh liebler 'systemctl is-active vibevhs; systemctl status vibevhs --no-pager | sed -n 1,12p; docker ps --format "{{.Names}} {{.Status}}" | grep vibevhs; curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:5080/vibevhs/gesund; ls -la /opt | grep vibevhs; ls /tmp/vibevhs-kit >/dev/null && echo kit'
```

Ausgabe: Dienst aktiv seit 23:51:10, /gesund → 200, /opt/vibevhs vom 23:51, vibevhs.vorher vom 22:20; docker ps ohne sudo nicht erlaubt.

## Befehl 9 (lesend): Verlauf des Dienstes heute

```
ssh liebler 'journalctl -u vibevhs --since "2026-11-24 22:00" --no-pager -o short | grep -E "Started|Stopped|Main process exited|FTL|ERR|Failed|Datenbank|Schema|Kommentar" | head -60'
```

## Befehl 10 (lesend, ohne sudo; nur eine kurzlebige Benutzer-Unit): gibt systemd-run --wait den Exitcode weiter?

```
ssh liebler 'systemd --version | head -1; systemd-run --user --quiet --wait --pipe --collect sh -c "echo hallo; exit 3"; echo "exit=$?"'
```

Ausgabe: `hallo`, `exit=3` → Exitcode wird weitergegeben.

## Befehl 11 (lesend)

```
ssh liebler 'systemctl show vibevhs -p Environment --value; systemctl show vibevhs -p NRestarts -p ActiveState -p SubState -p ExecMainStatus'
```

## Befehl 12 (ohne sudo, kurzlebige Benutzer-Unit): Umgebung des Dienstes an systemd-run weitergeben

```
ssh liebler 'systemd-run --user --quiet --wait --pipe --collect -p "Environment=$(systemctl show vibevhs -p Environment --value)" sh -c "echo \$ASPNETCORE_ENVIRONMENT \$Speicher__Logs \$ReverseProxy__Host"; echo exit=$?'
```

Ausgabe: `Testsystem /var/log/vibevhs liebler.iui.hs-osnabrueck.de`, exit=0 → die Umgebung kommt vollständig an.
