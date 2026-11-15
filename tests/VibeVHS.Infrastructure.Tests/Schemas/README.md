# Offizielle Schemata für die Validierungstests

Abgerufen am 15.11.2026 mit Freigabe von Klaus. Die Dateien werden nur in den Tests verwendet
(`SchemaValidierungTests`), nicht in der Anwendung.

| Datei | Quelle | Version | Lizenz/Hinweis |
|---|---|---|---|
| `iso20022/pain.008.001.08.xsd` | <https://www.iso20022.org/sites/default/files/documents/messages/pain/schemas/pain.008.001.08.xsd> | ISO 20022, Message Definition Report 2019 (Standards Editor R1.6.15, 14.02.2019) | ISO 20022 Message Schemas: frei nutzbar laut ISO-20022-Nutzungsbedingungen (iso20022.org) |
| `iso20022/pain.001.001.09.xsd` | <https://www.iso20022.org/sites/default/files/documents/messages/pain/schemas/pain.001.001.09.xsd> | ISO 20022, 2019 | wie oben |
| `ubl-2.1/maindoc/UBL-Invoice-2.1.xsd` und `ubl-2.1/common/*.xsd` (14 Dateien) | <http://docs.oasis-open.org/ubl/os-UBL-2.1/UBL-2.1.zip> (Ordner `xsd/`) | OASIS UBL 2.1, OASIS Standard 04.11.2013 | © OASIS Open 2013, OASIS IPR Policy; unverändert weitergegeben mit Copyright-Hinweis in jeder Datei |

## Was die Tests prüfen – und was nicht

- **Prüfen:** Erzeugte `pain.008`- und `pain.001`-Dateien gegen das ISO-20022-Schema; erzeugte XRechnungen gegen das
  UBL-2.1-Invoice-Schema (Syntax der XRechnung).
- **Nicht geprüft:** Die Einschränkungen der Deutschen Kreditwirtschaft (DK, Anlage 3 der EBICS-Spezifikation, „GBIC“)
  und des EPC (SEPA-Zeichensatz, Längen) sind strenger als ISO 20022. Die DK-Schemata gibt es als ZIP unter
  <https://www.ebics.de/de/datenformate>; sie sind hier nicht abgelegt. Zeichensatz und Längen sichert `SepaXml.Text` ab.
- **KoSIT-Schematron** (Geschäftsregeln EN 16931 und XRechnung-CIUS, <https://github.com/itplr-kosit/xrechnung-schematron>)
  wird nicht ausgeführt, weil der KoSIT-Validator Java braucht. Empfehlung für später: Validator-Konfiguration
  <https://github.com/itplr-kosit/validator-configuration-xrechnung> in einer CI-Stufe mit Java laufen lassen.
