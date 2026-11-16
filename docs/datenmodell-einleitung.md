## Bitemporale Datenhaltung

Alle Personenangaben (Namen, Merkmale wie Titel, Rollen, Sprache, Geburtsdatum, Anrede, Adressen, Adresskennzeichen, Rufnummern,
E-Mail-Adressen, Organisationen, Vollmachten, Einwilligungen, Qualifikationen) und weitere Stammdaten (Entgelte, Ortsmerkmale) werden
**bitemporal** („zwei Zeitachsen“) gespeichert:

| Achse | Spalten | Bedeutung | Beispiel |
|---|---|---|---|
| **Gültigkeitszeit** (valid time) | `GueltigVon`, `GueltigBis` | Wann galt die Angabe **in der Welt**? Halboffen: `GueltigVon` einschließlich, `GueltigBis` ausschließlich (`NULL` = offen). | „wohnte bis 31.07. in der Lindenstraße“ |
| **Wissenszeit** (transaction/system time) | `ErfasstAm`, `ErsetztAm` | Wann **wussten wir** es? Ab `ErfasstAm`, bis `ErsetztAm` (`NULL` = aktueller Wissensstand). | „das wussten wir vom 15.03. bis zum 20.09.“ |

**Versionen werden nie geändert.** Jede Änderung – eine Änderung ab Datum (z. B. ein gemeldeter Umzug), eine Korrektur, das Beenden –
erzeugt neue Zeilen und markiert die bisherige als ersetzt (`ErsetztAm`). `ErsetztAm` heißt also nicht „war falsch“, sondern „ab da
wussten wir es anders bzw. genauer“. Nur so bleibt beantwortbar, was das System zu einem früheren Zeitpunkt wusste (z. B. warum ein Brief
an die alte Adresse ging).

**Änderungsvorgang:** Zeitpunkt, Person und Grund stehen nicht in jeder Zeile, sondern einmal je Aktion in der Tabelle
`Aenderungsvorgaenge`; die Zeilen verweisen mit `ErfasstDurchVorgangId` (angelegt durch) und `ErsetztDurchVorgangId` (ersetzt durch)
darauf. `ErfasstAm` ist der Zeitpunkt des anlegenden, `ErsetztAm` der des ersetzenden Vorgangs. `Erfassungsart` (Ersterfassung, Änderung ab
Datum, Korrektur, Abgrenzung, Beendet) beschreibt die Rolle der Zeile im Vorgang. Die Sichten `v_…` zeigen alles wieder zusammen.

**Stabile Identität vs. Version:** Die Spalte `Id` ist die Zeilen-Id einer *Version*. Gegenstände, von denen eine Person mehrere haben kann,
tragen zusätzlich eine stabile Identität, die über alle Versionen gleich bleibt: `AdressId` (eine Adresse), `NummerId` (eine Rufnummer) usw.
Darauf verweisen andere Versionen, z. B. das Kennzeichen „üblicher Aufenthaltsort“ (`PersonAdressKennzeichen.AdressId`).
`AdressId` ist also **kein Adresstyp** – der Typ ergibt sich aus den Kennzeichen.

### Beispiel: Umzug mit nachträglicher Korrektur

- Frau Muster wohnt seit 01.01.2020 in der Lindenstraße 5 (bei uns erfasst am 10.01.2020).
- Am 15.03.2026 meldet sie: „Ab 01.08. wohne ich im Bergweg 2.“ (Vorgang „Umzug gemeldet“)
- Am 10.07. geht die Kursbestätigung raus – in die Lindenstraße.
- Am 20.09. legt sie die Meldebescheinigung vor: Der Umzug war schon am 01.07. (Vorgang „Meldebescheinigung“, Funktion
  „Umzugsdatum korrigieren“)

| Zeile | Adresse | gültig von | gültig bis (einschl.) | erfasst (Vorgang) | ersetzt (Vorgang) | Erfassungsart |
|---|---|---|---|---|---|---|
| A | Lindenstr. 5 | 01.01.2020 | offen | 10.01.2020 (Ersterfassung) | 15.03.2026 (Umzug gemeldet) | Ersterfassung |
| B | Lindenstr. 5 | 01.01.2020 | 31.07.2026 | 15.03.2026 (Umzug gemeldet) | 20.09.2026 (Meldebescheinigung) | Abgrenzung |
| C | Bergweg 2 | 01.08.2026 | offen | 15.03.2026 (Umzug gemeldet) | 20.09.2026 (Meldebescheinigung) | Änderung ab Datum |
| D | Lindenstr. 5 | 01.01.2020 | 30.06.2026 | 20.09.2026 (Meldebescheinigung) | – | Korrektur |
| E | Bergweg 2 | 01.07.2026 | offen | 20.09.2026 (Meldebescheinigung) | – | Korrektur |

(In der Datenbank steht `GueltigBis` ausschließlich, also bei B `20260801`, bei D `20260701`.)

So liest man es:
- B sagt **„wohnte bis 31.07. in der Lindenstraße“** – das war unser Wissen vom 15.03. bis 20.09. (`ErfasstAm` bis `ErsetztAm`).
- D sagt **„wohnte bis 30.06. in der Lindenstraße“** – das ist unser Wissen seit 20.09. (`ErsetztAm` leer = aktueller Stand).

Zwei typische Fragen:
- **„Wo wohnte sie am 10.07.?“** (heutiger Wissensstand) → nur nicht ersetzte Zeilen D, E → **Bergweg 2**.
- **„Wohin hätten wir am 10.07. geschrieben?“** (Wissensstand 10.07.) → Zeilen, die am 10.07. bekannt und nicht ersetzt waren: B, C →
  **Lindenstraße 5**. Der Brief ging also zu Recht dorthin, obwohl wir heute wissen, dass sie schon umgezogen war.

Beide Fragen beantwortet dieselbe Abfrage mit zwei Parametern – Stichtag `:tag` und Wissensstand `:bekannt`:

```sql
SELECT a.*
FROM PersonAdressen a
JOIN Aenderungsvorgaenge e ON e.Id = a.ErfasstDurchVorgangId
LEFT JOIN Aenderungsvorgaenge r ON r.Id = a.ErsetztDurchVorgangId
WHERE a.PersonId = :person
  AND a.GueltigVon <= :tag AND (a.GueltigBis IS NULL OR a.GueltigBis > :tag)   -- galt am Stichtag
  AND e.Am <= :bekannt AND (r.Id IS NULL OR r.Am > :bekannt);                 -- war zu diesem Zeitpunkt bekannt
-- „Wo wohnte sie am 10.07.?“                 :tag = 20260710, :bekannt = jetzt (UTC-Millisekunden)
-- „Wohin hätten wir am 10.07. geschrieben?“  :tag = 20260710, :bekannt = 10.07.2026 24:00 Ortszeit als UTC-Millisekunden
```

Für den aktuellen Wissensstand genügt `a.ErsetztDurchVorgangId IS NULL`. Im Code übernimmt das `ZeitlicheHistorie.AlsStichtag(gueltigAm,
bekanntAm)`; `Person.StandAm(tag, bekannt)` liefert den gesamten Stand. Speicherformate: Tage als Ganzzahl JJJJMMTT, Zeitpunkte als
UTC-Millisekunden seit 1970, Ids als 16 Byte (UUIDv7, Big Endian); zum Lesen von Hand dienen die Sichten `v_…` (z. B. `v_PersonAdressen`).

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
