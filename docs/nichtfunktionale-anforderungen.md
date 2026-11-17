# Nichtfunktionale Anforderungen – Nachweise im Repository

**Die Anforderungen selbst (Text, Priorität, Nummer) stehen nur in [`spezifikation/anforderungen.md`](spezifikation/anforderungen.md),
Abschnitt „Nichtfunktionale Anforderungen“.** Diese Datei nennt zu jeder Nummer nur, wo und wie sie umgesetzt und geprüft ist – keine
Anforderungstexte, keine eigene Nummerierung. Neue NFA zuerst dort anlegen, dann hier den Nachweis eintragen.

## Zeit: Speicherung und Darstellung

| Nr. | Umsetzung / Nachweis |
|---|---|
| NFA-14 | Globale Konverter `ZeitpunktAlsMillisekunden` und `TerminAlsUtcMillisekunden` (`Infrastructure/Datenbank/Speicherformate.cs`); Test `ZeitumstellungTests` prüft die gespeicherten UTC-Werte. |
| NFA-15 | Konverter `TagAlsZahl`. Offenes Ende: Konverter `OffenesEnde` (`GueltigBis` aller Versionen; Sichten `v_…` zeigen „offen“). |
| NFA-16 | `Domain.Gemeinsam.Zeitzone` (`Festlegen` beim Start, `NachUtc`/`AusUtc`/`Ortszeit`), von den Konvertern genutzt; „je Mandant“ vermerkt in `docs/mandanten.md`, Punkt 7. |
| NFA-17 | `Zeitzone.NachUtc` (Lücke 02:00–03:00 zählt nach der Umstellung); `ZeitumstellungTests`: Dienstage um den letzten Sonntag im März und Oktober 2026. |
| NFA-18 | `docs/betrieb.md`, Abschnitt „Zeitzone“ (`timedatectl`). |
| NFA-19 | `Schema.Sichten`: zu jeder Tabelle `v_<Tabelle>` (Ids als GUID-Text, Tage als JJJJ-MM-TT, Zeitpunkte als UTC-Text, bei Versionen Änderungsvorgang), beim Anlegen der Datenbank erzeugt; in jedem Test angelegt. |

## Datenhaltung

| Nr. | Umsetzung / Nachweis |
|---|---|
| NFA-20 | Hash-Bildung in `Finanzprotokoll` und `Beleg` über fachliche Werte (Zeitpunkt als Millisekunden, nicht die Speicherform). Nach der Umstellung der Speicherformate war die Kette auf einer Kopie der Testdatenbank vollständig gültig (974/974). |
| NFA-21 | `ArchitekturTests` A/B/C (`tests/VibeVHS.Infrastructure.Tests`) verhindern Kopien und JSON-Spalten; Einstufung jeder Stammdatenspalte in `Infrastructure/Datenbank/Spalteneinstufung.cs`; Ausnahmen (Belege, Mahn- und Zahlungsposten, Postausgang, Anonymisierungsreste) in [`datenmodell.md`](datenmodell.md). |
| NFA-22 | Tabelle `Aenderungsvorgaenge`; `BitemporaleVersion` verweist über `ErfasstDurch`/`ErsetztDurch` darauf (`Domain`, Abbildung in `VibeVhsDbContext.Version<T>`). |
| NFA-23 | Konverter `IdAlsBytes` (16 Byte, big-endian) und `Ids.Neu` (monoton steigende UUIDv7). |
| NFA-24 | Collation `DIN5007_2` (registriert in `SqliteErweiterungen`), Index `IX_PersonNamen_Sortierung`; Messung in [`messungen.md`](messungen.md). |
| NFA-25 | Entscheidung „Eigene Datenbank je VHS“ in [`mandanten.md`](mandanten.md); Umsetzung der Mandantenauflösung dort als Plan beschrieben. |
