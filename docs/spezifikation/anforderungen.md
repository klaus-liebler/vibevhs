# VibeVHS – Anforderungsanalyse und Use Cases

Stand: 17.11.2026

## Ziel, Umfang und Quellen

VibeVHS soll die Verwaltungsmitarbeitenden einer Volkshochschule im Tagesgeschäft spürbar entlasten: weniger Klicks, weniger Doppelerfassung, nachvollziehbare Automatisierung. Dieses Dokument ist Schritt 1 von 4 und bildet die Grundlage für Datenmodell, Konzepte und den klickbaren Prototyp.

- **Zielgruppe für die Überzeugungsarbeit:** Verwaltungsmitarbeitende (Programmplanung, Teilnehmerservice, Kasse/Buchhaltung, Integrationskurs-Team).
- **Technik:** ASP.NET Core, PostgreSQL (seit 24.11.2026, vorher SQLite), Docker-Image, Anmeldung über die kommunale Infrastruktur (OIDC/SAML, Active Directory).
- **Schnittstellen:** in dieser Phase nur als Mocks mit realistischem Datenformat.
- **Quellen:** ausschließlich öffentlich zugängliche Informationen (Herstellerseiten, Handbuch-Auszüge, Verbandsinformationen, BAMF-Vorgaben). Es lagen keine Unterlagen der Hersteller vor.
- **Zentrale Datenregel:** Stammdaten sind zeitlich versioniert. Ein Dokument zu einem vergangenen Vorgang zeigt immer den damals gültigen Stand, etwa den früheren Nachnamen einer Dozentin.

### Ausbaustufen

Die Analyse und das Datenmodell beschreiben den Endausbau mit Portalen und allen Schnittstellen. Implementiert wird zunächst nur die Verwaltung. Anforderungen für spätere Stufen sind mit „Endausbau“ markiert und werden nur so weit berücksichtigt, dass sie später ohne Umbau möglich sind.

| Stufe | Inhalt | Umsetzung |
| --- | --- | --- |
| 1 | Verwaltung: Planung, Anmeldung, Anwesenheit, Finanzen, Integrationskurse, Auswertungen; Schnittstellen als Mocks | Zuerst implementiert |
| 2 | Dozierendenportal (mobil): Termine, Anwesenheit, Verträge, Honorarnachweise | Endausbau, im Datenmodell vorbereitet |
| 3 | Teilnehmendenportal: Selbstregistrierung, Buchung öffentlicher Kurse, Check-in, Dokumente; Firmenportal | Endausbau, im Datenmodell vorbereitet |
| 4 | Echte Schnittstellen statt Mocks, schrittweise je Partner | Endausbau, über Adapter vorbereitet |

## Akteure und Rollen

Neun Akteure nutzen das System, fünf davon intern. Die Rollen werden aus Gruppen der kommunalen Anmeldung (AD/OIDC) abgeleitet. Rechte gelten zusätzlich je Fachbereich und Semester, wie in KuferSQL üblich.

| Akteur | Typische Aufgaben | Zugang |
| --- | --- | --- |
| Teilnehmerservice (Anmeldung, Kasse) | Anmeldungen, Umbuchungen, Storno, Barzahlung, Auskünfte am Telefon und am Tresen | Verwaltungsoberfläche |
| Programmplanung (Fachbereichsleitung) | Kurse planen, Dozierende und Räume zuordnen, Semesterprogramm freigeben | Verwaltungsoberfläche |
| Integrationskurs-Team | BAMF-Kurse, Teilnahmeberechtigungen, Anwesenheit, Abrechnung, Fahrtkosten, Tests | Verwaltungsoberfläche |
| Buchhaltung | Lastschriften, Mahnungen, Honorare, Übergabe an die Finanzsoftware der Kommune | Verwaltungsoberfläche |
| Leitung | Auswertungen, Kennzahlen, Statistik, Freigaben | Dashboard |
| Dozierende | Teilnehmerlisten, Anwesenheit per QR oder Liste, Kursinhalte, Verträge, Honorarabrechnung | Dozierendenportal (mobil) |
| Teilnehmende | Kurse suchen und buchen, Termine einchecken, Bescheinigungen und Rechnungen abrufen | Webportal und QR-Codes |
| Firmen und Kostenträger | Mitarbeitende anmelden, Sammelrechnungen erhalten | Webportal |
| Administration (IT der Kommune) | Betrieb, Rollen, Schnittstellen, Löschläufe, Protokolle | Admin-Bereich |

Dazu kommen Systemakteure: der Zeitplaner für automatische Abläufe sowie externe Systeme (BAMF, Finanzsoftware, Bank, vhs.cloud, Statistik).

## Erkenntnisse aus bestehenden Systemen

KuferSQL deckt den VHS-Alltag fachlich sehr breit ab. VibeVHS sollte also nicht weniger können, sondern den Alltag einfacher machen: weniger Masken, geführte Folgeschritte, Automatisierung mit Protokoll. cmxOrganize zeigt, wohin moderne Systeme gehen: Workflows, Dashboards, Portale und APIs.

| Bereich | Was die Systeme bieten | Konsequenz für VibeVHS |
| --- | --- | --- |
| Programmplanung | Kursvorlagen, Kopie ganzer Semester, Planungsgrafik mit Drag and Drop, Kontrollausdrucke an Dozierende, Single-Source-Publishing für Heft und Web ([Kufer-Broschüre](https://www.kufer.de/fileadmin/user_upload/dokumente/broschuere/Kufer_Broschuere_2021.pdf)) | Übernehmen. Semesterkopie und Vorlagen sind der größte Zeitgewinn. |
| Anmeldung | Web mit Warenkorb, Foyer-PC, Firmenbuchung, Abgleich mit bekannten Personen und Dublettenprüfung ([Kufer DSGVO-Beschreibung](https://www.peiting.de/wp-content/uploads/2018/05/Kufer-SQL-VHS-Beschreibung.pdf)) | Übernehmen, dazu QR-Anmeldung und eine echte Warteliste mit Nachrückautomatik. |
| Räume und Medien | Raumpläne, Sperrzeiten, Rüstzeiten, Doppelbelegungsprüfung, Raumanzeige im Foyer | Übernehmen, Raumanzeige als einfache Webseite. |
| Dozierende | Qualifikationen, Wunschtermine, Verträge im Sammeldruck, Honorarjournal, elektronisches Klassenbuch, Dozierenden-Intranet | Übernehmen, mobil nutzbar. |
| Finanzen | Zahlungsjournal, SEPA-Lastschrift, Mahnwesen, Kassenabschluss, Übergabe an Fibu (SAP, Infoma, H+H u. a.), Honorarmeldung ans Finanzamt | Übernehmen. Die Fibu-Schnittstelle wird zuerst gemockt. |
| Integrationskurse | Taggenaue Anwesenheit, BAMF-Formulare, Abrechnungsbogen, Fahrtkosten, InGe-Online-Meldungen ([Kufer BAMF-Flyer](https://www.kufer.de/fileadmin/user_upload/dokumente/flyer/bamf/Flyer_BAMF_2017.pdf)) | Kernbereich mit hohem Nutzen. Meldungen zuerst als Mock. |
| Statistik | DVV-/DIE-Statistik, Landesstatistik, freie Auswertungen ohne Programmierung | Übernehmen, Statistikexport als Mock. |
| Datenschutz | Feldrechte, Semesterrechte, Feldprotokoll, Löschroutinen, Anonymisierung | Übernehmen und zur Grundregel machen. |
| Kooperation | XML-Austausch von Kursdaten zwischen VHS, KOS-Kursportal, geplant vhs2gether ([vhs Bayern](https://vhs-bayern.de/wp-content/uploads/2024/01/DV_Kompetenzcluster_Technische_Moeglichkeiten_zum_Kursdatentausch.pdf)) | Kursdaten von Anfang an über eine offene API austauschbar machen. |
| vhs.cloud | Konten für Teilnehmende und Kursleitungen anlegen, Kursaufnahme, Zuordnung über E-Mail ([Checkliste](https://www.vhs-cloud2.digionline.de/wws/bin/242374-1349574-1-vhs_cloud_checkliste_kursverwaltungssoftware.pdf)) | Schnittstelle vorsehen, zuerst gemockt. |
| Moderne Plattformen | Workflows, Genehmigungen, Dashboards, Portale, API, XRechnung ([cmxOrganize](https://www.softguide.de/programm/cmxorganize-die-cloudbasierte-unternehmensloesung/vorschau)) | Workflows mit Protokoll, Dashboard, XRechnung für Firmen und Behörden. |

**Typische Schwächen älterer Systeme**, aus Struktur und Funktionsumfang abgeleitet und nicht durch Nutzerbefragung belegt:

- Viele Masken und Reiter, Wissen hängt an erfahrenen Mitarbeitenden.
- Funktionen als kostenpflichtige Zusatzmodule, dadurch Medienbrüche (z. B. XML per E-Mail).
- Desktop-Client statt Browser. Dozierende und Teilnehmende brauchen eigene Zusatzportale.
- Historie meist nur als Feldprotokoll, nicht als fachlich gültiger Stand zu einem Stichtag.

## Use Cases nach Fachbereich

32 Use Cases in sieben Bereichen. Jeder nennt den Folgeschritt, den das System danach anbietet, weil das eine Kernvorgabe für die Bedienung ist. Die mit „P“ markierten Use Cases zeigt der klickbare Prototyp.

| ID | Use Case | Hauptakteur | Angebotener Folgeschritt | P |
| --- | --- | --- | --- | --- |
| UC-01 | Semester aus Vorsemester kopieren | Programmplanung | Geänderte Termine und Feiertagskonflikte prüfen | P |
| UC-02 | Kurs aus Vorlage anlegen | Programmplanung | Dozierende und Raum zuordnen | P |
| UC-03 | Termine eines Kurses planen (Serie, Ausnahmen, Feiertage) | Programmplanung | Raumkonflikte auflösen | P |
| UC-04 | Raum und Medien buchen, Konflikte erkennen | Programmplanung | Kurs zur Veröffentlichung freigeben | P |
| UC-05 | Dozierende zuordnen, Honorarvertrag erzeugen | Programmplanung | Vertrag zur Unterschrift senden | P |
| UC-06 | Programm veröffentlichen (Web, Programmheft-Export) | Programmplanung | Newsletter an Interessenten senden |  |
| UC-07 | Kurs absagen oder verschieben | Programmplanung | Teilnehmende, Dozierende und Raum automatisch informieren, Erstattungen anlegen | P |
| UC-10 | Teilnehmende am Tresen oder am Telefon anmelden | Teilnehmerservice | Zahlungsart erfassen, Anmeldebestätigung senden | P |
| UC-11 | Online-Anmeldung prüfen und übernehmen (Dublettenabgleich) | Teilnehmerservice | Mit bestehender Person zusammenführen | P |
| UC-12 | Anmeldung per QR-Code (Plakat, Programmheft, Brief) | Teilnehmende | Termine in den eigenen Kalender übernehmen | P |
| UC-13 | Warteliste führen, Nachrücken automatisch anbieten | Teilnehmerservice / System | Nachrückangebot mit Frist versenden | P |
| UC-14 | Umbuchen, Stornieren nach AGB-Fristen | Teilnehmerservice | Gutschrift oder Erstattung anlegen | P |
| UC-15 | Firmen- oder Kostenträgeranmeldung (Sammelanmeldung) | Firmen | Sammelrechnung erzeugen |  |
| UC-16 | Ermäßigung prüfen und mit Gültigkeitszeitraum erfassen | Teilnehmerservice | Nachweis zur Wiedervorlage legen |  |
| UC-20 | Anwesenheit per QR-Check-in je Termin | Teilnehmende / Dozierende | Fehlende Personen nachtragen | P |
| UC-21 | Anwesenheit manuell oder per Liste erfassen | Dozierende | Unterrichtsinhalt eintragen | P |
| UC-22 | Teilnahmebescheinigung erzeugen (auch Nachdruck mit historischem Stand) | Teilnehmerservice | Per Mail senden oder drucken | P |
| UC-23 | Kurs abschließen (Anwesenheit, Feedback, Abrechnung) | Programmplanung | Honorarabrechnung auslösen | P |
| UC-30 | Lastschriftlauf (SEPA) erzeugen | Buchhaltung | Datei an Bank übergeben, Rückläufer erwarten |  |
| UC-31 | Zahlungseingänge zuordnen, Mahnlauf starten | Buchhaltung | Mahnbriefe drucken oder mailen | P |
| UC-32 | Honorare abrechnen und auszahlen | Buchhaltung | Honorarmitteilung ans Finanzamt vormerken |  |
| UC-33 | Buchungen an die kommunale Finanzsoftware übergeben (Mock) | Buchhaltung | Übergabeprotokoll prüfen |  |
| UC-34 | Rechnung als XRechnung an Firma oder Behörde | Buchhaltung | Versandstatus überwachen |  |
| UC-40 | Integrationskurs anlegen und Kursbeginn melden (Mock) | Integrationskurs-Team | Teilnehmende mit Berechtigung zuordnen | P |
| UC-41 | Teilnahmeberechtigung und Kostenbefreiung erfassen | Integrationskurs-Team | Fehlende Nachweise anfordern | P |
| UC-42 | Anwesenheit in Unterrichtseinheiten, Abrechnung je Modul | Integrationskurs-Team | Abrechnungsbogen erzeugen | P |
| UC-43 | Fahrtkostenerstattung berechnen und auszahlen | Integrationskurs-Team | Auszahlung buchen |  |
| UC-44 | Prüfungen (DTZ, Orientierungskurs) anmelden und Ergebnisse erfassen | Integrationskurs-Team | Zertifikat ausgeben |  |
| UC-50 | Dashboard: Belegung, Auslastung, Umsätze, offene Aufgaben | Leitung | Detailliste öffnen | P |
| UC-51 | Jahresstatistik für DVV/DIE erzeugen (Mock-Export) | Leitung | Plausibilitätsfehler korrigieren |  |
| UC-60 | Auskunft nach Art. 15 DSGVO erzeugen | Administration | Auskunft versenden, Vorgang dokumentieren |  |
| UC-61 | Löschen und Anonymisieren nach Fristen | System | Löschprotokoll prüfen |  |

### Ergänzende Use Cases: Buchungsrechte und Portale

UC-17 und UC-18 gehören zu Stufe 1. Die Portal-Use-Cases sind Endausbau; sie bestimmen aber schon jetzt Rechte und Datenmodell.

| ID | Use Case | Hauptakteur | Angebotener Folgeschritt | Stufe |
| --- | --- | --- | --- | --- |
| UC-17 | Buchungsrichtlinie eines Kurses festlegen (Sichtbarkeit, wer sich selbst und wer andere buchen darf) | Programmplanung | Richtlinie als Vorlage für die Kursart speichern | 1 |
| UC-18 | Buchungsvollmacht erfassen (wer darf wen buchen, ab wann, bis wann) | Teilnehmerservice | Vollmacht von der gebuchten Person bestätigen lassen | 1 |
| UC-70 | Eigene Kurse und Termine einsehen, Kalender abonnieren | Dozierende | Teilnehmerliste öffnen | 2 |
| UC-71 | Verfügbarkeiten und Wunschtermine für das nächste Semester melden | Dozierende | Kursvorschlag einreichen | 2 |
| UC-72 | Kursvorschlag einreichen | Dozierende | Status der Prüfung verfolgen | 2 |
| UC-73 | Honorarvertrag digital bestätigen | Dozierende | Vertrag herunterladen | 2 |
| UC-74 | Stundennachweis einreichen, Honorarabrechnung einsehen | Dozierende | Abrechnung prüfen | 2 |
| UC-75 | Nachricht an alle Teilnehmenden eines Kurses senden | Dozierende | Kursmaterial hochladen | 2 |
| UC-76 | Eigene Stammdaten ändern (z. B. Name nach Heirat) mit Gültigkeitsdatum; Verwaltung gibt frei | Dozierende / Teilnehmende | Nachweis hochladen | 2/3 |
| UC-80 | Konto selbst anlegen und E-Mail bestätigen | Teilnehmende | Bevorzugte Sprache und Einwilligungen festlegen | 3 |
| UC-81 | Öffentlichen Kurs suchen und für sich selbst buchen | Teilnehmende | Bezahlen oder SEPA-Mandat erteilen | 3 |
| UC-82 | Eine andere Person buchen (z. B. Kind), sofern eine Vollmacht besteht | Teilnehmende | Weitere Person buchen | 3 |
| UC-83 | Buchungen, Rechnungen und Bescheinigungen abrufen | Teilnehmende | Bescheinigung herunterladen | 3 |
| UC-84 | Innerhalb der Frist selbst stornieren oder umbuchen | Teilnehmende | Alternativkurs vorschlagen lassen | 3 |
| UC-85 | Wartelistenangebot annehmen oder ablehnen | Teilnehmende | Termine in den Kalender übernehmen | 3 |
| UC-86 | Einwilligungen, Sprache, Datenauskunft und Kontolöschung selbst verwalten | Teilnehmende | Auskunft herunterladen | 3 |
| UC-90 | Mitarbeitende in Firmen- oder offene Kurse buchen | Firmen | Sammelrechnung abrufen | 3 |

## Funktionale Anforderungen

Der Katalog umfasst 58 Anforderungen. Muss-Anforderungen bilden das MVP, Soll folgt in der nächsten Ausbaustufe, Kann ist optional. „Endausbau“ steht für Portalfunktionen, die das Datenmodell schon jetzt berücksichtigt.

| ID | Anforderung | Prio | Herkunft |
| --- | --- | --- | --- |
| FA-01 | Kurse mit Nummer, Titel, Fachbereich, Programmbereich (DVV-Systematik), Beschreibung, Entgelt, Min./Max. Teilnehmende | Muss | KuferSQL |
| FA-02 | Kursvorlagen und Kopie ganzer Semester mit Datumsverschiebung und Feiertagsprüfung | Muss | KuferSQL |
| FA-03 | Terminplanung als Serie mit Ausnahmen, Ferien- und Feiertagskalender je Bundesland (Details FA-93 bis FA-97) | Muss | Praxis |
| FA-04 | Raum- und Medienbuchung mit Konfliktprüfung, Rüstzeiten und Sperrzeiten | Muss | KuferSQL |
| FA-05 | Planungsansicht (Tag/Woche/Semester) mit Drag and Drop | Soll | KuferSQL |
| FA-06 | Kursstatus-Workflow: Entwurf, freigegeben, veröffentlicht, läuft, abgeschlossen, abgesagt | Muss | Praxis |
| FA-07 | Mindestteilnehmerzahl-Prüfung X Tage vor Beginn mit Vorschlag „absagen oder durchführen“ | Muss | Praxis |
| FA-161 | Einen Kurs absagen geht nur mit Bestätigung und Grund – über die Aufgabe „absagen oder durchführen?“ oder auf der Kursseite. Dabei fallen künftige Termine aus, offene Belegungen (auch Warteliste und Nachrückangebote) werden ohne Gebühr aufgehoben und alle Betroffenen per Mail mit Übersetzungslink benachrichtigt; die Bestätigung nennt vorher, wie viele Personen und Termine betroffen sind. Einen Statuswechsel auf „abgesagt“ ohne diese Schritte gibt es nicht. | Muss | Abnahme-Vorbereitung |
| FA-08 | Export für Programmheft und Website aus einer Datenquelle | Soll | KuferSQL |
| FA-10 | Personenverwaltung ohne Dubletten: Abgleich nach Name, Geburtsdatum, E-Mail, Telefon, mit Zusammenführung | Muss | KuferSQL |
| FA-11 | Anmeldung am Tresen in max. 3 Schritten (Person suchen/anlegen, Kurs wählen, Zahlart) | Muss | Vorgabe |
| FA-12 | Online-Anmeldung mit Warenkorb und Bestätigungsmail | Muss | KuferWEB |
| FA-13 | Anmeldung per QR-Code, der direkt zum Kurs führt; bekannte Personen buchen mit einem Klick | Muss | Vorgabe |
| FA-14 | Warteliste mit automatischem Nachrückangebot und Annahmefrist | Muss | Praxis |
| FA-15 | Storno und Umbuchung nach konfigurierbaren AGB-Fristen und Gebühren | Muss | Praxis |
| FA-16 | Ermäßigungen mit Nachweis und Gültigkeitszeitraum | Muss | KuferSQL |
| FA-17 | Sammelanmeldung durch Firmen und Kostenträger, abweichender Rechnungsempfänger | Soll | KuferSQL |
| FA-18 | Einwilligungen (Newsletter, Fotos, vhs.cloud) mit Datum und Version des Textes speichern | Muss | DSGVO |
| FA-20 | QR-Check-in je Termin: Teilnehmende scannen den Code im Raum oder Dozierende scannen den persönlichen Code | Muss | Vorgabe |
| FA-21 | Digitale Anwesenheitsliste für Dozierende, minuten- bzw. UE-genau | Muss | KuferSQL |
| FA-22 | Teilnahmebescheinigung und Zertifikat als PDF, Nachdruck mit historisch korrekten Daten | Muss | Vorgabe |
| FA-23 | Elektronisches Klassenbuch (Inhalte, Hausaufgaben) | Kann | KuferSQL |
| FA-24 | Kursfeedback per Online-Fragebogen | Kann | KuferSQL |
| FA-30 | Teilnehmerkonto (Soll/Haben) mit Rechnungen, Gutschriften, Zahlungen | Muss | KuferSQL |
| FA-31 | SEPA-Lastschrift (pain.008) mit Mandatsverwaltung, Rückläufer-Verarbeitung | Muss | KuferSQL |
| FA-32 | Mahnwesen mit Stufen, Fristen und Gebühren | Muss | KuferSQL |
| FA-33 | Honorarverträge und Honorarabrechnung aus tatsächlich gehaltenen Terminen | Muss | KuferSQL |
| FA-34 | Übergabe an kommunale Finanzsoftware (Mock, CSV/XML) | Soll | KuferSQL |
| FA-35 | XRechnung für öffentliche Auftraggeber | Soll | cmxOrganize |
| FA-36 | Kassenbuch für Barzahlungen mit Tagesabschluss | Soll | KuferSQL |
| FA-40 | Integrationskurse mit Modulen, BAMF-Kursnummer, Teilnahmeberechtigung, Kostenbeitrag bzw. Befreiung | Muss | BAMF |
| FA-41 | Anwesenheit in Unterrichtseinheiten, Abrechnungsliste je Abrechnungszeitraum | Muss | BAMF |
| FA-42 | Fahrtkostenerstattung mit Berechnung und Auszahlung | Soll | BAMF |
| FA-43 | InGe-Online-Meldungen (Kursbeginn, Teilnehmerauskunft, Tests) als Mock | Soll | BAMF |
| FA-44 | Prüfungsverwaltung (DTZ, LiD) | Soll | BAMF |
| FA-50 | Dashboard für Leitung und Fachbereiche | Muss | cmxOrganize |
| FA-51 | Freie Auswertungen mit Filter und Excel-Export | Muss | KuferSQL |
| FA-52 | DVV/DIE-Jahresstatistik (Mock-Export) | Soll | KuferSQL |
| FA-53 | vhs.cloud-Anbindung: Konten und Kurse anlegen (Mock) | Kann | vhs.cloud |
| FA-54 | Kursdatenaustausch mit Partner-VHS über offene API | Kann | vhs Bayern |
| FA-55 | Raumanzeige für den Eingangsbereich als Webseite | Kann | KuferSQL |

### Buchungsrechte

Jeder Kurs legt fest, wer sich selbst buchen darf und wer andere buchen darf. Selbstregistrierte Teilnehmende buchen nur öffentliche Kurse. Wer für wen buchen darf, regeln Buchungsvollmachten mit Gültigkeitszeitraum.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-60 | Jeder Kurs hat eine Sichtbarkeit: öffentlich, intern (nur Verwaltung bucht) oder geschlossen (nur benannte Gruppe, z. B. Firmenkurs, Integrationskurs). | Muss |
| FA-61 | Jeder Kurs hat eine Buchungsrichtlinie: je Rolle die Rechte „sich selbst buchen“ und „andere buchen“. Rollen sind z. B. Verwaltung, Dozierende, registrierte Teilnehmende, Firmenbevollmächtigte. | Muss |
| FA-62 | Selbstregistrierte Teilnehmende dürfen nur öffentliche Kurse buchen; das System erzwingt das unabhängig von der Richtlinie. | Muss |
| FA-63 | Buchungsvollmachten legen fest, wer wen buchen darf: buchende Person oder Organisation, gebuchte Person, Art (Erziehungsberechtigte, Firma, Betreuung, Kostenträger), Gültigkeitszeitraum, Nachweis, Bestätigung. | Muss |
| FA-64 | Vollmachten für Gruppen: Eine Firma darf alle ihr zugeordneten Mitarbeitenden buchen; die Zuordnung ist selbst zeitlich gültig. | Soll |
| FA-65 | Vorlagen für Buchungsrichtlinien je Kursart (z. B. offener Kurs: alle; Integrationskurs: nur Verwaltung; Firmenkurs: nur Firmenbevollmächtigte und Verwaltung). | Muss |
| FA-66 | Verwaltungsrollen dürfen nach Fachbereich und Semester eingeschränkt werden (Datenrechte wie in KuferSQL). | Muss |
| FA-67 | Jede Belegung speichert, wer für wen gebucht hat, in welcher Rolle und auf Grundlage welcher Vollmacht. | Muss |
| FA-68 | Ist eine Buchung nicht erlaubt, erklärt das System den Grund (z. B. „Dieser Kurs ist nur über die Verwaltung buchbar“) und nennt den Weg dorthin. | Muss |

### Portale (Endausbau)

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-70 | Dozierendenportal: Kurse, Termine, Kalender-Abo (iCal), Teilnehmerlisten | Endausbau |
| FA-71 | Dozierendenportal: Verfügbarkeiten, Kursvorschläge, digitale Vertragsbestätigung | Endausbau |
| FA-72 | Dozierendenportal: Stundennachweis, Honorarabrechnung, Nachrichten an Teilnehmende, Kursmaterial | Endausbau |
| FA-73 | Stammdatenänderungen aus Portalen sind Anträge mit Gültigkeitsdatum und Nachweis; die Verwaltung gibt frei | Endausbau |
| FA-80 | Teilnehmendenportal: Selbstregistrierung mit E-Mail-Bestätigung, Buchung öffentlicher Kurse, Buchung für Bevollmächtigte | Endausbau |
| FA-81 | Teilnehmendenportal: Buchungen, Rechnungen, Bescheinigungen, Storno und Umbuchung in der Frist | Endausbau |
| FA-82 | Teilnehmendenportal: Online-Zahlung oder SEPA-Mandat, Wartelistenangebote | Endausbau |
| FA-83 | Teilnehmendenportal: Sprache, Einwilligungen, Datenauskunft, Kontolöschung | Endausbau |
| FA-84 | Firmenportal: Mitarbeitende verwalten und buchen, Sammelrechnungen | Endausbau |

### Kontaktdaten, Kurse, Veranstaltungen und Orte

Ein Kurs ist die logische Klammer über seine Veranstaltungen (Einzeltermine). Veranstaltungen entstehen als Serie oder einzeln, finden an einem oder mehreren Orten gleichzeitig statt und haben je Ort eine eigene Kapazität. Anmeldungen gelten für alle Veranstaltungen oder für ein Kontingent.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-90 | Eine Person hat beliebig viele Postadressen, Telefon- bzw. Messaging-Nummern und E-Mail-Adressen, jeweils mit Art und Gültigkeitszeitraum. | Muss |
| FA-91 | Genau eine Postadresse ist „üblicher Aufenthaltsort“, genau eine ist „Standard-Korrespondenzadresse“; im Normalfall dieselbe. Briefe gehen an die Korrespondenzadresse, Fahrtkosten und Statistik nutzen den Aufenthaltsort. | Muss |
| FA-92 | Je Kanal ist eine Nummer bzw. Adresse Standard; Messaging-Nummern tragen den Dienst (z. B. SMS, Signal) und die Einwilligung. | Muss |
| FA-93 | Ein Kurs hat ein Dozierendenteam und eine Laufart: „bestimmte Anzahl Veranstaltungen“ oder „regelmäßig auf unbestimmte Zeit“. | Muss |
| FA-94 | Schnellweg „Regelmäßige Veranstaltungen erzeugen“: Wochentag, Uhrzeit, Rhythmus, Anzahl oder Enddatum, gleicher Ort, gleiche Dozierende; Feiertage und Ferien NRW werden ausgelassen und angezeigt. | Muss |
| FA-95 | Bei Kursen auf unbestimmte Zeit erzeugt das System die Veranstaltungen rollierend für einen einstellbaren Zeitraum im Voraus (z. B. 12 Wochen). | Muss |
| FA-96 | Einzelveranstaltungen lassen sich frei anlegen, mit abweichendem Datum, Ort und Dozierenden (vorgeschlagen aus dem Kursteam, andere mit Hinweis möglich). | Muss |
| FA-97 | Einzelne Veranstaltungen einer Serie lassen sich ändern oder absagen, ohne die Serie zu zerstören; Änderungen an der Serie fragen „nur diese / alle folgenden“. | Muss |
| FA-98 | Eine Veranstaltung findet an einem oder mehreren Orten gleichzeitig statt (hybrid, z. B. Raum 2.14 und Online-Raum). | Muss |
| FA-99 | Ortsarten: Raum (gehört zu einem Gebäude, das optional zu einer Liegenschaft gehört), allgemeiner Ort (z. B. Treffpunkt im Freien, mit Koordinaten), virtueller Raum (Plattform, Link, Zugangsdaten). | Muss |
| FA-100 | Lagebeschreibungen je Ebene (Liegenschaft: Zufahrt, Parken, ÖPNV; Gebäude: Eingang, Barrierefreiheit; Raum: Etage, Weg ab Eingang). Daraus erzeugt das System automatisch Wegbeschreibungen für Bestätigungen, Aushänge und die Übersetzungsseite. | Muss |
| FA-101 | Orte tragen Koordinaten bzw. Adresse, damit Entfernungen für Fahrtkosten (Dozierende, BAMF) berechnet werden können. | Muss |
| FA-102 | Jeder Ort hat Kapazitäten je Nutzungsart (z. B. Seminar 16, Vortrag 30, Prüfung 10). Bei der Planung wird die passende Kapazität übernommen oder eine eigene Höchstzahl je Veranstaltung und Ort festgelegt. | Muss |
| FA-103 | Belegungsarten: „alle Veranstaltungen“ oder „Kontingent von n Veranstaltungen“ (z. B. 10 aus einem Kurs auf unbestimmte Zeit), optional mit Gültigkeitsdauer. | Muss |
| FA-104 | Je Kurs einstellbar: „Vorab-Einbuchung erforderlich“. Teilnehmende buchen sich dann vorab in einzelne Veranstaltungen ein; das geht nur, solange die Kapazität des gewählten Ortes nicht erreicht ist. | Muss |
| FA-105 | Vorab-Einbuchung und Abmeldung mit Frist; bei voller Veranstaltung Warteliste für genau diese Veranstaltung. Der Check-in vor Ort prüft die Einbuchung. | Muss |
| FA-106 | Das Kontingent zeigt jederzeit: gebucht, besucht, verfallen, verbleibend. Regel für Nichterscheinen (zählt oder zählt nicht) ist je Kurs einstellbar. | Muss |
| FA-107 | In Stufe 1 bucht die Verwaltung Vorab-Einbuchungen; im Endausbau buchen Teilnehmende selbst im Portal oder per QR-Code. | Muss |
| FA-108 | Orte, Gebäude und Liegenschaften tragen Merkmale aus einem pflegbaren Katalog (ja/nein, Zahl, Text, Auswahl), z. B. barrierearmer Zugang, barrierefreier Zugang nach DIN 18040, Aufzug, rollstuhlgerechtes WC, Höranlage, Ausstattung. Gebäudemerkmale gelten für alle Räume, sofern der Raum nichts anderes sagt. | Muss |
| FA-109 | Zulassungen als Merkmal mit Nachweis und Gültigkeit, z. B. „Kursort für Integrationskurse beim BAMF gemeldet“ oder Prüfungsort. Vor Ablauf entsteht eine Aufgabe. | Muss |
| FA-110 | Kursarten können Merkmale verlangen (Integrationskurs → gültige BAMF-Meldung am Veranstaltungstag). Die Raumplanung filtert danach und erklärt per Tooltip, warum ein Ort nicht wählbar ist. Zugänglichkeit erscheint in Wegbeschreibung, Bestätigung und Portal; Teilnehmende können danach filtern. | Muss |
| FA-146 | Jedes Merkmal im Katalog hat einen festen Werttyp: Ja/Nein, Auswahl aus gepflegten Werten (einfach oder mehrfach), Ganzzahl mit Einheit und Grenzen, Text. Kursarten verlangen Bedingungen je Typ (z. B. mind. 12 PC-Arbeitsplätze); die Eignungsprüfung nennt konkret, was fehlt. Der Typ ist nach erster Verwendung nicht mehr änderbar. | Muss |

### Archivieren, Löschen und Betroffenenrechte

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-111 | Semester, Kurse und Personen lassen sich nach Kriterien suchen: Kursende vor Datum, Semester älter als n, Personen ohne Aktivität seit n Monaten oder Jahren (Zeitdauer einstellbar). | Muss |
| FA-112 | Archivieren: Semester, Kurse und Personen werden schreibgeschützt und aus Standardlisten ausgeblendet, bleiben aber über einen Filter auffindbar. Rückgängig machbar. | Muss |
| FA-113 | Löschen erhält die referentielle Integrität: Personen werden an Ort und Stelle anonymisiert (eigener Platzhalter je Person, „Gelöschte Person · anonymisiert am …“), auch in allen alten Versionen, Kontaktdaten, Einwilligungen, Suchindex, Postausgang und Dokumenten außerhalb von Fristen. | Muss |
| FA-114 | Laufen gesetzliche Aufbewahrungsfristen, wird statt gelöscht gesperrt (Art. 18 DSGVO); nach Fristende anonymisiert ein Löschlauf automatisch. Das gilt insbesondere für Belege nach GoBD (FA-130, FA-131). | Muss |
| FA-115 | Löschläufe sind regelbasiert mit einstellbaren Fristen, zeigen vorher Treffer und Zahlen, brauchen eine Vier-Augen-Freigabe und schreiben ein Löschprotokoll ohne Personendaten. | Muss |
| FA-116 | Löschverlangen (Art. 17): sofortige Löschung des Zulässigen, Sperre des Rests, Antwortschreiben an die Person mit Fristen. | Muss |
| FA-117 | Auskunft (Art. 15) als PDF und JSON mit allen Daten, Historie, Dokumenten, Empfängern und Fristen. | Muss |
| FA-118 | Statistik (Weiterbildungsgesetz NRW, BAMF) bleibt nach Anonymisierung korrekt; erhalten bleiben nur grobe Merkmale wie Altersgruppe, Geschlecht, Kreis. | Muss |
| FA-119 | Ausbaustufe: Crypto-Shredding (Schlüssel je Person), damit Daten auch in Sicherungen unlesbar werden. | Kann |

### Dozierende: Beschäftigungsart und Qualifikationen

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-120 | Dozierende haben eine zeitlich gültige Beschäftigungsart: festangestellt (Arbeitszeit, kein Honorar) oder freiberuflich (Honorarvertrag, Honorarsatz). Ein Wechsel gilt ab Datum; alte Verträge und Abrechnungen behalten die damalige Art. | Muss |
| FA-121 | Dozierende tragen Qualifikationsmerkmale aus einem pflegbaren Katalog, je mit Nachweis (Dokument, Aktenzeichen), ausstellender Stelle und Gültigkeit, z. B. BAMF-Zulassung als Lehrkraft für Integrationskurse (mit Zulassungsart), Zusatzqualifikation Alphabetisierung, Prüferlizenz telc oder Goethe, Übungsleiterlizenz, Erste-Hilfe-Nachweis, erweitertes Führungszeugnis. | Muss |
| FA-122 | Kursarten verlangen Qualifikationen, z. B. Integrationskurs → gültige BAMF-Lehrkraftzulassung, Alpha-Kurs → zusätzlich Alpha-Zusatzqualifikation, Kurse für Kinder → Führungszeugnis. Geprüft wird je Veranstaltung am Veranstaltungstag. | Muss |
| FA-123 | Bei der Einsatzplanung sind ungeeignete Dozierende gesperrt; der Tooltip nennt den Grund (z. B. „BAMF-Lehrkraftzulassung fehlt“, „Führungszeugnis abgelaufen am …“). Die Fachbereichsleitung kann mit Begründung eine protokollierte Ausnahme erteilen, wo Regeln das zulassen; bei Integrationskursen nicht. | Muss |
| FA-124 | Läuft eine Qualifikation bald ab oder fehlt ein Nachweis, entsteht eine Aufgabe für die Fachbereichsleitung; betroffene künftige Veranstaltungen werden markiert. | Muss |
| FA-125 | Im Dozierendenportal (Endausbau) laden Dozierende Nachweise selbst hoch; die Verwaltung gibt sie frei. | Kann |

### Ordnungsmäßigkeit der Finanzdaten (GoBD)

VibeVHS ist ein steuerlich relevantes Vorsystem der Finanzbuchhaltung, weil es Gebührenbescheide und Rechnungen erzeugt, per SEPA einzieht, mahnt, Honorare abrechnet und Buchungsdaten übergibt. Deshalb gelten für diese Daten die GoBD; die kommunale Rechnungsprüfung verlangt im Kern dasselbe. Eine Software kann nicht GoBD-zertifiziert sein; VibeVHS liefert die Funktionen, die Verantwortung und die Verfahrensdokumentation liegen bei der VHS. Die Fristen unten sind mit Kämmerei oder Steuerberatung zu bestätigen.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-126 | Unveränderbarkeit: Ein festgeschriebener Beleg (Bescheid, Rechnung, Gutschrift, Honorarabrechnung, Mahnung, Zahlung) wird nie überschrieben oder gelöscht. Korrekturen nur per Storno und Neubeleg mit Begründung und Verweis auf den Ursprungsbeleg. | Muss |
| FA-127 | Belegnummern sind je Belegart fortlaufend und lückenlos. Eine Lücke ist technisch ausgeschlossen oder wird begründet protokolliert. | Muss |
| FA-128 | Nachvollziehbarkeit: Jede Änderung an steuerlich relevanten Daten ist mit Zeitpunkt, Person und vorherigem Wert protokolliert. Das Protokoll ist manipulationsgeschützt (Hash-Kette) und prüfbar. | Muss |
| FA-129 | Zeitgerechte Erfassung: Zahlungseingänge und Belege tragen Belegdatum und Erfassungszeitpunkt. Festschreibung spätestens bei Übergabe an die Finanzbuchhaltung oder nach einstellbarer Frist. | Muss |
| FA-130 | Aufbewahrung: Buchungsbelege 8 Jahre, Handels- und Geschäftsbriefe 6 Jahre, Fristbeginn mit Ende des Kalenderjahres. Belege bleiben im Originalformat (PDF, XRechnung-XML) samt SHA-256 maschinell auswertbar. Fristen einstellbar. | Muss |
| FA-131 | Vorrang vor der Löschung: Personen mit Belegen in laufender Frist werden nur gesperrt, nicht anonymisiert (Art. 17 Abs. 3 DSGVO). Der Löschlauf nennt je Person die längste Frist und anonymisiert nach deren Ende automatisch. | Muss |
| FA-132 | Datenzugriff für Prüfungen: Rolle „Prüfung“ mit Nur-Lese-Zugriff auf Finanzdaten und Protokoll sowie Export aller Belege und Buchungen eines Zeitraums als CSV mit Beschreibungsdatei (index.xml nach Beschreibungsstandard). | Muss |
| FA-133 | Bausteine für die Verfahrensdokumentation: Das System erzeugt eine aktuelle Beschreibung von Belegarten, Nummernkreisen, Festschreibung, Schnittstellen, Rollen und Fristen. | Soll |
| FA-145 | Barzahlungen: VibeVHS ist keine Kasse. Bargeld läuft über die zertifizierte Kasse der Kommune bzw. VHS (§ 146a AO, KassenSichV mit TSE); VibeVHS erfasst nur den Zahlungseingang mit Verweis auf den Kassenbeleg. Eine eigene Kassenfunktion mit Cloud-TSE wäre eine spätere Ausbaustufe. Klären: wie die VHS heute Barzahlungen abwickelt. | Muss |

### Honorarmodelle

Honorare werden über austauschbare Honorarmodelle (Plugins) berechnet; mitgeliefert werden „Honorar pro UE“ und „Basishonorar pro Termin + pro UE + pro Teilnehmer am Termin“.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-139 | Ein Honorarmodell beschreibt seine Parameter (Typ, Pflicht, Standard, Einheit, Hilfetext); die Oberfläche erzeugt daraus das Formular. Ein Honorarvertrag wählt Modell und Parameter und ist zeitlich gültig. | Muss |
| FA-140 | Aufruf mit Zeitraum, Terminen (Datum, UE, Status, Ort, Entfernung), anonymen Teilnehmerdaten je Termin (angemeldet, anwesend, grobe Merkmale) und allen bisherigen Abrechnungen der Person; Ergebnis sind Buchungstexte und Beträge je Position. | Muss |
| FA-141 | Modelle sind deterministisch und ohne Zugriff auf Datenbank, Netz oder Uhr. Der Kern verhindert Doppelabrechnung von Terminen. | Muss |
| FA-142 | Jede festgeschriebene Honorarabrechnung speichert Modell, Version und Parameter als Snapshot (GoBD). | Muss |
| FA-143 | Mitgeliefert: „Honorar pro UE“ (optional anteilige Vergütung kurzfristig ausgefallener Termine) und „Basis je Termin + je UE + je anwesender Person“ (optional Mindest- und Höchstbetrag je Termin); beide mit optionalen Fahrtkosten je Termin oder je km. | Muss |
| FA-144 | Weitere Modelle lassen sich als Erweiterung nachladen, ohne den Kern zu ändern. | Soll |

### Kursevaluation

Kurse können von den Teilnehmenden bewertet werden. Welche Fragen gestellt werden, legt ein Fragebogen-Template fest. Wann evaluiert wird, legt der Kurs fest.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-147 | Fragebogen-Templates mit Fragen der Typen Ja/Nein, Likert-Skala (Stufenzahl und Beschriftung der Pole einstellbar), Freitext und 0 bis 5 Sterne. Jede Frage ist Pflicht oder optional, Fragen lassen sich in Abschnitte gliedern. Templates sind mehrsprachig wie Briefe. | Muss |
| FA-148 | Templates sind versioniert: Eine Änderung erzeugt eine neue Version mit Gültigkeitszeitraum. Eine laufende oder abgeschlossene Evaluation bleibt an die Version gebunden, mit der sie gestartet wurde, damit Ergebnisse vergleichbar und nachvollziehbar bleiben. | Muss |
| FA-149 | Template-Zuordnung mit Vorrang: Standard der vhs, je Fachbereich, je Kurstyp (z. B. Integrationskurs) und je Kurs. Der Kurs übernimmt das speziellste passende Template, die Programmplanung kann es überschreiben. | Soll |
| FA-150 | Auslöser je Kurs, mehrere kombinierbar: (a) in der letzten Veranstaltung, nur bei fester Terminzahl; (b) regelmäßig alle N Veranstaltungen; (c) Zwischenevaluation zur Kursmitte; (d) nach Kursende per Mail bzw. im Teilnehmendenportal mit Frist; (e) zu einem festen Stichtag, z. B. für unbefristete Kurse oder zum Semesterende; (f) manuell durch Verwaltung oder Dozent. Für unbefristete Kurse sind nur (b), (e) und (f) wählbar. | Muss |
| FA-151 | Erinnerung der Dozierenden vor dem Termin, an dem evaluiert wird (Dozierendenportal und Mail), mit dem Kurs-QR-Code zum Zeigen oder Ausdrucken. Wird nicht evaluiert, entsteht eine Aufgabe für den Fachbereich. | Muss |
| FA-152 | Erfassungsart je Evaluation: anonym oder namentlich. Sie wird beim Start festgelegt, den Teilnehmenden vor dem Ausfüllen deutlich angezeigt und danach nicht mehr geändert. | Muss |
| FA-153 | Nur Teilnehmende des Kurses können antworten, und jede Person höchstens einmal je Evaluation, auch bei anonymer Erfassung. Umsetzung: Jede belegte Person erhält ein persönliches Einmal-Token (Link im Portal bzw. per Mail, oder am Kurstermin über den Kurs-QR-Code mit Anmeldung bzw. Teilnehmendennummer). Bei der Abgabe wird nur vermerkt, dass das Token verbraucht ist. Bei anonymer Erfassung wird die Antwort ohne Verweis auf Token oder Person gespeichert, sodass sie nicht zurückverfolgt werden kann. Teilnahme am Handy, barrierearm und in der Sprache der Teilnehmenden. | Muss |
| FA-154 | Bei anonymer Erfassung werden Ergebnisse erst ab 5 Antworten angezeigt, sonst wäre die Anonymität in kleinen Kursen nicht gewahrt. Freitexte werden unverändert angezeigt; vor dem Ausfüllen gibt es einen Hinweis, keine persönlichen Daten einzutragen. Bei namentlicher Erfassung gilt keine Mindestzahl. | Muss |
| FA-155 | Auswertung je Kurs für das Dozententeam des Kurses (im Portal) und den Fachbereich: Verteilungen, Mittelwerte, Rücklaufquote, Freitexte. Verlauf über mehrere Zeitpunkte desselben Kurses. Export als PDF und CSV. | Muss |
| FA-156 | Auswertung über Kurse hinweg für Fachbereich und Leitung: je Dozent, Fachbereich, Kurstyp und Semester, nur über gleiche Template-Versionen bzw. gleiche Fragen vergleichbar. | Soll |
| FA-157 | Wer nicht zum Dozententeam des Kurses gehört, sieht Ergebnisse erst, nachdem er in einem modalen Dialog per ausdrücklicher Checkbox bestätigt hat, dass das Vorgehen mit den erforderlichen Gremien (z. B. Personalrat) abgestimmt ist. Die Bestätigung wird mit Person, Zeitpunkt und Evaluation protokolliert. | Muss |
| FA-158 | Aufbewahrung und Löschung: Rohantworten und Freitexte werden nach einer einstellbaren Frist gelöscht (Vorschlag 2 Jahre), zusammengefasste Kennzahlen bleiben. Namentliche Antworten fallen unter Auskunft und Löschung nach DSGVO. | Muss |

### Identitätsprüfung in Integrationskursen

Die Identität wird einmal am Originaldokument geprüft und im Kurs per Sichtvergleich mit einem Lichtbild bestätigt. Ein automatischer Gesichtsabgleich (biometrische Daten nach Art. 9 DSGVO) ist ausgeschlossen, weil Integrationskursverordnung und Teilnahmeverpflichtung weder Rechtsgrundlage noch freiwillige Einwilligung tragen. Die Einschätzung ist mit Datenschutzbeauftragter und BAMF-Regionalkoordination abzustimmen.

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-134 | Ausweisprüfung bei der Anmeldung: Die Verwaltung vergleicht das Originaldokument mit dem Berechtigungsschein und erfasst nur Prüfdatum, prüfende Person, Dokumentart und Ergebnis. Keine Ausweiskopie. Ohne Prüfung ist der Kursstart gesperrt (Tooltip mit Grund). | Muss |
| FA-135 | Lichtbild für den Sichtvergleich: Bei der Anmeldung wird mit Information nach Art. 13 DSGVO ein Foto aufgenommen. Es wird nur angezeigt, nie technisch abgeglichen, nicht auf dem Gerät gespeichert und mit Kursende plus einstellbarer Frist gelöscht. | Muss |
| FA-136 | Beim Check-in Weg B zeigt das Handy der Kursleitung nach dem Scan des persönlichen QR-Codes Name und Lichtbild; die Kursleitung bestätigt „Stimmt überein“ oder „Zweifel“. | Muss |
| FA-137 | Bei „Zweifel“ entsteht eine Aufgabe „Ausweiskontrolle“ für die Verwaltung; die Anwesenheit bleibt bis zur Klärung vorläufig und zählt nicht für die BAMF-Abrechnung. | Muss |
| FA-138 | Ausbaustufe: Vor-Ort-Auslesen des Personalausweises oder elektronischen Aufenthaltstitels per NFC (§ 18a PAuswG) mit Berechtigungszertifikat; ergänzt FA-134, ersetzt sie nicht (Duldung, Gestattung, Papierdokumente). | Kann |

### Hilfe, Dokumentation und Rechtliches

| ID | Anforderung | Prio |
| --- | --- | --- |
| FA-159 | Impressum (Anbieterkennzeichnung, § 5 DDG) ist ohne Anmeldung erreichbar und in allen Fußzeilen verlinkt; Leitung und Systemverwaltung pflegen den Text in den Einstellungen. Solange er fehlt, zeigt die Seite einen deutlich markierten Platzhalter. | Muss |
| FA-160 | Leitung, Verwaltungsleitung und Systemverwaltung lesen Spezifikation und technische Doku in der Anwendung und kommentieren sie je Abschnitt: Antworten, Status offen/erledigt, Übersicht aller Kommentare und Export als Markdown zum Einarbeiten. Andere Rollen sehen den Menüpunkt gesperrt mit Begründung; die Rechte prüft der Dienst. | Muss |

## Querschnittsanforderungen

Diese Regeln gelten für jede Maske und jeden Ablauf. Sie entscheiden, ob das System die Verwaltungsmitarbeitenden überzeugt.

### Zeitliche Gültigkeit der Daten (Historisierung)

Jede fachlich relevante Angabe hat einen Gültigkeitszeitraum. Dokumente zu einem Vorgang zeigen den Stand zum Zeitpunkt des Vorgangs. Das löst das Beispiel der Dozentin, die nach der Heirat einen neuen Nachnamen trägt: Eine nachgedruckte Bescheinigung für einen Kurs von 2024 zeigt den Namen von 2024.

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-01 | Stammdaten mit Historie (Name, Anschrift, Bankverbindung, Ermäßigungsstatus, Entgelte, Honorsätze, Raumdaten, Kursbezeichnung) werden als Versionen mit „gültig von“ und „gültig bis“ gespeichert, nie überschrieben. | Muss |
| QA-02 | Zusätzlich wird gespeichert, wann eine Version im System erfasst wurde (zweite Zeitachse). So lassen sich rückwirkende Korrekturen von echten Änderungen unterscheiden (bitemporales Modell). | Muss |
| QA-03 | Abfragen und Dokumente kennen einen Stichtag. Standard für Bescheinigungen ist das Kursende, für Rechnungen das Rechnungsdatum, für Verträge das Vertragsdatum. | Muss |
| QA-04 | Erzeugte Dokumente (Rechnung, Bescheinigung, Vertrag, Mahnung) werden als unveränderliche PDF mit den verwendeten Daten archiviert. Ein Nachdruck nutzt das Archiv; eine Neuerzeugung ist nur bewusst mit Begründung möglich. | Muss |
| QA-05 | Die Oberfläche zeigt bei historisierten Feldern die Historie per Klick und fragt bei Änderungen: „Ab wann gilt das?“ (Standard: heute). | Muss |
| QA-06 | Unterschied zwischen Korrektur (Tippfehler, gilt rückwirkend) und Änderung (Heirat, gilt ab Datum) ist in der Oberfläche wählbar und wird protokolliert. | Muss |

Das ist ein bekanntes Muster (Temporal Data, bitemporale Datenhaltung, in Data-Warehouses „Slowly Changing Dimension Typ 2“). Das konkrete Datenmodell folgt in Schritt 2.

### Bedienung

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-10 | Nach jedem abgeschlossenen Schritt bietet das System passende Folgeschritte als Buttons an (siehe Use Cases). | Muss |
| QA-11 | Jedes gesperrte Feld und jeder deaktivierte Button erklärt im Tooltip, warum, und wenn möglich, was zu tun ist (z. B. „Kurs ist abgeschlossen. Bearbeiten nur durch Fachbereichsleitung.“). | Muss |
| QA-12 | Globale Suche nach Person, Kurs, Rechnung, Raum über ein Eingabefeld; Tastaturbedienung für den Tresen. | Muss |
| QA-13 | Aufgabenliste je Person: Wiedervorlagen, automatisch erkannte Probleme (Unterbelegung, fehlende Nachweise, offene Posten). Details unter „Aufgabensteuerung“. | Muss |
| QA-14 | Barrierefreiheit nach BITV 2.0 / WCAG 2.1 AA. | Muss |
| QA-15 | Responsive Oberfläche; Dozierenden- und Teilnehmerfunktionen auf dem Smartphone. | Muss |
| QA-56 | Hilfe in der Anwendung für alle Verwaltungsrollen: Grundprinzipien und die häufigsten Abläufe Schritt für Schritt, abgeleitet aus Spezifikation und Abnahme-Drehbuch, mit Inhaltsverzeichnis. | Muss |
| QA-57 | Abnahme- und Schulungsmodus: Geführte Touren zeigen mit Sprechblasen direkt an der Oberfläche, was anzuklicken oder einzutragen ist, und gehen weiter, sobald es getan ist (Zurück, Überspringen, Beenden, Zeitmessung). Die Touren folgen dem Abnahme-Drehbuch, sind im Test- und Schulungsbetrieb einschaltbar und werden automatisch auf Lauffähigkeit geprüft. | Soll |

### Kommunikation, Übersetzung und QR-Codes

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-20 | Jede Mail an Kundinnen und Kunden enthält einen Link „Diese Nachricht in Ihrer Sprache lesen“. Die bevorzugte Sprache wird an der Person gespeichert. | Muss |
| QA-21 | Jeder erzeugte Brief trägt einen QR-Code „Translate this letter in your language“, der zu einer Übersetzungsseite des Briefs führt. | Muss |
| QA-22 | Die Übersetzungsseite ist ohne Login erreichbar, aber nur über ein nicht erratbares Token, und läuft nach einer Frist ab (Datenschutz). | Muss |
| QA-23 | QR-Codes für Kursanmeldung (öffentlich), Termin-Check-in (je Termin, zeitlich begrenzt gültig) und persönlichen Teilnehmerausweis. | Muss |
| QA-24 | Alle Vorlagen (Mail, Brief) zentral pflegbar, mit Platzhaltern und Vorschau. | Muss |

### Automatisierung mit Protokoll

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-30 | Routineaufgaben laufen zeitgesteuert: Erinnerungen vor Kursbeginn, Mindestteilnehmerprüfung, Wartelisten-Nachrücken, Lastschriftvorbereitung, Mahnvorschlag, Löschfristen. | Muss |
| QA-31 | Jeder automatische Lauf schreibt ein Protokoll: was, wann, für wen, mit welchem Ergebnis. Die Einträge sind am betroffenen Kurs bzw. an der Person sichtbar. | Muss |
| QA-32 | Kritische Automatismen (Absage, Mahnung, Lastschrift) laufen zunächst als Vorschlag, den ein Mensch freigibt; die Freigabe ist je Automatismus einstellbar. | Muss |
| QA-33 | Jede manuelle Änderung an Stammdaten und Buchungen wird mit Benutzer und Zeitpunkt protokolliert (Audit-Log). | Muss |

### Aufgabensteuerung

Nicht jede Person sieht alle Aufgaben. Aufgaben entstehen aus Regeln, gehen an die zuständige Gruppe und werden dort von einer Person übernommen.

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-40 | Jeder Aufgabentyp legt die Zuständigkeit fest: Rolle, eingeschränkt auf Fachbereich und Semester (Datenrechte), optional eine feste Person (z. B. Sachbearbeitung des Kurses). | Muss |
| QA-41 | Neue Aufgaben landen im Gruppenkorb. „Übernehmen“ verschiebt sie in die persönliche Liste; alle anderen sehen, wer sie bearbeitet. Zuweisen an Kolleginnen und Kollegen ist möglich. | Muss |
| QA-42 | Jede Aufgabe hat eine Fälligkeit aus dem Fall (z. B. 2 Tage vor Kursbeginn) und eine Dringlichkeit. | Muss |
| QA-43 | Überfällige Aufgaben werden hervorgehoben und nach einer je Typ einstellbaren Frist an die Leitung eskaliert. | Muss |
| QA-44 | Abwesenheiten mit Vertretung: Aufgaben abwesender Personen erscheinen bei der Vertretung. | Soll |
| QA-45 | Entfällt der Anlass (z. B. Mindestzahl doch erreicht), schließt das System die Aufgabe selbst und protokolliert das. | Muss |
| QA-46 | Jede Aufgabe führt per Klick direkt zur Maske mit dem passenden Folgeschritt. | Muss |
| QA-47 | Startseite: „Meine Aufgaben“, „Offen in meinen Gruppen“, „Bei Kolleginnen und Kollegen“; für die Leitung zusätzlich eine Übersicht überfälliger Aufgaben je Gruppe. | Muss |
| QA-48 | Übernahme, Zuweisung, Erledigung, Eskalation und automatisches Schließen werden protokolliert. | Muss |

### Listen mit vielen Datensätzen

Die Verwaltung sucht meist gezielt. Listen starten deshalb mit Suche und Filtern und zeigen das Ergebnis vollständig als durchgehend scrollbare Liste, die Daten erst beim Scrollen nachlädt. Blättern und harte Obergrenzen entfallen.

| ID | Anforderung | Prio |
| --- | --- | --- |
| QA-50 | Suche zuerst: Eingabefeld mit sofortigen Treffern, tolerant gegenüber Schreibweisen (Yılmaz/Yilmaz, ä/ae, Bindestriche). | Muss |
| QA-51 | Filter als Chips mit Trefferzahl je Wert (z. B. „Dozierende 182“); die Gesamtzahl der Treffer steht immer sichtbar über der Liste. | Muss |
| QA-52 | Ergebnislisten scrollen durchgehend; das System lädt Datensätze abschnittsweise nach und zeichnet nur die sichtbaren Zeilen (virtuelles Scrollen). | Muss |
| QA-53 | Spaltenkopf bleibt stehen, Sortierung per Klick, Scrollposition und Filter bleiben beim Zurückkehren erhalten. | Muss |
| QA-54 | Sammelaktionen und Export beziehen sich auf das gesamte Filterergebnis, nicht nur auf die sichtbaren Zeilen („Alle 1.312 Treffer anschreiben“). | Muss |
| QA-55 | Nur die Schnellsuche in der Kopfzeile zeigt eine begrenzte Auswahl (8 Treffer) mit dem Link „Alle 312 Treffer anzeigen“. | Muss |

## Schnittstellen

24 Schnittstellen sind für den Endausbau vorgesehen. Jede liegt hinter einem eigenen Adapter (NFA-10): In Stufe 1 gibt es für die von der Verwaltung benötigten einen Mock mit realistischem Datenformat, die übrigen sind nur konzeptionell vorgesehen. Details folgen im Integrationskonzept (Schritt 2).

| Nr. | Schnittstelle | Zweck | Richtung | Format / Standard | Stufe 1 |
| --- | --- | --- | --- | --- | --- |
| S-01 | Kommunale Anmeldung (AD, Keycloak, ADFS, Entra ID) | Login und Rollen der Mitarbeitenden | ein | OIDC, SAML 2.0 | Mock-Login |
| S-02 | Konten für Portale | Login für Teilnehmende und Dozierende ohne AD | intern | E-Mail, Passkey/2FA | konzeptionell |
| S-03 | Kommunale Finanzsoftware (SAP, Infoma, H+H u. a.) | Buchungen, Personenkonten, Zahlungseingänge zurück | aus / ein | CSV, XML, ggf. Hersteller-API | Mock |
| S-04 | Bank: Lastschrift | Einzug von Kursentgelten | aus | SEPA pain.008 | Mock (Datei) |
| S-05 | Bank: Überweisung | Honorare, Erstattungen, Fahrtkosten | aus | SEPA pain.001 | Mock (Datei) |
| S-06 | Bank: Kontoauszug | Zahlungsabgleich, Rückläufer | ein | camt.053 / camt.054 | Mock (Datei) |
| S-07 | Online-Zahlung im Portal | Bezahlung bei Buchung | aus / ein | ePayBL oder Payment-Provider | konzeptionell |
| S-08 | E-Rechnung | Rechnungen an Behörden und Firmen | aus | XRechnung, ZUGFeRD, Peppol | Mock |
| S-09 | BAMF InGe-Online | Kursbeginn, Teilnehmerauskunft, Testmeldungen | aus / ein | BAMF-Webservices | Mock |
| S-10 | BAMF Abrechnung Integrationskurse und Berufssprachkurse (DeuFöV) | Abrechnungsbogen, Anwesenheitslisten | aus | BAMF-Formulare, PDF | Mock |
| S-11 | Prüfungsanbieter (z. B. telc, g.a.s.t.) | Prüfungsanmeldung, Ergebnisse | aus / ein | Anbieterportal, CSV | konzeptionell |
| S-12 | Bundesagentur für Arbeit / Jobcenter | Kurse mit Bildungsgutschein melden, Kostenträgerabrechnung | aus | Weiterbildungsdatenbank, XRechnung | konzeptionell |
| S-13 | DIE-Volkshochschul-Statistik | Jahresstatistik | aus | DIE-Erhebungsformat | Mock |
| S-14 | Landesstatistik und Förderabrechnung (NRW: Weiterbildungsgesetz) | Unterrichtsstunden für Förderung | aus | Landesformat | Mock |
| S-15 | vhs.cloud | Konten und Kurse anlegen, Kursaufnahme | aus | vhs.cloud-API | konzeptionell |
| S-16 | Kursportale (meine-vhs.de, KOS, vhs2gether, Weiterbildungsdatenbanken) | Kursdaten veröffentlichen und tauschen | aus / ein | XML, PAS 1045, REST | konzeptionell |
| S-17 | VHS-Website / CMS | Kursdaten für die Homepage, Buchungslink | aus | REST/JSON | Mock |
| S-18 | Programmheft | Satzdaten für das gedruckte Programm | aus | XML für InDesign, DOCX | konzeptionell |
| S-19 | E-Mail und SMS | Bestätigungen, Erinnerungen, Mahnungen | aus | SMTP, SMS-Gateway | Mock (Postfach) |
| S-20 | Übersetzungsdienst | Übersetzungslink in Mails, QR-Code auf Briefen | aus | DeepL API, LibreTranslate | Mock |
| S-21 | Druck und Hybridpost | Briefversand in großer Menge | aus | PDF, Druckdienstleister | konzeptionell |
| S-22 | DMS / E-Akte der Kommune | Archivierung von Verträgen, Bescheinigungen | aus | CMIS, Hersteller-API | konzeptionell |
| S-23 | Kalender | Termine für Dozierende, Teilnehmende, Raumbuchung | aus | iCalendar (ICS), Exchange | konzeptionell |
| S-24 | Gebäudetechnik und Raumanzeige | Raumanzeige, Schließsystem, Heizung nach Belegung | aus | REST, Webseite | konzeptionell |
| S-25 | Messenger (Signal, WhatsApp) | Kurzfristige Hinweise an Teilnehmende ohne Mailroutine (z. B. Integrationskurse) | aus | Business-API des Dienstes; Auftragsverarbeitung vorher klären | konzeptionell |
| S-26 | Virenscanner der Kommune | Prüfung hochgeladener Dateien (Unterlagen, Nachweise) | aus | z. B. ClamAV (clamd) | Mock |
| S-27 | Geokodierung | Entfernung Wohnort–Kursort (BAMF-Fahrtkosten), Wegbeschreibung | aus | Geodienst (Anbieter offen) | Mock |
| S-28 | Externe Datensicherung | Fortlaufende Kopie der Datenbank außerhalb des Servers | aus | WAL-Archivierung von PostgreSQL (z. B. pgBackRest) → S3-Speicher des Rechenzentrums | konzeptionell |

Eine eigene dokumentierte REST-API (OpenAPI) bildet die Grundlage für alle Portale und Partneranbindungen.

## Nichtfunktionale Anforderungen

Das System läuft als ein Docker-Image im kommunalen Rechenzentrum und hält alle Daten in Deutschland. Die Werte unten sind Startannahmen für eine mittelgroße VHS und werden mit dem Betrieb abgestimmt.

| ID | Bereich | Anforderung |
| --- | --- | --- |
| NFA-01 | Betrieb | Ein Docker-Image (ASP.NET Core, Linux), Konfiguration über Umgebungsvariablen, PostgreSQL 17 als eigener Container mit Volume |
| NFA-02 | Mengen | Bis 3.000 Kurse und 40.000 Belegungen pro Jahr, 30 gleichzeitige Verwaltungsnutzer, 500 gleichzeitige Web-Besucher bei Anmeldestart |
| NFA-03 | Antwortzeit | Masken der Verwaltung unter 1 s (95 %), Suche unter 0,5 s |
| NFA-04 | Anmeldung | OIDC oder SAML gegen die kommunale Anmeldung (AD, Keycloak, ADFS); Rollen aus Gruppen; Zwei-Faktor für Admin und Buchhaltung |
| NFA-05 | Externe Konten | Teilnehmende und Dozierende melden sich ohne AD an (E-Mail-Login mit Magic Link oder Passwort und 2FA) |
| NFA-06 | DSGVO | Verzeichnis der Verarbeitungstätigkeiten unterstützen, Löschfristen je Datenart, Anonymisierung, Auskunft nach Art. 15, Einwilligungsnachweise |
| NFA-07 | Sicherheit | OWASP ASVS Stufe 2 als Ziel, TLS überall, Verschlüsselung sensibler Felder (IBAN, Aufenthaltsdaten) |
| NFA-08 | Datensicherung | Tägliche konsistente Sicherung der Datenbank (pg_dump), Wiederherstellung bei jeder Sicherung in eine Wegwerf-Datenbank getestet |
| NFA-09 | Nachvollziehbarkeit | Audit-Log manipulationsgeschützt (verkettete Hashes), Aufbewahrung nach Fristen (Buchungsbelege 10 Jahre, § 147 AO) |
| NFA-10 | Schnittstellen | Alle Schnittstellen hinter Adaptern; Mock und echte Implementierung austauschbar per Konfiguration |
| NFA-11 | Sprache | Oberfläche Deutsch, Teilnehmerportal mehrsprachig vorbereitet |
| NFA-12 | Wartbarkeit | Automatisierte Tests, Datenbankmigrationen versioniert, Open-Source-Bibliotheken mit kompatibler Lizenz |
| NFA-13 | Bundesland | Landesspezifisches (Ferien, Feiertage, Landesstatistik, Förderregeln, Formulare) liegt hinter einer austauschbaren Länderkonfiguration. Stufe 1: NRW fest hinterlegt. |

### Zeitangaben: Speicherung und Darstellung

| ID | Anforderung | Prio |
| --- | --- | --- |
| NFA-14 | Zeitpunkte (Erfassung, Ersetzung, Zahlungen, Protokoll, Termine mit Uhrzeit) werden ausschließlich in UTC gespeichert, als Ganzzahl in Millisekunden seit 1970-01-01 (Unix-Epoch). Keine Speicherung als Text und keine lokale Zeit in der Datenbank. | Muss |
| NFA-15 | Reine Kalendertage ohne Uhrzeit (Gültigkeiten, Geburtsdatum, Fälligkeiten) werden als Ganzzahl im Format JJJJMMTT gespeichert; offenes Ende = 99991231. Sie werden nie in Zeitzonen umgerechnet. | Muss |
| NFA-16 | Anzeige und Eingabe erfolgen in einer einstellbaren Zeitzone. Ist keine eingestellt, gilt die Systemzeitzone. Bei Mandantenfähigkeit ist die Zeitzone je Mandant einstellbar. Alle Umrechnungen laufen über einen zentralen Dienst. | Muss |
| NFA-17 | Wiederkehrende Termine werden aus der lokalen Regel (z. B. „dienstags 18:00“) für jeden Termin einzeln in UTC umgerechnet, sodass sie über die Zeitumstellung hinweg zur gleichen Ortszeit stattfinden. Tests decken beide Umstellungstage ab. | Muss |
| NFA-18 | Der Betrieb stellt die Systemzeitzone des Servers korrekt ein (z. B. Europe/Berlin), sofern keine Zeitzone konfiguriert ist. | Muss |
| NFA-19 | Für die Prüfung der Datenbank gibt es lesbare Sichten (Views), die Zeitangaben als Datum und Uhrzeit in der eingestellten Zeitzone zeigen. Das Speicherformat ist in der Schema-Dokumentation beschrieben. | Soll |

### Datenhaltung

| ID | Anforderung | Prio |
| --- | --- | --- |
| NFA-20 | Prüfsummen (Finanzprotokoll, Belege, Dokumente) werden über die fachlichen Werte gebildet, nicht über die Speicherform. Ein Wechsel der Speicherform (z. B. Id- oder Datumsformat) darf sie nicht verändern. | Muss |
| NFA-21 | Die Datenbank ist normalisiert (3. Normalform): keine Kopien von Werten aus anderen Tabellen oder Versionen, keine JSON-Spalten mit relationalem Inhalt. Der Stand zu einem Stichtag wird zur Abfragezeit berechnet. Ausnahmen sind nur bewusst eingefrorene Dokumente (Belege, Mahn- und Zahlungsposten, Postausgang) und Anonymisierungsreste; sie sind dokumentiert, und ein Architekturtest verhindert neue Kopien. | Muss |
| NFA-22 | Erfassungsangaben (Zeitpunkt, Person, Art, Grund) stehen einmal je Änderungsvorgang in einer gemeinsamen Tabelle; Versionen verweisen darauf. | Muss |
| NFA-23 | Ids sind GUIDs, gespeichert als 16-Byte-Binärwert und zeitlich sortiert erzeugt (UUIDv7). | Muss |
| NFA-24 | Namen werden nach DIN 5007-2 sortiert (ä = ae, ß = ss, Namensvorsatz zählt nicht), über eine Sortierregel der Datenbank statt einer gespeicherten Hilfsspalte. | Muss |
| NFA-25 | Je Mandant (vhs) gibt es eine eigene Datenbank. | Muss |
| NFA-26 | Kommentare zur Spezifikation liegen in einer eigenen Datenbank mit eigener Schemaversion; sie überstehen den Neuaufbau der Hauptdatenbank und werden mit ihr gesichert. | Muss |
| NFA-27 | Solange es keine Migrationen gibt (Entwicklungsphase), legt das Ausrollen bei einer Schemaänderung die alte Datenbank beiseite, statt sie zu löschen; scheitert der Start, kommen vorige Version und vorige Datenbank automatisch zurück. Vor dem ersten Produktivbetrieb werden Migrationen eingeführt. | Muss |
| NFA-28 | Kurzformen einer Id (Pseudonym „Person #…“ im Protokoll, Platzhalter nach Anonymisierung) stammen aus dem zufälligen Teil der UUIDv7, nicht aus dem Zeitstempel – sonst sehen alle Personen eines Imports gleich aus. Eindeutige Schlüssel wie Logins der Portal-Konten werden aus der vollen Id gebildet. | Muss |
| NFA-29 | Die Suche nach Personen bleibt auch bei vielen Personen schnell, ohne eigene Suchtabelle und ohne Kopien: Trigramm-Indizes auf den normalisierten Suchausdrücken der Versionstabellen (PostgreSQL pg_trgm). | Muss |
| NFA-30 | Tests laufen gegen dieselbe Datenbank wie der Betrieb (PostgreSQL); je Test eine Wegwerf-Datenbank als Kopie einer Vorlage, damit Tests unabhängig und schnell bleiben. | Muss |

## Entscheidungen

Die offenen Fragen sind am 14.11.2026 geklärt. Die Annahmen gelten als Vorgabe.

| Thema | Entscheidung |
| --- | --- |
| Kommunale Anmeldung | OIDC; in Stufe 1 Mock-Login |
| Finanzsoftware | CSV-Mock, echte Anbindung später |
| Bundesland | NRW, in Stufe 1 fest hinterlegt; konzeptionell austauschbar (NFA-13) |
| Datenbank | PostgreSQL 17 (Entscheidung Klaus, 24.11.2026: „mit PostgreSQL im Docker so einfach“); bis dahin SQLite über dieselbe Datenzugriffsschicht |
| Übersetzung | Austauschbarer Adapter, in Stufe 1 Mock |
| Selbstregistrierung | Erlaubt; selbstregistrierte Teilnehmende buchen nur öffentliche Kurse (FA-60 bis FA-68) |
| Umfang | Analyse und Datenmodell für den Endausbau mit Portalen; Implementierung zuerst nur Verwaltung |

## Quellen

- [Kufer SQL – Beschreibung zur DSGVO (Gemeinde Peiting)](https://www.peiting.de/wp-content/uploads/2018/05/Kufer-SQL-VHS-Beschreibung.pdf)
- [Kufer Broschüre 2021](https://www.kufer.de/fileadmin/user_upload/dokumente/broschuere/Kufer_Broschuere_2021.pdf)
- [Kufer Flyer BAMF](https://www.kufer.de/fileadmin/user_upload/dokumente/flyer/bamf/Flyer_BAMF_2017.pdf)
- [cmxOrganize bei SoftGuide](https://www.softguide.de/programm/cmxorganize-die-cloudbasierte-unternehmensloesung/vorschau)
- [vhs.cloud – Checkliste Kursverwaltungssoftware](https://www.vhs-cloud2.digionline.de/wws/bin/242374-1349574-1-vhs_cloud_checkliste_kursverwaltungssoftware.pdf)
- [vhs Bayern – Technische Möglichkeiten zum Kursdatentausch](https://vhs-bayern.de/wp-content/uploads/2024/01/DV_Kompetenzcluster_Technische_Moeglichkeiten_zum_Kursdatentausch.pdf)
