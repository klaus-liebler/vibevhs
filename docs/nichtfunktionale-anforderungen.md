# Nichtfunktionale Anforderungen – Nachweise im Repository

Die Anforderungen selbst stehen im Anforderungsdokument (claude.ai-Artefakt „Anforderungen“); hier steht zu jeder Nummer, wo und wie sie
umgesetzt und geprüft ist. Keine eigene Nummerierung.

## Zeit: Speicherung und Darstellung

| Nr. | Anforderung (Kurzform) | Umsetzung / Nachweis |
|---|---|---|
| NFA-14 | Zeitpunkte und Termine (Datum mit Uhrzeit) als UTC, int64 Epoch-Millisekunden; keine Ortszeit und kein Datum als Text in der Datenbank. | Globale Konverter `ZeitpunktAlsMillisekunden` und `TerminAlsUtcMillisekunden` (`Infrastructure/Datenbank/Speicherformate.cs`); Test `ZeitumstellungTests` prüft die gespeicherten UTC-Werte. |
| NFA-15 | Kalendertage ohne Uhrzeit (Gültigkeit von/bis, Geburtsdatum, Fälligkeit) als Ganzzahl JJJJMMTT, offenes Ende 99991231. | Konverter `TagAlsZahl`. Offenes Ende: Konverter `OffenesEnde` (`GueltigBis` aller Versionen; Sichten `v_…` zeigen „offen“). |
| NFA-16 | Einstellbare Anzeige-/Eingabezeitzone (Einstellung „Zeitzone“), sonst die Systemzeitzone; später je Mandant; alle Umrechnungen zentral. | `Domain.Gemeinsam.Zeitzone` (`Festlegen` beim Start, `NachUtc`/`AusUtc`/`Ortszeit`), von den Konvertern genutzt; „je Mandant“ vermerkt in `docs/mandanten.md`, Punkt 7. |
| NFA-17 | Serientermine je Termin aus der lokalen Regel nach UTC umgerechnet (bleiben nach der Zeitumstellung zur selben Ortszeit); Test über die Zeitumstellung. | `Zeitzone.NachUtc` (Lücke 02:00–03:00 zählt nach der Umstellung); `ZeitumstellungTests`: Dienstage um den letzten Sonntag im März und Oktober 2026. |
| NFA-18 | Systemzeitzone des Servers gesetzt (bzw. Einstellung „Zeitzone“). | `docs/betrieb.md`, Abschnitt „Zeitzone“ (`timedatectl`). |
| NFA-19 | Lesbare Sichten zum Prüfen von Hand. | `Schema.Sichten`: zu jeder Tabelle `v_<Tabelle>` (Ids als GUID-Text, Tage als JJJJ-MM-TT, Zeitpunkte als UTC-Text, bei Versionen Änderungsvorgang), beim Anlegen der Datenbank erzeugt; in jedem Test angelegt. |

Vorschlag für eine weitere Anforderung (noch ohne Nummer): Prüfsummen (Finanzprotokoll, Belege, Dokumente) werden über fachliche Werte
gebildet, nicht über die Speicherform; ein Wechsel der Speicherform darf sie nicht ändern. Nachweis: Hash-Bildung in `Finanzprotokoll`/`Beleg`
(Zeitpunkt als Millisekunden); nach der Umstellung der Speicherformate war die Kette auf einer Kopie der Testdatenbank vollständig gültig (974/974).
