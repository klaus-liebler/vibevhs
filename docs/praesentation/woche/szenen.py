#!/usr/bin/env python3
"""Drehbuch „Eine turbulente Woche an der vhs“ als Daten: erzeugt folien.json und drehbuch.md.

Aufruf aus dem Repo-Wurzelverzeichnis:  py -3 docs/praesentation/woche/szenen.py
Bilder: bilder/<name>.jpg aus den Bildschirmfotos von tools/praesentation/app-bilder.mjs (aufnahmen.json), aufbereitet mit bilder.py.
"""
import json
from pathlib import Path

ORDNER = Path(__file__).resolve().parent
FUSS = 'Eine turbulente Woche an der vhs · echte Bildschirmfotos, Testdaten'

# (Zeit, Wer, Titel, Text, Bild, Bereiche, Nutzen-Marke, Nutzen-Satz, Sprechernotiz)
TAGE = [
    ('Montag, 23. November', 'Volle Kurse, ein Doppelgänger und eine Absage',
     'Der Tresen öffnet. Über Nacht hat VibeVHS schon gearbeitet – und bis zum Abend kommt einiges dazu.', [
        ('7:30', 'Verwaltungsleitung · Sonja Eickelberg', 'Was über Nacht passiert ist',
         'Die Startseite fasst zusammen, was die Automatik erledigt hat: Mahnvorschläge, Lastschriftlauf, Honorarentwürfe, festgeschriebene Belege. '
         'Was eine Freigabe braucht, ist als „Prüfung nötig“ markiert.',
         'mo-0730-start', ['Startseite', 'Automatik', 'Protokoll'], 'Für die Verwaltung', 'Der Tag beginnt mit einer Liste, nicht mit einer Suche.',
         'Jeder automatische Schritt steht mit Uhrzeit im Protokoll. Gleichartige Schritte werden zusammengefasst, damit die Startseite lesbar bleibt.'),
        ('7:40', 'Leitung · Dr. Kai Lüdeking', 'Was liegen geblieben ist',
         'Die Leitung sieht, welche Aufgaben in den Körben überfällig sind und an sie eskaliert wurden – mit Grund und Fälligkeit. '
         'Übernehmen oder zuweisen geht mit einem Klick.',
         'mo-0740-leitung', ['Aufgaben', 'Eskalation'], 'Für die Leitung', 'Nichts versandet: Was liegen bleibt, landet automatisch auf dem richtigen Tisch.',
         'Eskalationsfristen stellt die Verwaltung je Aufgabenart ein (Aufgaben → Regeln).'),
        ('7:45', 'Integrationskurs-Team · Sylvia Bergmann', 'Der Korb des Teams',
         'Zwei Integrationskurse warten auf die Kursbeginn-Meldung ans BAMF, eine Ausweiskontrolle ist offen, eine Raumzulassung läuft ab. '
         'Jede Karte sagt, was zu tun ist.',
         'mo-0745-ik-korb', ['Aufgaben', 'Integrationskurse'], 'Für das Team', 'Der Gruppenkorb ersetzt Post-its und Zurufe – mit Vertretung bei Urlaub.',
         'Aufgaben entstehen aus Regeln und schließen sich selbst, wenn die Ursache behoben ist.'),
        ('8:30', 'Teilnehmerservice · Katja Friedsen', 'Yoga ist ausgebucht',
         'Nadia Haddad möchte zu „Yoga am Abend“. Schon in der Kursauswahl steht: 16 von 16 Plätzen belegt, drei warten.',
         'mo-0830-yoga-voll', ['Anmeldung am Tresen'], 'Für die Teilnehmenden', 'Eine ehrliche Auskunft in Sekunden – noch am Tresen.',
         'Die Buchungsprüfung zeigt bei jedem Kurs, ob und warum er buchbar ist.'),
        ('8:31', 'Teilnehmerservice', 'Warteliste statt Absage',
         'VibeVHS schlägt die Warteliste vor (Platz 4). Wer darf, kann bewusst überbuchen – das wird protokolliert. Das Entgelt ist gesperrt und erklärt, warum.',
         'mo-0831-zahlung', ['Anmeldung', 'Warteliste'], 'Für die Verwaltung', 'Ausnahmen sind möglich, aber nie unbemerkt.',
         'Gesperrte Felder erklären per Tooltip, warum sie nicht änderbar sind.'),
        ('8:32', 'Teilnehmerservice', 'Und was jetzt?',
         'Gespeichert und protokolliert. „Wie geht es weiter?“ bietet die sinnvollen Folgeschritte an: Bestätigung ansehen, weitere Anmeldung, zur Person, zum Kurs.',
         'mo-0832-warteliste', ['Folgeschritte'], 'Für die Verwaltung', 'Nach jedem Schritt der nächste – ohne Suchen im Menü.',
         'Frau Haddad hat keine E-Mail-Adresse; VibeVHS weist darauf hin, die Bestätigung auszudrucken.'),
        ('9:30', 'Teilnehmerservice', 'Gibt es Frau Kowalenko schon?',
         'Eine Teilnehmerin soll neu angelegt werden. Mit dem Geburtsdatum erkennt VibeVHS: Olena Kovalenko gibt es schon – ein Buchstabe Unterschied.',
         'mo-0930-dublette', ['Personen', 'Dubletten'], 'Für die Verwaltung', 'Keine doppelten Personen, keine doppelten Mahnungen.',
         'Speichern ist erst möglich, wenn bestätigt ist, dass es eine andere Person ist. Schreibweisen wie Yilmaz/Yılmaz gelten als gleich.'),
        ('9:35', 'Integrationskurs-Team · Sylvia Bergmann', 'Anwesenheit im Integrationskurs',
         'Im Integrationskurs ist die Anwesenheit abrechnungsrelevant. Selbst-Check-ins werden bestätigt, Minuten und Unterrichtseinheiten je Person gezählt.',
         'mo-0935-ik-anwesenheit', ['Anwesenheit', 'Integrationskurse'], 'Für das Team', 'Die BAMF-Abrechnung stimmt, weil die Liste stimmt.',
         'Beim Sichtvergleich mit dem Lichtbild kann „Zweifel“ vermerkt werden; dann ist die Anwesenheit vorläufig und es entsteht eine Aufgabe Ausweiskontrolle.'),
        ('10:30', 'Fachbereichsleitung · Varvara Schatte', '„Gesund kochen“ hat zu wenige Anmeldungen',
         'Am Donnerstag sollte der Kochkurs starten. Die Mindestteilnehmerzahl ist nicht erreicht.',
         'mo-1030-kochen', ['Kurse', 'Mindestteilnehmerzahl'], 'Für die Fachbereiche', 'Die Entscheidung fällt rechtzeitig, nicht am Abend in der Lehrküche.',
         'Die Aufgabe „absagen oder durchführen?“ entsteht automatisch vor Kursbeginn.'),
        ('10:32', 'Fachbereichsleitung', 'Absagen – mit Grund',
         'Die Bestätigung nennt, wie viele Personen und Termine betroffen sind. Ohne Grund bleibt der Knopf gesperrt – der Grund steht in der Absage.',
         'mo-1032-absage', ['Kurse', 'Absage'], 'Für die Teilnehmenden', 'Alle erfahren es sofort und mit Begründung.',
         'Offene Belegungen werden ohne Gebühr aufgehoben, Rechnungen im nächsten Lauf storniert.'),
        ('10:33', 'Fachbereichsleitung', 'Abgesagt und benachrichtigt',
         'Der Kurs ist abgesagt, die künftigen Termine fallen aus, die Lehrküche ist wieder frei. Die Absagen liegen im Postausgang.',
         'mo-1033-abgesagt', ['Kurse', 'Postausgang'], 'Für die Verwaltung', 'Ein Klick statt zwölf Telefonate.',
         'Die Absage lässt sich nicht zurücknehmen – darauf weist die Bestätigung hin.'),
        ('11:30', 'Teilnehmerservice · Katja Friedsen', 'Ein Platz wird frei',
         'Zoé Hannecker zieht nach Münster und meldet sich von Yoga ab. Der Grund kommt in die Belegung.',
         'mo-1130-abmelden', ['Belegung', 'Abmeldung'], 'Für die Teilnehmenden', 'Abmelden geht am Telefon in einer Minute.',
         'Abmeldebedingungen und Gebühren richtet VibeVHS nach den Geschäftsbedingungen.'),
        ('11:31', 'Teilnehmerservice', 'Nachrücken – automatisch',
         'Sabine Weber, Platz 1 der Warteliste, bekommt sofort ein Nachrückangebot mit Frist.',
         'mo-1131-nachruecken', ['Warteliste', 'Postausgang'], 'Für die Teilnehmenden', 'Wer wartet, ist zuerst dran – fair und ohne Liste auf Papier.',
         'Läuft die Frist ab, geht das Angebot an die nächste Person.'),
        ('11:35', 'Teilnehmerservice', 'Die Absage im Postausgang',
         'Jede Mail an Teilnehmende enthält einen Link zur Übersetzung in die gewünschte Sprache. Im Testsystem verlässt nichts das Haus.',
         'mo-1135-mail-absage', ['Postausgang', 'Mehrsprachigkeit'], 'Für die Teilnehmenden', 'Verstanden werden, auch ohne Deutschkenntnisse.',
         'Mails und Schnittstellenaufrufe stehen mit vollem Text im Postausgang; jede Zustellung im Protokoll.'),
        ('17:30', 'IT · Amy Behrens', 'Ein Bildschirm für das Foyer',
         'Bevor die Abendkurse beginnen, richtet Amy Behrens einen Info-Monitor ein. Die geheime Adresse erscheint nur einmal; ein Fernseher mit Browser genügt.',
         'mo-1730-monitore', ['Info-Monitore'], 'Für die IT', 'Kein eigenes Digital-Signage-System nötig.',
         'Geht die Adresse verloren, wird eine neue erzeugt; die alte lässt sich sperren.'),
        ('18:25', 'Foyer', 'Wohin heute Abend?',
         'Der Monitor zeigt die Kurse der nächsten Stunden mit Raum – Raumänderungen und Ausfälle hervorgehoben, ohne Namen von Teilnehmenden.',
         'mo-1825-monitor', ['Info-Monitore'], 'Für die Teilnehmenden', 'Der richtige Raum, ohne am Tresen zu fragen.', ''),
        ('18:30', 'Abenddienst · Katja Friedsen', 'Die Abendkurse laufen',
         'Acht Veranstaltungen heute. Die Kursleitungen erfassen die Anwesenheit selbst, Teilnehmende checken per QR-Code ein.',
         'mo-1830-anwesenheit', ['Anwesenheit', 'Check-in'], 'Für die Kursleitungen', 'Keine Papierlisten, die am Monatsende abgetippt werden.',
         'Für Integrationskurse ist die Anwesenheit abrechnungsrelevant; dort bestätigt die Kursleitung den Selbst-Check-in.'),
        ('18:32', 'Abenddienst', 'Eine Anwesenheitsliste',
         'Wer ist da, wer hat sich entschuldigt? Die Liste rechnet Minuten und Unterrichtseinheiten und wird am Ende abgeschlossen.',
         'mo-1832-liste', ['Anwesenheit'], 'Für die Teilnehmenden', 'Die Bescheinigung stimmt – weil die Liste stimmt.',
         'Nach dem Abschluss sind Änderungen Nacherfassungen mit Grund.'),
    ]),
    ('Dienstag, 24. November', 'BAMF, Lehrfreigabe und ein Krankheitsfall',
     'Integrationskurse brauchen Nachweise, ein Sprachkurs eine Lehrfreigabe – und Herr Daubner wird krank.', [
        ('8:30', 'Verwaltungsleitung · Sonja Eickelberg', 'Die erste Nacht',
         'Die Automatik ist durchgelaufen: Erinnerungen vor Kursbeginn, Aufgaben, Rechnungen. Alles steht auf der Startseite.',
         'di-0830-start', ['Startseite', 'Automatik'], 'Für die Verwaltung', 'Routine läuft von selbst – und ist trotzdem dokumentiert.',
         'Im Betrieb laufen die Routinen nachts, stündlich und morgens; im Testsystem nach Spielzeit.'),
        ('8:40', 'Integrationskurs-Team · Sylvia Bergmann', 'Kursbeginn melden? Noch nicht.',
         '„Kursbeginn an das BAMF melden“ ist gesperrt. Der Hinweis nennt die Personen, deren Ausweisprüfung fehlt.',
         'di-0840-ik', ['Integrationskurse', 'BAMF'], 'Für das Team', 'Gesperrte Knöpfe sagen, was fehlt – und wo.',
         'Ausweise werden im Original geprüft, nie kopiert; Lichtbilder nur für den Sichtvergleich, mit Löschfrist.'),
        ('10:30', 'Fachbereichsleitung · Dr. Heike Steinkamp', 'Spanisch – aber nur Italienisch freigegeben',
         'Nathanael Roloff soll ab Mittwoch Spanisch unterrichten, hat aber nur die Lehrfreigabe für Italienisch. VibeVHS zeigt es am Kurs.',
         'di-1030-lehrfreigabe', ['Kurse', 'Qualifikationen'], 'Für die Fachbereiche', 'Qualität wird geprüft, bevor die erste Stunde beginnt.',
         'Ab dem ersten betroffenen Termin steht „nicht geeignet“ am Kurs; die Aufgabe dazu war schon an die Leitung eskaliert.'),
        ('10:32', 'Fachbereichsleitung', 'Eine Ausnahme – begründet',
         'Frau Steinkamp weiß: Herr Roloff spricht fließend Spanisch, die Eignungsprüfung steht an. „Ausnahme erteilen“ wird erst klickbar, wenn eine Begründung dasteht.',
         'di-1032-ausnahme', ['Kurse', 'Ausnahmen'], 'Für die Fachbereiche', 'Menschen entscheiden – das System sorgt dafür, dass es dokumentiert ist.',
         'Ausnahmen darf nur die Fachbereichsleitung erteilen; der Grund steht im Protokoll und in der Datenhistorie.'),
        ('10:33', 'Fachbereichsleitung', 'Ausnahme erteilt',
         'Der Kurs kann am Mittwoch stattfinden. Die eskalierte Aufgabe schließt sich von selbst.',
         'di-1033-ausnahme-erteilt', ['Kurse', 'Aufgaben'], 'Für die Teilnehmenden', 'Der Spanischkurs fällt nicht aus.', ''),
        ('11:30', 'Kursleitung · Ruben Daubner', 'Ausfall melden vom Handy',
         'Herr Daubner ist krank. Im Dozierendenportal meldet er den Ausfall für Mittwoch – mit Grund.',
         'di-1130-ausfall-app', ['Dozierendenportal'], 'Für die Kursleitungen', 'Kein Anruf auf dem Anrufbeantworter um 7 Uhr.',
         'Das Portal ist eine Web-App ohne App-Store; Anmeldung per Link.'),
        ('11:31', 'Kursleitung', 'Gemeldet',
         'Die Meldung geht als Anliegen an die Verwaltung. Herr Daubner sieht den Stand.',
         'di-1131-ausfall-gemeldet', ['Dozierendenportal'], 'Für die Kursleitungen', 'Gemeldet heißt angekommen.',
         ''),
        ('11:40', 'Verwaltungsleitung · Sonja Eickelberg', 'Anliegen der Kursleitungen',
         'Ausfälle, Bankverbindungen, Nachweise: Was Kursleitungen im Portal melden, landet hier – und als Aufgabe im Korb.',
         'di-1140-anliegen', ['Anliegen'], 'Für die Verwaltung', 'Eine Stelle für alles, was Kursleitungen melden.', ''),
        ('11:41', 'Verwaltungsleitung', 'Der Ausfall im Detail',
         'Kurs, Termin, Grund und wer betroffen ist – auf einen Blick.',
         'di-1141-anliegen-detail', ['Anliegen', 'Termine'], 'Für die Teilnehmenden', 'Niemand steht vor verschlossener Tür.', ''),
        ('11:42', 'Verwaltungsleitung', 'Zur Kenntnis genommen',
         'Die Entscheidung ist dokumentiert; Herr Daubner sieht sie im Portal.',
         'di-1142-kenntnis', ['Anliegen', 'Protokoll'], 'Für die Kursleitungen', 'Rückmeldung ohne Telefonkette.', ''),
    ]),
    ('Mittwoch, 25. November', 'Geld, das ankommt – und Geld, das fehlt',
     'Linda Lütke bringt die Finanzen in Ordnung: Kontoauszug, Mahnungen, Honorare. Abends bucht eine Teilnehmerin selbst.', [
        ('8:30', 'Buchhaltung · Linda Lütke', 'Was ist zu tun?',
         'Offene Forderungen, Überfälliges, Honorare, Belege – und eine Liste der nächsten Schritte.',
         'mi-0830-finanzen', ['Finanzen'], 'Für die Buchhaltung', 'Der Überblick, bevor die erste Excel-Datei geöffnet ist.',
         'Belege werden nach GoBD festgeschrieben: lückenlos nummeriert, Storno statt Änderung.'),
        ('8:40', 'Buchhaltung', 'Der Kontoauszug ordnet sich fast selbst',
         'Eindeutige Zahlungen sind schon gebucht. Bei zweien passt der Betrag nicht genau – VibeVHS macht einen Vorschlag.',
         'mi-0840-kontoauszug', ['Kontoauszüge', 'camt.053'], 'Für die Buchhaltung', 'Nur die unklaren Fälle brauchen einen Menschen.',
         'Doppelt eingelesene Auszüge werden erkannt (Prüfsumme und Auszugs-ID).'),
        ('8:45', 'Buchhaltung', 'Vorschlag gebucht',
         'Ein Klick bestätigt den Vorschlag. Rücklastschriften erscheinen mit Grund und lösen eine Aufgabe aus.',
         'mi-0845-gebucht', ['Kontoauszüge'], 'Für die Buchhaltung', 'Weniger Abgleich, mehr Überblick.', ''),
        ('9:00', 'Buchhaltung', 'Mahnungen mit Augenmaß',
         'Der Mahnvorschlag nennt Rechnung, Stufe und Gebühr. Einzelne Posten lassen sich ausschließen; Rechnungen im laufenden Lastschrifteinzug werden nie gemahnt.',
         'mi-0900-mahnung', ['Mahnwesen'], 'Für die Teilnehmenden', 'Erst eine freundliche Erinnerung, dann die Mahnung.',
         ''),
        ('9:01', 'Buchhaltung', 'Freigegeben',
         'Die Zahlungserinnerungen gehen mit Übersetzungslink in den Postausgang.',
         'mi-0901-gemahnt', ['Mahnwesen', 'Postausgang'], 'Für die Buchhaltung', 'Vorschlag von der Automatik, Entscheidung vom Menschen.', ''),
        ('9:30', 'Buchhaltung', 'Honorare',
         'Aus den stattgefundenen Terminen entstehen Honorarentwürfe je Kursleitung – nach dem Honorarmodell des Vertrags.',
         'mi-0930-honorar', ['Honorare'], 'Für die Kursleitungen', 'Pünktlich und nachvollziehbar abgerechnet.',
         'Bereits abgerechnete Termine werden nie doppelt berechnet.'),
        ('17:30', 'Teilnehmerin · Olena Kovalenko', 'Meine vhs auf dem Handy',
         'Olena Kovalenko sieht ihre nächsten Termine, checkt per Link ein und hat ihren Ausweis immer dabei.',
         'mi-1730-portal', ['Teilnehmendenportal'], 'Für die Teilnehmenden', 'Alles zur vhs an einem Ort – ohne Konto-Passwort.',
         ''),
        ('17:35', 'Teilnehmerin', 'Selbst buchen',
         'Mit Kursnummer oder Suche bucht sie selbst – auch für ihren Sohn Taras, für den sie eine Vollmacht hat.',
         'mi-1735-buchen', ['Teilnehmendenportal', 'Vollmachten'], 'Für die Teilnehmenden', 'Weniger Schlange am Tresen.', ''),
    ]),
    ('Donnerstag, 26. November', 'Planen, bescheinigen, schützen',
     'Das Frühjahrsprogramm wächst, eine Teilnehmerin braucht eine Bescheinigung, und der Datenschutz räumt auf.', [
        ('9:30', 'Fachbereichsleitung · Dr. Heike Steinkamp', 'Ein Kurs für das Frühjahr',
         'Spanisch A2 soll weitergehen. Die Kursart verlangt eine Lehrfreigabe für Spanisch: Herr Roloff erscheint mit Warnzeichen. Termine, Ferien und Räume rechnet die Vorschau, sobald eine geeignete Kursleitung gewählt ist.',
         'do-0900-kurs-neu', ['Kurs anlegen', 'Qualifikationen'], 'Für die Fachbereiche', 'Planen mit Leitplanken statt mit Bauchgefühl.',
         'Die Vorschau zeigt jeden Termin, ausgelassene Ferien und Feiertage und Raumkonflikte, bevor gespeichert wird.'),
        ('11:30', 'Teilnehmerservice · Celine Wilkes', 'Eine Bescheinigung, bitte',
         'Olena Kovalenko braucht ihre Teilnahmebescheinigung für das Jobcenter. Der Nachdruck ist das archivierte Original – mit QR-Code „Translate this letter in your language“.',
         'do-1100-person', ['Personen', 'Bescheinigungen'], 'Für die Teilnehmenden', 'In zwei Minuten am Tresen ausgedruckt.',
         'Jedes Dokument liegt mit Prüfsumme (SHA-256) im Archiv.'),
        ('14:30', 'Stellv. Leitung · Gisela Junkerholt', 'Datenschutz mit vier Augen',
         'Wer lange nicht mehr da war, wird nach Frist anonymisiert. Der Löschlauf braucht zwei Freigaben.',
         'do-1400-datenschutz', ['Datenschutz'], 'Für alle', 'DSGVO nicht als Projekt, sondern als Routine.',
         'Exporte und Zugriffe auf gesperrte Personen werden protokolliert.'),
    ]),
    ('Freitag, 27. November', 'Rückblick und Ausblick',
     'Die Leitung schaut auf die Zahlen, die IT auf den Betrieb – und das Protokoll erzählt die ganze Woche.', [
        ('8:30', 'Leitung · Dr. Kai Lüdeking', 'Wie war die Woche?',
         'Die Körbe sind leerer als am Montag. Was noch offen ist, steht oben.',
         'fr-0800-leitung', ['Startseite', 'Aufgaben'], 'Für die Leitung', 'Führen mit Überblick statt mit Rundmails.', ''),
        ('8:45', 'Leitung', 'Das Dashboard',
         'Anmeldungen, Auslastung, Umsatz – gerechnet aus den echten Daten, jederzeit aktuell.',
         'fr-0900-dashboard', ['Dashboard'], 'Für die Leitung', 'Zahlen, wenn man sie braucht – nicht erst zum Jahresbericht.', ''),
        ('9:00', 'Leitung', 'Auswertungen',
         'Kursliste, Auslastung, Statistik für den Landesverband. Jeder Export wird protokolliert.',
         'fr-1000-auswertung', ['Auswertungen', 'Statistik'], 'Für die Leitung', 'Die Volkshochschulstatistik auf Knopfdruck.', ''),
        ('11:30', 'IT · Amy Behrens', 'Der Betrieb',
         'Sicherung, Schnittstellen, Zeitpläne der Automatik: alles auf einer Seite.',
         'fr-1100-betrieb', ['Betrieb'], 'Für die IT', 'Selbst betrieben, im kommunalen Rechenzentrum.', ''),
        ('13:30', 'Fachbereichsleitung · Varvara Schatte', 'Die nächste Woche kündigt sich an',
         'Mindestteilnehmerzahlen, Kursbeginne, ablaufende Nachweise: Der Korb füllt sich für Montag – rechtzeitig.',
         'fr-1300-aufgaben', ['Aufgaben'], 'Für die Verwaltung', 'Vorausschauend statt hinterher.', ''),
        ('14:00', 'Verwaltungsleitung · Sonja Eickelberg', 'Das Protokoll der Woche',
         'Jeder Schritt dieser Woche – von Menschen und von der Automatik – steht im Protokoll, ohne Personendaten im Klartext.',
         'fr-1400-protokoll', ['Protokoll'], 'Für alle', 'Nachvollziehbar heißt: man muss sich nicht erinnern.', ''),
        ('15:00', 'Alle', 'Hilfe und geführte Touren',
         'Wer etwas Neues lernt, startet eine geführte Tour an der echten Oberfläche.',
         'fr-1500-hilfe', ['Hilfe', 'Touren'], 'Für alle', 'Sicher ausprobieren, bevor es ernst wird.', ''),
    ]),
]


def bauen():
    vorhanden = {p.stem for p in (ORDNER / 'bilder').glob('*.jpg')}
    folien = [{
        'id': 'titel', 'hintergrund': 'hell', 'logo': 'bilder/vibevhs-logo-hell.png', 'logo2': 'bilder/logo-vhs-egs.png', 'eyebrow': 'VibeVHS im Alltag',
        'h1': 'Eine turbulente Woche an der vhs',
        'lead': 'Fünf Tage am Tresen, im Büro und unterwegs – mit Volkshochschule Emsdetten Greven Saerbeck als Beispiel.',
        'fuss': 'Echte Bildschirmfotos der laufenden Anwendung · Teilnehmende und Kursleitungen sind erfunden', 'fuss_rechts': '[Datum · Vortragende/r]',
        'notiz': 'Wir begleiten das Team der vhs durch eine Woche. Alle Bilder sind echte Bildschirmfotos aus einem Testsystem. '
                 'Die Mitarbeitenden tragen verfremdete Namen, alle Teilnehmenden, Kursleitungen und Daten sind erfunden. '
                 'Jedes Kapitel ist ein Tag – man kann Tage überspringen.'}]
    folien.append({'id': 'figuren', 'hintergrund': 'hell', 'eyebrow': 'Wer mitspielt', 'h2': 'Das Team dieser Woche', 'fuss': FUSS,
                   'karten': [{'marke': 'Leitung', 'titel': 'Dr. Kai Lüdeking, Gisela Junkerholt', 'text': 'Leitung und stellvertretende Leitung'},
                              {'marke': 'Fachbereiche', 'titel': 'Varvara Schatte, Dr. Heike Steinkamp', 'text': 'Programmplanung, Kursleitungen, Qualität'},
                              {'marke': 'Verwaltung', 'titel': 'Sonja Eickelberg, Katja Friedsen, Celine Wilkes, Linda Lütke', 'text': 'Verwaltungsleitung, Teilnehmerservice, Buchhaltung'},
                              {'marke': 'Integration und IT', 'titel': 'Sylvia Bergmann, Amy Behrens', 'text': 'Integrationskurs-Team, IT und Digitale vhs'}],
                   'lead': 'Dazu erfundene Teilnehmende (u. a. Olena Kovalenko, Nadia Haddad, Sabine Weber) und Kursleitungen (u. a. Ruben Daubner).',
                   'notiz': 'Die Rollen entsprechen den Zuständigkeiten auf der Teamseite; jede Rolle sieht in VibeVHS ihre eigenen Körbe und Rechte.'})
    drehbuch = ['# Drehbuch „Eine turbulente Woche an der vhs“', '',
                'Echte Bildschirmfotos der laufenden Anwendung im Schulungsbetrieb (Spielzeit Mo 23.–Fr 27.11.2026, Demodaten der vhs EGS).',
                'Die Ereignisse entstehen durch Klicks in der echten Oberfläche (`aufnahmen.json`, `tools/praesentation/app-bilder.mjs`);',
                'Folien: `szenen.py` → `folien.json` → `py -3 tools/praesentation/praesentation.py woche`. Ablauf siehe `README.md`.', '']
    fehlend = []
    for tag, titel, lead, szenen in TAGE:
        szenen = [s for s in szenen if s[4] in vorhanden or fehlend.append(s[4])]
        folien.append({'id': 'kapitel-' + tag.split(',')[0].lower(), 'hintergrund': 'tief', 'eyebrow': tag, 'kapitel': titel, 'lead': lead,
                       'szenen': [s[2] for s in szenen], 'fuss': FUSS,
                       'notiz': f'Kapitel {tag}: {lead} Zum Überspringen bis zum nächsten Kapiteltrenner weiterklicken.'})
        drehbuch += [f'## {tag}: {titel}', '', lead, '', '| Zeit | Wer | Szene | Bereiche | Bild |', '| --- | --- | --- | --- | --- |']
        for zeit, wer, h2, text, bild, bereiche, marke, satz, notiz in szenen:
            folien.append({'id': bild, 'hintergrund': 'hell', 'eyebrow': f'{tag.split(",")[0]} {zeit} · {wer}', 'h2': h2, 'text': text,
                           'bild': f'bilder/{bild}.jpg', 'bereiche': bereiche, 'gewinn': {'marke': marke, 'satz': satz}, 'fuss': FUSS, 'notiz': notiz})
            drehbuch.append(f'| {zeit} | {wer} | {h2} | {", ".join(bereiche)} | `{bild}.jpg` |')
        drehbuch.append('')
    folien.append({'id': 'schluss', 'hintergrund': 'tief', 'eyebrow': 'Eine Woche, ein System', 'fuss': FUSS,
                   'aussage': [{'text': 'Egal, was los ist: VibeVHS nimmt der Verwaltung die Routine ab. '}, {'text': 'Damit Zeit für die Menschen bleibt.', 'gelb': True}],
                   'pillen': ['Routinen laufen von selbst, mit Protokoll', 'Gesperrtes erklärt sich', 'Nach jedem Schritt der nächste',
                              'In jeder Sprache verständlich', 'Selbst betrieben, DSGVO-konform'],
                   'notiz': 'Zusammenfassung: Jedes Ereignis der Woche hat das nächste ausgelöst – ohne doppelte Eingaben.'})
    (ORDNER / 'folien.json').write_text(json.dumps({'titel': 'Eine turbulente Woche an der vhs', 'folien': folien}, ensure_ascii=False, indent=1),
                                        encoding='utf-8')
    (ORDNER / 'drehbuch.md').write_text('\n'.join(drehbuch), encoding='utf-8')
    print(f'{len(folien)} Folien' + (f'; ohne Bild ausgelassen: {", ".join(fehlend)}' if fehlend else ''))


if __name__ == '__main__':
    bauen()
