# Abnahme Phase A – Klick-Drehbuch für die vhs Emsdetten Greven Saerbeck

**Dauer:** 30–45 Minuten · **Umfang:** Iterationen 0–3 (Fundament, Personen, Programm, Datenschutz, Anmeldung)
**Adresse:** die lokale Testinstanz (z. B. `http://localhost:5189`) · **Daten:** ausschließlich fiktive Demodaten

So geht's:

- Auf der Seite **Anmelden** das genannte Testkonto anklicken. Ein Passwort gibt es nicht.
- Mit **Konto wechseln** (oben rechts) wechseln Sie zum Testkonto der nächsten Aufgabe.
- Alle Mails werden nur simuliert und landen im **Postausgang**. Nichts verlässt das System.
- Bitte tragen Sie zu jeder Aufgabe ein, ob sie geklappt hat und was Ihnen auffällt: unklare Begriffe, fehlende Schritte, Wünsche.
- Ausgegraute Schaltflächen erklären beim Darüberfahren mit der Maus (oder beim Ansteuern mit der Tab-Taste), warum sie gesperrt sind.
  Bitte prüfen Sie, ob diese Erklärungen verständlich sind.

| Nr. | Rolle / Testkonto | Zeit |
|---|---|---|
| 1 | Teilnehmerservice · Katja Friesen | 3 min |
| 2 | Teilnehmerservice · Katja Friesen | 4 min |
| 3 | Teilnehmerservice · Katja Friesen | 5 min |
| 4 | Teilnehmerservice · Katja Friesen | 5 min |
| 5 | Teilnehmerservice · Katja Friesen | 4 min |
| 6 | Teilnehmerservice · Katja Friesen | 5 min |
| 7 | Integrationskurs-Team · Sylvia Beckmann | 3 min |
| 8 | Fachbereichsleitung · Varvara Schatz | 6 min |
| 9 | Verwaltungsleitung · Sonja Eickelkamp | 4 min |
| 10 | Verwaltungsleitung · Sonja Eickelkamp | 4 min |

---

## 1. Person finden und Datenhistorie lesen

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Eine Person schnell finden und nachvollziehen, wie sich ihre Daten verändert haben.
- **Schritte:**
  1. Links **Personen** öffnen.
  2. Im Suchfeld „kovalenko“ eingeben.
  3. *Olena Kovalenko* öffnen.
  4. Oben **Datenhistorie** anklicken.
- **Erwartetes Ergebnis:**
  - Die Person erscheint sofort beim Tippen.
  - Die Personenseite zeigt die aktuellen Stammdaten kompakt: Wohnadresse, abweichende Korrespondenzadresse (c/o), Mobilnummer.
  - Die Datenhistorie zeigt Umzug, c/o-Adresse und die Signal-Einwilligung, jeweils mit Datum, Grund und Bearbeiter.
- **Anmerkungen:**

  &nbsp;

## 2. Neue Anmeldung am Tresen (3 Schritte)

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Eine Person in drei Schritten zu einem Kurs anmelden.
- **Schritte:**
  1. Links **Anmeldung** öffnen.
  2. „Weber Sabine“ suchen und die Person wählen.
  3. **Weiter zur Kursauswahl**.
  4. Den Kurs *26H-301 Rhetorik für Ehrenamtliche* wählen.
  5. **Weiter zur Zahlung**, Zahlart *Überweisung*, **Anmeldung speichern**.
- **Erwartetes Ergebnis:**
  - In der Kursauswahl sind nicht buchbare Kurse ausgegraut. Der Tooltip nennt den Grund und den Weg, zum Beispiel:
    - „Interner Kurs: Buchung nur über das Integrationskurs-Team“
    - „Der Kurs ist abgesagt“
    - „Geschlossener Kurs für Stadtwerke …“
  - Nach dem Speichern erscheint „Anmeldung gespeichert“ mit eingefrorenem Entgelt (49,00 €) und Hinweisen, wie es weitergeht:
    - Bestätigung im Postausgang
    - Kalenderdatei
    - Weitere Anmeldung
- **Anmerkungen:**

  &nbsp;

## 3. Bestätigung im Postausgang prüfen

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Die Anmeldebestätigung prüfen: Inhalt, Übersetzungslink, Kalenderdatei.
- **Schritte:**
  1. Unter „Wie geht es weiter?“ **Bestätigung im Postausgang ansehen** wählen (oder links **Postausgang** öffnen).
  2. Die Mail „Anmeldebestätigung: 26H-301 …“ anklicken.
  3. Den Anhang `26H-301-termine.ics` öffnen.
  4. Den Übersetzungslink anklicken.
- **Erwartetes Ergebnis:**
  - Die Mail enthält Kurs, nächsten Termin, Ort, Entgelt, Zahlart und die Abmeldefrist.
  - Am Ende steht der Link „Diese Nachricht in Ihrer Sprache lesen“.
  - Die Kalenderdatei lässt sich in Outlook bzw. im Kalender öffnen und enthält alle Termine.
- **Anmerkungen:**

  &nbsp;

## 4. Voller Kurs: Warteliste und Nachrückangebot

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Verstehen, wie Warteliste und Nachrücken funktionieren.
- **Schritte:**
  1. **Kurse** öffnen, dann *26H-801 Yoga am Abend* öffnen.
  2. Im Abschnitt „Teilnehmende“ die Warteliste ansehen (Sabine Weber auf Platz 1).
  3. Eine angemeldete Person anklicken.
  4. Auf ihrer Belegung einen Grund eingeben und **Abmelden (stornieren)**.
  5. Zurück zum Kurs.
- **Erwartetes Ergebnis:**
  - Vor dem Abmelden zeigt die Belegung die Storno-Regel, z. B. „Nach der Abmeldefrist …: das Entgelt bleibt fällig (AGB)“.
  - Nach dem Abmelden hat Sabine Weber ein **Nachrückangebot** mit Annahmefrist.
  - Im Postausgang liegt die Mail „Ein Platz ist frei …“.
  - Im Kurs *26H-702 Progressive Muskelentspannung* ist ein solches Angebot schon offen. Es lässt sich auf der Belegung annehmen oder ablehnen.
- **Anmerkungen:**

  &nbsp;

## 5. Umbuchen bei abgesagtem Kurs

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Teilnehmende eines abgesagten Kurses auf einen anderen Kurs umbuchen.
- **Schritte:**
  1. **Kurse** öffnen, dann *26H-504 Französisch B1* (abgesagt) öffnen.
  2. Eine Person aus „Teilnehmende“ anklicken.
  3. Bei „Umbuchen“ einen Vorschlag wählen.
- **Erwartetes Ergebnis:**
  - Es erscheinen nur buchbare, ähnliche Kurse (Fachbereich Sprachen).
  - Die Abmeldung vom abgesagten Kurs wäre kostenlos.
  - Nach dem Umbuchen öffnet sich die neue Belegung mit dem Entgelt des neuen Kurses. Die alte Belegung steht auf „umgebucht“.
- **Anmerkungen:**

  &nbsp;

## 6. Pilates mit 10er-Karte: in einen Termin einbuchen

- **Rolle / Testkonto:** Teilnehmerservice · *Katja Friesen*
- **Ziel:** Eine Person mit Kontingent in einzelne Veranstaltungen einbuchen.
- **Schritte:**
  1. Kurs *26H-704 Pilates fortlaufend (10er-Karte)* öffnen.
  2. Unter „Kontingente“ *Nadia Haddad* anklicken.
  3. Bei der übernächsten Veranstaltung **Einbuchen**.
  4. Danach die Zeile der nächsten Veranstaltung ansehen.
  5. Eine andere Person mit Kontingent öffnen und versuchen, sie bei einer schon vollen Veranstaltung einzubuchen.
- **Erwartetes Ergebnis:**
  - Oben stehen Kontingent, besucht, verfallen, eingebucht und verbleibend.
  - Nach dem Einbuchen sinkt „verbleibend“ um 1.
  - Die nächste Veranstaltung ist voll (14/14), Nadia steht dort auf der Warteliste. Trägt sich jemand aus, rückt sie automatisch nach und bekommt eine Mail.
  - Bei voller Veranstaltung heißt die Schaltfläche „Warteliste“.
  - Einbuchen und Austragen gehen bis 24 Stunden vor Beginn; danach sind die Schaltflächen gesperrt, der Tooltip nennt die Regel.
- **Anmerkungen:**

  &nbsp;

## 7. Integrationskurs: Buchung nur über das Integrationskurs-Team

- **Rolle / Testkonto:** Integrationskurs-Team · *Sylvia Beckmann*
- **Ziel:** Prüfen, dass interne Kurse nur über das zuständige Team buchbar sind.
- **Schritte:**
  1. **Anmeldung** öffnen und *Olena Kovalenko* wählen.
  2. Den Kurs *26H-401 Integrationskurs allgemein, Modul 3* suchen.
- **Erwartetes Ergebnis:**
  - Für das Integrationskurs-Team ist der Kurs buchbar (Zahlart z. B. Kostenträger).
  - Mit dem Konto Katja Friesen (Teilnehmerservice) ist derselbe Kurs gesperrt, mit dem Grund „nur über das Integrationskurs-Team“.
- **Anmerkungen:**

  &nbsp;

## 8. Kurs anlegen im Schnellweg und Raumplan prüfen

- **Rolle / Testkonto:** Fachbereichsleitung · *Varvara Schatz*
- **Ziel:** Einen regelmäßigen Kurs mit Ferien- und Feiertagsprüfung anlegen.
- **Schritte:**
  1. **Kurse** öffnen, dann **Kurs anlegen**.
  2. Einen Kurs anlegen: wöchentlich mittwochs 18:00, 10 Termine, Ort *A 1.12*, Entgelt 98 €.
  3. Die Vorschau ansehen und speichern.
  4. Den Kurs **veröffentlichen**.
  5. **Raumplan** öffnen.
- **Erwartetes Ergebnis:**
  - Die Vorschau zeigt ausgelassene Feiertage und Ferien mit Grund.
  - Ungeeignete Orte sind gesperrt, mit Begründung, z. B. fehlende Kapazität.
  - Konflikte werden rot angezeigt und erklärt.
  - Nach dem Veröffentlichen ist der Kurs am Tresen buchbar.
- **Anmerkungen:**

  &nbsp;

## 9. Vorlage einer Bestätigungsmail anpassen

- **Rolle / Testkonto:** Verwaltungsleitung · *Sonja Eickelkamp*
- **Ziel:** Eine Mailvorlage zentral ändern und die Vorschau prüfen.
- **Schritte:**
  1. Links **Vorlagen** öffnen und „Anmeldebestätigung“ wählen.
  2. Einen Satz ergänzen, z. B. Hinweis auf Hausschuhe.
  3. **Vorschau aktualisieren**, dann **Speichern**.
- **Erwartetes Ergebnis:**
  - Die Vorschau zeigt den Text mit Beispielwerten.
  - Unbekannte Platzhalter wie `{Foo}` werden beim Speichern mit einer Erklärung abgelehnt.
  - Mit dem Konto Katja Friesen ist **Speichern** gesperrt, mit Begründung.
- **Anmerkungen:**

  &nbsp;

## 10. Datenschutz: Auskunft und Protokoll

- **Rolle / Testkonto:** Verwaltungsleitung · *Sonja Eickelkamp*
- **Ziel:** Eine Auskunft nach Art. 15 DSGVO erstellen und das Protokoll prüfen.
- **Schritte:**
  1. Person *Mehmet Yılmaz* öffnen.
  2. Unten im Bereich „Datenschutz“ **Auskunft als PDF** wählen.
  3. Danach links **Protokoll** öffnen.
- **Erwartetes Ergebnis:**
  - Das PDF enthält Stammdaten, Historie, Kursbelegungen, Nachrichten und Aufbewahrungsfristen.
  - Im Protokoll stehen die Einträge zur Anmeldung und zur Auskunft. Gespeichert sind dort nur Pseudonyme („Person #…“), der Name erscheint nur in der Anzeige („Bezug: …“).
- **Anmerkungen:**

  &nbsp;

---

## Gesamteindruck

| Frage | Antwort |
|---|---|
| Was hat besonders gut funktioniert? | |
| Wo haben Sie gezögert oder gesucht? | |
| Welche Begriffe passen nicht zu Ihrem Arbeitsalltag? | |
| Was fehlt für den täglichen Einsatz am Tresen am dringendsten? | |
| Datum, Name, Funktion | |
