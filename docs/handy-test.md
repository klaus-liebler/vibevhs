# Handy-Test: Check-in mit der Handykamera im lokalen Netz

Die Kamera im Handy-Browser funktioniert nur über **HTTPS** (Ausnahme: `localhost`). Deshalb gibt es das Startprofil **`lan`**:

- Die Anwendung lauscht mit HTTPS auf allen Netzwerkschnittstellen, Port **7189**.
- Das Zertifikat ist selbst erzeugt. Es liegt außerhalb des Repos unter `%USERPROFILE%\.vibevhs\lan`.
- Die öffentliche Adresse (Links in Mails und QR-Codes) wird automatisch auf `https://<LAN-IP>:7189/` gesetzt.
- Das Profil nutzt eine **eigene Datenbank** (`src/VibeVHS.Web/data/vibevhs-lan.db`) und eigene Logs (`logs/lan`). Die normale Instanz läuft unverändert weiter.

## 1. Zertifikat erzeugen (einmalig, und erneut, wenn sich die LAN-IP ändert)

```bash
dotnet run tools/LanZertifikat.cs
```

Ausgabe: Ablageort, Gültigkeit (1 Jahr), enthaltene Adressen (localhost, Rechnername, LAN-IP) und Fingerabdruck.

- Das Zertifikat wird **nicht** in den Windows-Zertifikatspeicher eingetragen.
- Passwort und PFX-Datei liegen in `%USERPROFILE%\.vibevhs\lan`.

## 2. Starten

```bash
dotnet run --project src/VibeVHS.Web --launch-profile lan
```

- In der Konsole erscheint z. B. `LAN-Betrieb: https://192.168.2.118:7189/`.
- Der erste Start legt die Demodaten an und dauert etwa eine Minute.

## 3. Windows-Firewall (falls das Handy die Seite nicht erreicht)

Beim ersten Start fragt Windows eventuell, ob `dotnet.exe` im Netzwerk erreichbar sein darf. Erlauben Sie das nur für **private Netzwerke**.

Alternativ legt man in einer Administrator-PowerShell eine Regel an:

```powershell
New-NetFirewallRule -DisplayName "VibeVHS Handy-Test" -Direction Inbound -Protocol TCP -LocalPort 7189 -Profile Private -Action Allow
```

Nach dem Test wieder entfernen:

```powershell
Remove-NetFirewallRule -DisplayName "VibeVHS Handy-Test"
```

Handy und PC müssen im **selben WLAN/LAN** sein. Gäste-WLANs trennen Geräte oft voneinander.

## 4. Testablauf

### Vorbereitung am PC

1. `https://<LAN-IP>:7189` öffnen und die Zertifikatswarnung bestätigen („Erweitert → Weiter zu …“).
2. Damit ein Termin im Check-in-Fenster liegt (30 Min. vor bis 30 Min. nach Beginn):
   1. Als **Varvara Schatz** (Fachbereichsleitung) anmelden.
   2. Einen Kurs öffnen, z. B. *26H-301 Rhetorik*.
   3. Unter „Ändern“ eine **Zusätzliche Einzelveranstaltung** für heute anlegen, Beginn in ca. 10 Minuten.
3. Als **Katja Friesen** (Teilnehmerservice) über **Anmeldung** eine Person mit E-Mail-Adresse in diesen Kurs anmelden, z. B. *Sabine Weber*.
4. **Postausgang** öffnen und die Anmeldebestätigung anklicken. Unter dem Mailtext steht „Persönlichen Check-in-Link aufs Handy holen“: Der QR-Code enthält den persönlichen Link.
5. **Anwesenheit** öffnen, den Termin wählen und auf **Termin-QR A 1.12** klicken. Der QR-Code bleibt auf dem Bildschirm stehen.

### Weg A am Handy (Teilnehmende)

1. Den QR-Code des persönlichen Links mit der Handykamera scannen und die Seite öffnen.
2. Die Zertifikatswarnung **einmal** bestätigen.
3. Auf **„Einchecken“** tippen und den Kamerazugriff erlauben.
4. Den Termin-QR am PC-Bildschirm scannen. Erwartet wird: „✔ Eingecheckt – viel Spaß!“.
5. Zur Gegenprobe:
   - Ein zweiter Scan meldet „Bereits eingecheckt“.
   - Außerhalb des Zeitfensters kommt „gilt erst ab …“ bzw. „abgelaufen“.
6. Komfort: Auf der persönlichen Seite **„Dieses Gerät merken“** tippen. Danach reicht es, den Termin-QR mit der normalen Kamera-App zu scannen.
7. Am PC zeigt die Anwesenheitsliste die Person als *anwesend* mit dem Weg *Raum-QR*.

### Weg B am Handy (Kursleitung)

1. Am PC die Personenseite öffnen und unter „Dokumente“ den **Teilnehmerausweis** drucken bzw. als PDF öffnen. Oder den Anhang `checkin-ausweis.svg` der Bestätigungsmail öffnen.
2. Am Handy `https://<LAN-IP>:7189` öffnen und als Kursleitung oder Teilnehmerservice anmelden.
3. Unter **Anwesenheit** den Termin öffnen und **„Ausweise scannen“** wählen.
4. Den Ausweis-QR am Bildschirm oder auf Papier scannen.

### Abrechnungsrelevante Kurse

- Bei Integrationskursen ist der Selbst-Check-in auf „mit Bestätigung“ gestellt. Weg A merkt die Anwesenheit nur vor; die Kursleitung bestätigt sie in der Liste.
- In der Kursansicht lässt sich die Einstellung ändern: erlaubt / mit Bestätigung / aus.

## Hinweise

- Die Instanz im Profil `lan` ist nur für Tests im eigenen Netz gedacht und nicht für das Internet.
- Personen-Links, Geräte und Ausweise lassen sich auf der Personenseite unter „Datenschutz → Check-in-Zugänge“ sperren.
- Endet der Test, das Profil mit Strg+C beenden. Die Test-Datenbank `data/vibevhs-lan.db` kann gelöscht werden; beim nächsten Start entsteht sie neu.
