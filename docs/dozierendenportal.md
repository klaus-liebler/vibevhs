# Dozierendenportal und Kurs-QR (nach I9)

## Kurs-QR und Schnellanmeldung

- Jeder öffentliche Kurs hat einen QR-Code: auf der Kursseite als **PNG/SVG**, in der Kursliste als **Sammel-PDF** für das
  Programmheft (aktuelles Semester, optional je Fachbereich). Inhalt ist immer `…/k/{kursnummer}` – der Code bleibt gültig,
  auch wenn sich die Website ändert.
- `/k/{kursnummer}` ohne gemerktes Gerät: Weiterleitung auf die Kursseite (`Shop:KursSeite`, Standard
  `shop/kurs.html#kurs={nummer}`; für TYPO3 z. B. `https://www.vhs-egs.de/kurse/{slug}`) mit „In den Warenkorb“.
- **Schnellanmeldung**: mit gemerktem Gerät (normale Kamera-App) oder im persönlichen Check-in-Link (dort „Einchecken“
  und den Kurs-QR scannen) erscheint eine Bestätigungsseite mit Kurs, Preis (ggf. ermäßigt) und bisheriger Zahlart
  (Lastschrift nur mit aktivem Mandat). AGB und Datenschutz einmal bestätigen, dann „Verbindlich anmelden“ – dieselbe
  Buchungsprüfung, Reservierungen und Warteliste wie Tresen und Webshop.
- Bei kostenpflichtigen Kursen geht sofort eine Mail mit **Storno-Link** (`/k/storno/{token}`, nur als Hash gespeichert):
  24 Stunden kostenlos, danach nach den AGB-Fristen der Buchungsrichtlinie. So richtet ein weitergegebener Link keinen
  Schaden an. Alles wird protokolliert (Schnellanmeldung, Storno).

## Dozierendenportal `/portal/dozierende`

Mobil zuerst, statisch gerendert (schnell, auch bei schlechtem Netz), interaktiv nur die Anwesenheitsliste.
Anmeldung: **Anmeldelink per Mail** (15 Minuten, einmal; die Seite verlangt einen Klick, damit Mail-Scanner den Link nicht
verbrauchen; fehlt das Benutzerkonto, wird es mit der Rolle „Dozierende“ angelegt) oder – im Testbetrieb – die
Testanmeldung (Demokonto `ruben.daubner`). Später: Servicekonto/OIDC der Kommune.

| Bereich | Inhalt |
|---|---|
| Termine | nächste vier Wochen mit Ort, Wegbeschreibung, Teilnehmerzahl; Raum-/Zeitänderungen und Ausfälle farbig hervorgehoben |
| Kalender | Abo-Adresse (ICS, persönliches widerrufbares Token; neue Adresse sperrt die alte) und Einzeltermin-Download |
| Termin | Anwesenheitsliste (Ausweis-Scan, Termin-QR, Abhaken, Lichtbild-Sichtvergleich bei IK) ohne Links in die Verwaltung; Nachricht an die Teilnehmenden des Termins (Postausgang mit Übersetzungslink); **Ausfall melden** → Aufgabe der Fachbereichsleitung, optional sofort Nachricht |
| Kurse & Unterlagen | Upload (PDF, Office, Bilder, MP3; max. 10 MB; Signaturprüfung; Virenprüfung als Port, Mock erkennt EICAR), Verteilung per Link an alle Angemeldeten, Abrufzähler, Zurückziehen sperrt den Link; abrufbar bis 60 Tage nach Kursende |
| Honorar | Entwürfe und festgeschriebene Abrechnungen mit Positionen, Gutschrift als PDF (Original aus dem Archiv), Einwand → Aufgabe Buchhaltung |
| Meine Daten | Anschrift ändern (sofort, Datenhistorie), Bankverbindung beantragen (**wirksam erst nach Freigabe durch die Buchhaltung, nie durch sich selbst**), Qualifikationsnachweis hochladen → Fachbereichsleitung prüft und trägt ein; Übersicht der eigenen Anliegen mit Status und Vermerk |
| Nächstes Semester | Verfügbarkeit/Wunschzeiten und Kursvorschläge → Aufgabe der Fachbereichsleitung (Fachbereich vorausgewählt) |

**Rechte:** Dozierende sehen nur Kurse, in deren Team sie stehen oder deren Termine sie leiten; fremde Kurse/Termine → 403.
Mit der aktiven Rolle „Dozierende“ leiten Verwaltungsseiten auf das Portal um, Verwaltungs-Downloads antworten 403.
Gesperrte Knöpfe erklären per Tooltip den Grund.

**Verwaltung:** „Anliegen Kursleitungen“ (`/portal-anliegen`) – jedes offene Anliegen ist zugleich eine Aufgabe
(Typen „Ausfall gemeldet“, „Anliegen an die Buchhaltung“, „Anliegen an die Fachbereichsleitung“); die Entscheidung
(Annehmen/Ablehnen mit Vermerk, Pflicht bei Ablehnung) schließt die Aufgabe und ist für die Kursleitung im Portal sichtbar.

Teilnehmerlisten im Portal zeigen nur Name und Status. Ein Feld „Notfallkontakt“ gibt es in VibeVHS bisher nicht; es wird
daher nicht angezeigt (offener Punkt, falls gewünscht: mit Einwilligung erfassen).
