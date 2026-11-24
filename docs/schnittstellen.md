# Schnittstellen nach außen – geplant und Stand

Übersicht aller Verbindungen von VibeVHS zu anderen Systemen. Nummern **S-01 … S-28** wie in der
[Anforderungsanalyse](spezifikation/anforderungen.md#schnittstellen) (Abschnitt Schnittstellen); Einträge ohne Nummer sind
Datei-Übergaben ohne eigene Schnittstelle. Stand: 17.11.2026.

**Stand-Begriffe**

| Stand | Bedeutung |
|---|---|
| **umgesetzt** | funktioniert mit dem echten Gegenüber bzw. echtem Dateiformat, das die Gegenseite einliest |
| **Datei** | VibeVHS erzeugt/liest das echte Format; Übergabe aber noch von Hand (Datei herunterladen/hochladen) |
| **Mock** | Port vorhanden, Simulation statt Gegenstelle (Ergebnis im Postausgang sichtbar) |
| **vorbereitet** | Daten bzw. Einwilligungen werden schon geführt, Port/Adapter fehlt noch |
| **konzeptionell** | nur in den Anforderungen beschrieben |

**Grundsätze (für alle):** Jede Schnittstelle ist ein Port (Interface in `VibeVHS.Application`) mit austauschbarem
Adapter; welcher läuft (Mock oder echt), entscheidet die Konfiguration (`Adapters.Mocks/Registrierung.cs`). Alles
Ausgehende läuft über den **Postausgang** (Outbox, Wiederholung, am Vorgang sichtbar) und wird protokolliert; Logs und
Protokoll enthalten keine Personendaten (Pseudonyme). Zugangsdaten nur als Umgebungsvariablen auf dem Zielsystem.

## 1. Mail und Messaging

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-19 | **E-Mail** | Bestätigungen, Erinnerungen, Absagen, Rechnungen/Mahnungen (PDF), Konto-Bestätigung (Double-Opt-in) | aus | SMTP über das Mail-Relay der Kommune; ICS- und PDF-Anhänge, Übersetzungslink in jeder Mail | **Mock** (`IZustellKanal`, Kanal `EMail`; Mails nur im Postausgang) |
| S-19 | **SMS** | kurzfristige Ausfälle, Raumänderungen | aus | SMS-Gateway (Anbieter offen) | **vorbereitet** – Nummern mit Dienst „SMS“ und Einwilligung je Kanal/Zweck vorhanden, kein Kanal-Adapter |
| S-25 | **Messenger** (Signal, WhatsApp) | wie SMS, für Teilnehmende ohne Mailroutine (z. B. Integrationskurse) | aus | Business-API des Dienstes; Datenschutz/Auftragsverarbeitung **vorher klären** | **vorbereitet** – Nummern mit Dienst (Signal/WhatsApp) und Einwilligung „Messenger“ vorhanden |
| S-20 | **Übersetzungsdienst** | „Diese Nachricht in Ihrer Sprache lesen“ (Mail), QR-Code „Translate this letter“ (Brief), Hilfe im Portal | aus | DeepL API oder LibreTranslate (selbst betrieben) | **Mock** (`IUebersetzer`, `IUebersetzungslinks`; Zielseite zeigt Platzhalter) |
| S-21 | **Druck und Hybridpost** | Briefe in großer Menge (Bescheinigungen, Mahnungen, Programmversand) | aus | PDF-Stapel an Druckstraße der Kommune oder Hybridpost-Dienstleister | **konzeptionell** – Einzelbriefe als PDF mit QR-Code umgesetzt |
| S-23 | **Kalender** | Termine für Dozierende und Teilnehmende | aus | iCalendar (ICS): Abo-Adresse mit widerrufbarem Token, Einzeltermin, Mail-Anhang | **umgesetzt** (ICS); Exchange/Raumbuchung **konzeptionell** |

## 2. VHS-Systeme (Website, Shop, Verband, Altsystem)

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-17 | **VHS-Website / CMS** | Kursliste, Suche, Detail, Warenkorb, Buchung, „Mein Konto“-Link | aus / ein | Buchungs-API `/api/v1/shop` (REST/JSON, OpenAPI 3.1, Delta per `geaendertSeit`), Web-Komponente, TYPO3-Extension `vibevhs_shop` | **umgesetzt** – Details [webshop.md](webshop.md); TYPO3-Tests noch nicht ausgeführt |
| S-07 | **Online-Zahlung** | Bezahlen bei der Buchung | aus / ein | ePayBL (Bund/Länder) oder Payment-Provider | **konzeptionell** – heute Lastschrift (SEPA-Mandat) oder Rechnung |
| S-15 | **vhs.cloud** | Konten und Kursräume anlegen, Teilnehmende eintragen | aus | vhs.cloud-API (DVV) | **konzeptionell** |
| S-16 | **Kursportale** (meine-vhs.de, KOS, vhs2gether, Weiterbildungsdatenbanken) | Kursdaten veröffentlichen und austauschen | aus / ein | XML nach PAS 1045, REST | **konzeptionell** |
| S-18 | **Programmheft** | Satzdaten für das gedruckte Programm | aus | XML für InDesign, DOCX | **konzeptionell** – Kurs-QR-Bogen (PDF) umgesetzt |
| S-11 | **Prüfungsanbieter** (telc, g.a.s.t.) | Prüfungsanmeldung, Ergebnisse | aus / ein | Anbieterportal, CSV | **konzeptionell** – Prüferlizenzen als Qualifikation geführt |
| S-13 | **VHS-Statistik (DIE)** | Jahresstatistik je Programmbereich, Geschlecht, Altersgruppen | aus | DIE-Erhebungsformat | **Mock** – Zahlen werden berechnet (nur aggregiert), Übermittlung simuliert; Zuordnung Fachbereich → Programmbereich abstimmen |
| – | **Altsystem KuferSQL** | einmalige Übernahme von Personen und Anmeldungen | ein | CSV (UTF-8/Windows-1252, Trennzeichen erkannt) mit Probelauf und Dublettenprüfung | **umgesetzt** (Import `/import`) |

## 3. Stadtverwaltung (Kommune und kommunales Rechenzentrum)

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-01 | **Kommunale Anmeldung** (AD, Keycloak, ADFS, Entra ID) | Login und Rollen der Mitarbeitenden | ein | OIDC, SAML 2.0 | **Mock** – Testanmeldung mit Rollenwahl; Zugangsschutz am Webserver |
| S-02 | **Konten für Portale** | Login für Teilnehmende, Dozierende, Kundenkonto im Shop | intern | E-Mail mit Bestätigung, Passkey/2FA; alternativ Servicekonto/OIDC der Kommune | **Mock** – Konto-Bestätigung per Mail umgesetzt, Token vom Test-Identitätsdienst |
| S-03 | **Finanzsoftware der Kämmerei** (SAP, Infoma, H+H u. a.) | Buchungen, Personenkonten; Zahlungseingänge zurück | aus / ein | CSV, XML oder Hersteller-API | **Mock** – Buchungsstapel als CSV im Postausgang; **Sachkonten mit der Kämmerei klären** |
| S-22 | **DMS / E-Akte** | Archivierung von Verträgen, Bescheinigungen, Briefen | aus | CMIS oder Hersteller-API | **konzeptionell** – internes Dokumentarchiv mit SHA-256 umgesetzt |
| S-24 | **Gebäudetechnik und Raumanzeige** | Raumanzeige, Schließsystem, Heizung nach Belegung | aus | REST, Webseite | **konzeptionell** |
| S-26 | **Virenscanner** | Prüfung hochgeladener Dateien (Dozierenden-Unterlagen, Nachweise) | aus | z. B. ClamAV (clamd) der Kommune | **Mock** (`IVirenpruefung`, erkennt EICAR) |
| S-28 | **Datensicherung extern** | fortlaufende Kopie der Datenbank außerhalb des Servers | aus | WAL-Archivierung von PostgreSQL (z. B. pgBackRest) → S3-kompatibler Speicher des Rechenzentrums | offen; tägliche lokale Sicherung (pg_dump mit Wiederherstellungstest) umgesetzt |

## 4. Finanzen und Bank

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-04 | **Lastschrift** | Einzug der Kursentgelte | aus | SEPA **pain.008.001.08** | **Datei** – Lauf mit Freigabe und Vorabankündigung; XSD-Prüfung offen |
| S-05 | **Überweisung** | Honorare, Erstattungen, Fahrtkosten | aus | SEPA **pain.001.001.09** | **Datei** – XSD-Prüfung offen |
| S-06 | **Kontoauszug** | Zahlungsabgleich, Rücklastschriften | ein | **camt.053** (camt.054 geplant) | **Datei** – Einlesen mit Dublettenschutz und Zuordnungsvorschlag |
| S-08 | **E-Rechnung** | Rechnungen an Behörden und Firmen (Firmenkurse, BAMF) | aus | **XRechnung 3.0 (UBL)**; ZUGFeRD, Versand über Peppol geplant | **Datei** (XRechnung erzeugt); Peppol-Versand **konzeptionell** |
| – | **Betriebsprüfung (GoBD)** | Datenüberlassung an Prüfer | aus | ZIP mit CSV und `index.xml` (GDPdU) | **umgesetzt** |

## 5. Bund, Land und Kostenträger

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-09 | **BAMF InGe-Online** | Kursbeginn, Teilnehmerauskunft, Testmeldungen | aus / ein | BAMF-Webservices | **Mock** (`IBamfSchnittstelle`, simulierte Vorgangsnummer) |
| S-10 | **BAMF-Abrechnung** (Integrationskurse, Berufssprachkurse/DeuFöV) | Abrechnung je Abschnitt, Anwesenheitslisten | aus | BAMF-Formulare (PDF), XRechnung | **Mock** – Abrechnung berechnet; Sätze sind Platzhalter, Formulare als PDF offen |
| S-12 | **Bundesagentur für Arbeit / Jobcenter** | Kurse mit Bildungsgutschein melden, Kostenträgerabrechnung | aus | Weiterbildungsdatenbank, XRechnung | **konzeptionell** – Jobcenter als Organisation mit Vollmacht geführt |
| S-14 | **Land NRW (Weiterbildungsgesetz)** | Unterrichtsstunden für Statistik und Förderung | aus | Landesformat | **Mock** – Zahlen je Sachbereich berechnet, Übermittlung simuliert |

## 6. Hilfsdienste

| Nr. | Schnittstelle | Zweck | Richtung | Format / Technik | Stand |
|---|---|---|---|---|---|
| S-27 | **Geokodierung** | Entfernung Wohnort ↔ Kursort (BAMF-Fahrtkosten ab 3 km), Wegbeschreibung | aus | Geodienst (Anbieter offen) | **Mock** (`IGeokodierer`) |

## Offen und abzuklären

- **Kämmerei:** Zielsystem und Format für S-03, Sachkonten im Buchungsstapel und im GoBD-Export.
- **Kommune/IT:** Identitätsdienst (S-01/S-02), Mail-Relay (S-19), Virenscanner, DMS (S-22), S3-Ziel für die Sicherung.
- **Messaging:** ob und über welchen Dienst (SMS-Gateway, Signal, WhatsApp Business) – Datenschutz und Auftragsverarbeitung.
- **BAMF:** Zugang zu InGe-Online, aktuelle Kostenerstattungs- und Kostenbeitragssätze.
- **Übersetzer:** DeepL (Auftragsverarbeitung) oder selbst betriebenes LibreTranslate.
- **Barzahlung** (FA-145), Online-Zahlung (S-07).
- **Prüfung der Dateiformate** gegen die offiziellen XSD-Schemata (SEPA, XRechnung) – Download braucht Freigabe.
