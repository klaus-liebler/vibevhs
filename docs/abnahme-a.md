# Abnahme – Klick-Drehbuch für die vhs Emsdetten Greven Saerbeck (Stand nach Iteration 8)

**Teilnehmende:** 2–3 Verwaltungsmitarbeitende der vhs EGS (z. B. Teilnehmerservice, Fachbereichsleitung, Buchhaltung/Integrationskurs-Team)
**Dauer:** 60–90 Minuten · **Adresse:** die lokale Testinstanz (z. B. `http://localhost:5189`) · **Daten:** ausschließlich fiktive Demodaten

## Ablauf

1. **Begrüßung (5 min):** Ziel erklären: Wir testen die Software, nicht Sie. Fehler sind wertvolle Hinweise.
   Alles ist Testbetrieb: Mails landen nur im **Postausgang**, Zahlungen und BAMF-Meldungen sind simuliert.
2. **Lautes Denken (Think-aloud):** Bitte sagen Sie beim Klicken laut, was Sie suchen, erwarten und wundern.
   Die Moderation hilft nur, wenn jemand länger als zwei Minuten festsitzt, und notiert, wo das war.
3. **Aufgaben (45–70 min):** Reihenfolge wie unten. Jede Person übernimmt die Aufgaben „ihrer“ Rolle; Testkonten
   wechseln Sie oben rechts mit **Konto wechseln** (kein Passwort). Ausgegraute Schaltflächen erklären beim
   Darüberfahren (oder mit der Tab-Taste), warum sie gesperrt sind – bitte sagen Sie, ob die Erklärung verständlich ist.
4. **Nachgespräch (10–15 min)** mit den fünf Fragen am Ende.

Moderation: Pro Aufgabe die Zeit (Start bis „fertig“ oder Abbruch) und Beobachtungen eintragen. Abbruch nach 8 Minuten.

| Nr. | Aufgabe | Testkonto (Rolle) | Richtzeit |
|---|---|---|---|
| 1 | Person anlegen und Namensänderung | Katja Friesen (Teilnehmerservice) | 6 min |
| 2 | Kurs per Schnellweg anlegen | Varvara Schatz (Fachbereichsleitung) | 7 min |
| 3 | Tresenanmeldung mit Lastschrift | Katja Friesen (Teilnehmerservice) | 6 min |
| 4 | Voller Kurs: Warteliste | Katja Friesen (Teilnehmerservice) | 5 min |
| 5 | Anwesenheit erfassen (Check-in) | Katja Friesen (Teilnehmerservice) | 5 min |
| 6 | Bescheinigung nachdrucken | Katja Friesen (Teilnehmerservice) | 3 min |
| 7 | Aufgabenkorb: übernehmen und zuweisen | Varvara Schatz (Fachbereichsleitung) | 4 min |
| 8 | Mindestteilnehmerzahl: Kurs absagen | Varvara Schatz (Fachbereichsleitung) | 5 min |
| 9 | Rechnung stornieren | Linda Lüke (Buchhaltung) | 4 min |
| 10 | Lastschriftlauf freigeben | Linda Lüke (Buchhaltung) | 6 min |
| 11 | Integrationskurs: Ausweisprüfung und Kursbeginn | Sylvia Beckmann (Integrationskurs-Team) | 6 min |
| 12 | Auswertung exportieren | Sonja Eickelkamp (Verwaltungsleitung) | 3 min |

---

## 1. Person anlegen und Namensänderung

- **Testkonto:** Katja Friesen · **Startpunkt:** Startseite → **Person anlegen**
- **Aufgabe:** Legen Sie *Mira Albers*, geboren 12.03.1992, mit E-Mail und Anschrift in Emsdetten an. Frau Albers hat
  geheiratet und heißt ab dem 1. des Monats *Mira Albers-Kühn* – tragen Sie die Namensänderung ein.
- **Erwartung:**
  - Beim Anlegen erscheinen mögliche Dubletten, falls es ähnliche Personen gibt.
  - Die Namensänderung fragt: „Änderung ab einem Datum“ oder „Korrektur“; gewählt wird „Änderung ab“ mit Grund.
  - Unter **Datenhistorie** stehen beide Namen mit Gültigkeit; ältere Dokumente behalten den alten Namen.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 2. Kurs per Schnellweg anlegen

- **Testkonto:** Varvara Schatz · **Startpunkt:** **Kurse** → **Kurs anlegen**
- **Aufgabe:** Legen Sie für Fachbereich 7 den Kurs „Rückenfit am Abend“ an: mittwochs 18:00–19:30, 10 Termine,
  Ort *A 1.12*, 98 €, mindestens 6, höchstens 14 Personen. Prüfen Sie die Vorschau und veröffentlichen Sie den Kurs.
- **Erwartung:**
  - Die Vorschau zeigt ausgelassene Ferien und Feiertage mit Grund sowie Raumkonflikte.
  - Ungeeignete Orte und Kursleitungen sind gesperrt, mit Begründung (Tooltip).
  - Nach dem Veröffentlichen ist der Kurs am Tresen buchbar; der Raumplan zeigt ihn.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 3. Tresenanmeldung mit Lastschrift

- **Testkonto:** Katja Friesen · **Startpunkt:** Personenseite von *Mira Albers-Kühn* (Aufgabe 1)
- **Aufgabe:** Erfassen Sie für Frau Albers-Kühn ein SEPA-Lastschriftmandat (Bereich „SEPA-Lastschriftmandat“, IBAN
  `DE89 3704 0044 0532 0130 00`, heute unterschrieben). Melden Sie sie dann über **Anmeldung** im Kurs aus Aufgabe 2
  an, Zahlart **Lastschrift**.
- **Erwartung:**
  - Eine falsch abgetippte IBAN wird sofort mit Begründung abgelehnt.
  - Die Anmeldebestätigung liegt im **Postausgang**, mit Übersetzungslink, Kalenderdatei und persönlichem Check-in-Link.
  - Die Personenseite zeigt die Belegung und das Mandat mit Referenz (z. B. VHS-2026-…).
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 4. Voller Kurs: Warteliste

- **Testkonto:** Katja Friesen · **Startpunkt:** **Anmeldung**
- **Aufgabe:** Melden Sie *Sabine Weber* in *26H-801 Yoga am Abend* an (der Kurs ist ausgebucht). Melden Sie danach
  eine angemeldete Person des Kurses ab und sehen Sie nach, was mit der Warteliste passiert.
- **Erwartung:**
  - Die Anmeldung landet auf der Warteliste mit Platznummer; die Mail nennt den Wartelistenplatz.
  - Nach der Abmeldung bekommt Platz 1 ein Nachrückangebot mit Frist (Mail im Postausgang).
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 5. Anwesenheit erfassen (Check-in)

- **Testkonto:** Katja Friesen · **Startpunkt:** **Anwesenheit**
- **Aufgabe:** Öffnen Sie eine Veranstaltung von heute (falls heute keine stattfindet: im Kurs aus Aufgabe 2 unter
  „Ändern“ eine zusätzliche Einzelveranstaltung in 10 Minuten anlegen). Erfassen Sie zwei Personen als anwesend, eine
  davon 30 Minuten verspätet, und schließen Sie die Liste ab. Optional: **Termin-QR** anzeigen (Check-in mit dem Handy,
  siehe `docs/handy-test.md`).
- **Erwartung:**
  - Minuten und Unterrichtseinheiten werden je Person angezeigt.
  - Nach dem Abschließen gelten Nicht-Erfasste als „nicht anwesend“; Änderungen danach sind Nacherfassungen mit Grund.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 6. Bescheinigung nachdrucken

- **Testkonto:** Katja Friesen · **Startpunkt:** **Personen** → *Olena Kovalenko*
- **Aufgabe:** Olena braucht ihre Teilnahmebescheinigung für *25F-501* noch einmal. Drucken Sie sie nach.
- **Erwartung:**
  - Der Nachdruck ist das archivierte Original (Prüfsumme wird geprüft), mit dem QR-Code „Translate this letter in your language“.
  - Das Protokoll vermerkt den Nachdruck.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 7. Aufgabenkorb: übernehmen und zuweisen

- **Testkonto:** Varvara Schatz · **Startpunkt:** Startseite „Was steht an?“
- **Aufgabe:** Übernehmen Sie eine Aufgabe aus Ihrem Gruppenkorb. Weisen Sie eine zweite Aufgabe einer Kollegin zu.
  Prüfen Sie, warum „Erledigt“ bei einer nicht übernommenen Aufgabe gesperrt ist.
- **Erwartung:**
  - Übernommene Aufgaben wandern nach „Meine Aufgaben“, zugewiesene nach „Bei Kolleg:innen“.
  - Der Tooltip erklärt: „Bitte zuerst übernehmen …“.
  - Ein Klick auf den Titel öffnet die passende Seite.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 8. Mindestteilnehmerzahl: Kurs absagen

- **Testkonto:** Varvara Schatz · **Startpunkt:** Startseite, Aufgabe „26H-703 Gesund kochen mit wenig Geld: … absagen oder durchführen?“
- **Aufgabe:** Entscheiden Sie: Der Kurs wird abgesagt. Prüfen Sie anschließend den Postausgang.
- **Erwartung:**
  - Absagen ist erst nach „Übernehmen“ möglich (Tooltip).
  - Alle Teilnehmenden bekommen eine Absage mit Übersetzungslink; künftige Termine fallen aus; die Aufgabe ist erledigt.
  - Im Rechnungslauf werden bereits gestellte Rechnungen storniert.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 9. Rechnung stornieren

- **Testkonto:** Linda Lüke · **Startpunkt:** **Finanzen** → **Belege**
- **Aufgabe:** Eine Teilnehmerin hat aus Kulanz das Entgelt erlassen bekommen. Stornieren Sie ihre Rechnung (beliebige
  offene Rechnung wählen) mit Begründung.
- **Erwartung:**
  - Ohne Begründung ist „Stornieren“ gesperrt (Tooltip); die Rechnung selbst lässt sich nicht ändern oder löschen.
  - Es entsteht ein Stornobeleg mit eigener Nummer (ST-2026-…) und Verweis; das Personenkonto ist ausgeglichen.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 10. Lastschriftlauf freigeben

- **Testkonto:** Linda Lüke · **Startpunkt:** **Finanzen** → **Übersicht**
- **Aufgabe:** Führen Sie die Finanzroutinen aus (die Rechnung aus Aufgabe 3 entsteht), öffnen Sie dann
  **Lastschrift & Überweisung**, prüfen Sie den Vorschlag, geben Sie ihn frei und erzeugen Sie die SEPA-Datei.
- **Erwartung:**
  - Der Vorschlag enthält die Rechnung von Frau Albers-Kühn mit Mandatsreferenz und Sequenz FRST.
  - Mit der Freigabe gehen Vorabankündigungen raus (Postausgang); erst danach lässt sich die Datei erzeugen.
  - Die Datei (pain.008) lässt sich herunterladen.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 11. Integrationskurs: Ausweisprüfung und Kursbeginn

- **Testkonto:** Sylvia Beckmann · **Startpunkt:** **Integrationskurse** → *26H-401*
- **Aufgabe:** Der Kursstart ist gesperrt. Finden Sie heraus warum, prüfen Sie die fehlenden Ausweise (Original
  vorgelegt, stimmt mit der Berechtigung überein) und melden Sie den Kursbeginn an das BAMF.
- **Erwartung:**
  - Der Tooltip an „Kursbeginn melden“ nennt die Personen ohne Ausweisprüfung.
  - Gespeichert werden nur Datum, prüfende Person, Dokumentart und Ergebnis – keine Kopie.
  - Nach der Meldung steht eine Kursbeginnmeldung „BAMF (Mock)“ im Postausgang (nur Teilnehmernummern).
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

## 12. Auswertung exportieren

- **Testkonto:** Sonja Eickelkamp · **Startpunkt:** **Auswertungen**
- **Aufgabe:** Exportieren Sie die Auslastung je Fachbereich für das laufende Semester als Excel-Datei und öffnen Sie sie.
- **Erwartung:**
  - Die Tabelle erscheint sofort; der Export enthält echte Zahlen (Prozent, Euro) mit Autofilter.
  - Das Protokoll vermerkt den Export.
- **Zeit:** ____ min · **Beobachtungen:**

  &nbsp;

---

## Nachgespräch (5 Fragen)

1. Welche Aufgabe ging am leichtesten, welche war am schwersten – und warum?
2. Wo haben Sie etwas anders erwartet als es kam (Begriffe, Reihenfolge, Ort einer Schaltfläche)?
3. Waren die Erklärungen an gesperrten Schaltflächen und die vorgeschlagenen nächsten Schritte hilfreich?
4. Was fehlt Ihnen im Vergleich zu Ihrer bisherigen Arbeit (z. B. mit KuferSQL), damit Sie umsteigen würden?
5. Wenn Sie eine Sache sofort ändern dürften: welche?

**Notizen der Moderation:**

&nbsp;
