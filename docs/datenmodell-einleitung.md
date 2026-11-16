## Bitemporale Datenhaltung

Alle Personenangaben (Namen, Merkmale wie Titel, Rollen, Sprache, Geburtsdatum, Anrede, Adressen, Adresskennzeichen, Rufnummern,
E-Mail-Adressen, Organisationen, Vollmachten, Einwilligungen, Qualifikationen) und weitere Stammdaten (Entgelte, Ortsmerkmale) werden
**bitemporal** gespeichert. Es gibt zwei Zeitachsen:

| Achse | Spalten | Bedeutung |
|---|---|---|
| **Gültigkeitszeit** (valid time) | `GueltigVon`, `GueltigBis` | Wann galt die Angabe in der Welt? Intervall **halboffen**: `GueltigVon` einschließlich, `GueltigBis` ausschließlich (`NULL` = offen). |
| **Erfassungszeit** (transaction/system time) | `ErfasstAm`, `ErsetztAm` | Wann wusste das System das? Ebenfalls halboffen: ab `ErfasstAm`, bis `ErsetztAm` (`NULL` = aktueller Wissensstand). |

**Versionen werden nie geändert.** Jede Änderung – auch das Beenden einer Gültigkeit – erzeugt neue Zeilen und setzt `ErsetztAm` an der
alten Zeile. Nur so bleibt beantwortbar, was das System zu einem früheren Zeitpunkt wusste (z. B. warum ein Brief an die alte Adresse ging).

**Stabile Identität vs. Version:** Die Spalte `Id` ist die Zeilen-Id einer *Version*. Gegenstände, von denen eine Person mehrere haben kann,
tragen zusätzlich eine stabile Identität, die über alle Versionen gleich bleibt: `AdressId` (eine Adresse), `NummerId` (eine Rufnummer) usw.
Darauf verweisen andere Versionen, z. B. das Kennzeichen „üblicher Aufenthaltsort“ (`PersonAdressKennzeichen.AdressId`).
`AdressId` ist also **kein Adresstyp** – der Typ ergibt sich aus den Kennzeichen.

### Beispiel: Umzug mit nachträglicher Korrektur

Frau Muster wohnt seit 2020 in der Lindenstraße. Am 15.03.2026 meldet sie den Umzug zum 01.08. in den Bergweg. Am 10.07. geht die
Kursbestätigung raus. Am 20.09. kommt der Brief zurück, und sie sagt, sie sei schon zum 01.07. umgezogen.

| Zeile | Adresse | gültig von | gültig bis (einschl.) | erfasst | ersetzt | Erfassungsart |
|---|---|---|---|---|---|---|
| A | Lindenstr. 5 | 2020-01-01 | offen | 2020-01-10 | 2026-03-15 | Ersterfassung |
| B | Lindenstr. 5 | 2020-01-01 | 2026-07-31 | 2026-03-15 | 2026-09-20 | Ende wegen gemeldetem Umzug (Abgrenzung) |
| C | Bergweg 2 | 2026-08-01 | offen | 2026-03-15 | 2026-09-20 | Umzug ab 01.08. |
| D | Lindenstr. 5 | 2020-01-01 | 2026-06-30 | 2026-09-20 | – | Korrektur |
| E | Bergweg 2 | 2026-07-01 | offen | 2026-09-20 | – | Korrektur |

(In der Datenbank steht `GueltigBis` ausschließlich, also bei B `2026-08-01`, bei D `2026-07-01`.)

- **„Wo wohnte sie am 10.07.?“** (heutiger Wissensstand) → nur nicht ersetzte Zeilen D, E → **Bergweg**.
- **„Wohin hätten wir am 10.07. geschrieben?“** (Wissensstand 10.07.) → Zeilen, die am 10.07. bekannt und nicht ersetzt waren: B, C → **Lindenstraße**.

Beide Fragen beantwortet dieselbe Abfrage mit zwei Parametern – Stichtag `:tag` und Wissensstand `:bekannt`:

```sql
SELECT a.*
FROM PersonAdressen a
WHERE a.PersonId = :person
  AND a.GueltigVon <= :tag AND (a.GueltigBis IS NULL OR a.GueltigBis > :tag)          -- galt am Stichtag
  AND a.ErfasstAm <= :bekannt AND (a.ErsetztAm IS NULL OR a.ErsetztAm > :bekannt);   -- war zu diesem Zeitpunkt bekannt
-- „Wo wohnte sie am 10.07.?“            :tag = 20260710, :bekannt = jetzt (UTC-Millisekunden)
-- „Wohin hätten wir am 10.07. geschrieben?“ :tag = 20260710, :bekannt = 10.07.2026 24:00 Ortszeit als UTC-Millisekunden
```

Im Code übernimmt das `ZeitlicheHistorie.AlsStichtag(gueltigAm, bekanntAm)`; `Person.StandAm(tag, bekannt)` liefert den gesamten Stand.
Speicherformate: Tage (`GueltigVon`/`GueltigBis`) als Ganzzahl JJJJMMTT, Zeitpunkte (`ErfasstAm`/`ErsetztAm`) als UTC-Millisekunden seit 1970;
Ids als 16 Byte (UUIDv7, Big Endian). Die Parameter rechnet EF Core passend um; zum Lesen von Hand dienen die Sichten `v_…` (siehe unten).

### Sortierung nach DIN 5007-2 (Collation `DIN5007_2`)

Personen werden nach Nachname, dann Vorname sortiert, wie im Telefonbuch (DIN 5007-2): ä→ae, ö→oe, ü→ue, ß→ss, Akzente ohne Zeichen,
Groß-/Kleinschreibung egal; der Namensvorsatz („von“) steht in einer eigenen Spalte und zählt für die Sortierung nicht. Dafür tragen die
Spalten `PersonNamen.Nachname` und `PersonNamen.Vorname` die SQLite-Collation `DIN5007_2`; der Index `IX_PersonNamen_Sortierung`
(Nachname, Vorname) liegt auf ihr, die Personenliste blättert per Keyset (Nachname, Vorname, Id) über diesen Index (geprüft mit
`EXPLAIN QUERY PLAN`). Es gibt keine gespeicherte Sortierspalte.

Die Collation registriert die Anwendung bei jeder Verbindung (`SqliteErweiterungen`). **Werkzeuge ohne diese Collation** (z. B. „DB
Browser for SQLite“, die sqlite3-Kommandozeile) können die Datenbank lesen, aber
- nicht nach diesen Spalten sortieren oder vergleichen (`ORDER BY Nachname` → Fehler „no such collation sequence: DIN5007_2“),
- keine Namensversionen schreiben (der Index braucht die Collation) und kein `PRAGMA integrity_check` ausführen.
Zum Prüfen deshalb die lesbaren Sichten bzw. die Anwendung verwenden; Änderungen an den Daten nur über die Anwendung.
