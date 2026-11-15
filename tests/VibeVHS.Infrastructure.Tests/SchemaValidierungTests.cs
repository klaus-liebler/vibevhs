using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using VibeVHS.Application.Finanzen;
using VibeVHS.Domain.Finanzen;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Erzeugte SEPA-Dateien und XRechnungen gegen die offiziellen XSD-Schemata (siehe Schemas/README.md).</summary>
public class SchemaValidierungTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(1));

    private static string Pfad(params string[] teile) => Path.Combine([AppContext.BaseDirectory, "Schemas", .. teile]);

    private static List<string> Validieren(string xml, params string[] schemas)
    {
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        var einstellungen = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() };
        foreach (var s in schemas)
        {
            using var r = XmlReader.Create(s, einstellungen);
            set.Add(null, r);
        }

        set.Compile();
        var fehler = new List<string>();
        var doc = XDocument.Parse(xml);
        if (set.GlobalElements[new XmlQualifiedName(doc.Root!.Name.LocalName, doc.Root.Name.NamespaceName)] is null)
        {
            // Sonst validiert XDocument.Validate ein unbekanntes Wurzelelement stillschweigend nur „lax“.
            fehler.Add($"Wurzelelement {doc.Root.Name} ist im Schema nicht deklariert.");
        }

        doc.Validate(set, (_, e) => fehler.Add($"{e.Severity}: {e.Message}"));
        return fehler;
    }

    private static Finanzeinstellungen Einstellungen() => new("Volkshochschule Emsdetten Greven Saerbeck", "Bildungsweg 1\n48282 Emsdetten",
        Iban.AusBlzUndKonto("40351060", "72000011"), "WELADED1STF", "DE98ZZZ09999999999", "311/5970/1234");

    private static Zahlungslauf Lauf(Zahlungslaufart art, bool mitBic)
    {
        Zahlungsposten P(int i, string seq) => new(Guid.NewGuid(), $"RE-2026-0000{i}", Guid.NewGuid(), $"Müller-Lüdenscheidt & Söhne {i}", Iban.AusBlzUndKonto("40050150", $"12345{i}"),
            mitBic && i == 1 ? "COBADEFFXXX" : null, 9000 + i, $"RE-2026-0000{i} Kursentgelt 26H-811 Spanisch A1 – Einstieg", $"RE-2026-0000{i}-ab1{i}",
            art == Zahlungslaufart.Lastschrift ? $"VHS-2026-00000{i}" : null, art == Zahlungslaufart.Lastschrift ? new DateOnly(2026, 11, 1) : null, art == Zahlungslaufart.Lastschrift ? seq : null);
        var lauf = new Zahlungslauf(art, new DateOnly(2026, 11, 21), [P(1, "FRST"), P(2, "RCUR"), P(3, "RCUR")], Jetzt, "Test");
        lauf.Freigeben(new DateOnly(2026, 11, 21), Jetzt, "Test");
        return lauf;
    }

    [Fact]
    public void Lastschrift_pain008_ist_gueltig_nach_ISO_20022()
    {
        var xml = SepaXml.Lastschrift(Lauf(Zahlungslaufart.Lastschrift, mitBic: true), Einstellungen(), Jetzt);
        Validieren(xml, Pfad("iso20022", "pain.008.001.08.xsd")).ShouldBeEmpty();
        xml.ShouldContain("Mueller-Luedenscheidt + Soehne 1");
    }

    [Fact]
    public void Validierung_erkennt_Fehler_tatsaechlich()
    {
        var xml = SepaXml.Lastschrift(Lauf(Zahlungslaufart.Lastschrift, mitBic: true), Einstellungen(), Jetzt);
        Validieren(xml.Replace("<PmtMtd>DD</PmtMtd>", "<PmtMtd>XX</PmtMtd>", StringComparison.Ordinal), Pfad("iso20022", "pain.008.001.08.xsd")).ShouldNotBeEmpty();
        Validieren(xml, Pfad("iso20022", "pain.001.001.09.xsd")).ShouldNotBeEmpty(); // falsches Schema
    }

    [Fact]
    public void Ueberweisung_pain001_ist_gueltig_nach_ISO_20022()
    {
        var xml = SepaXml.Ueberweisung(Lauf(Zahlungslaufart.Ueberweisung, mitBic: false), Einstellungen(), Jetzt);
        Validieren(xml, Pfad("iso20022", "pain.001.001.09.xsd")).ShouldBeEmpty();
    }

    [Fact]
    public void XRechnung_ist_gueltig_nach_UBL_2_1()
    {
        var beleg = new Beleg(Belegart.Rechnung, new DateOnly(2026, 11, 15), null, Guid.NewGuid(), "Stadtwerke Emsdetten", "Moorbrückenstraße 30\n48282 Emsdetten",
            [new Belegposition("Firmenkurs 26H-404 Deutsch am Arbeitsplatz – 8 Teilnehmende", 72000, 8, 9000, kursId: Guid.NewGuid(), art: Positionsart.Kursentgelt),
             new Belegposition("Lehrmaterial", 4550)], Jetzt, "Test", new DateOnly(2026, 11, 29));
        beleg.Festschreiben(new Nummernkreis(Belegart.Rechnung, 2026), Jetzt, "Test");
        var empf = new Belegempfaenger(null, Guid.NewGuid(), "Stadtwerke Emsdetten", ["Stadtwerke Emsdetten", "Moorbrückenstraße 30", "48282 Emsdetten"],
            "rechnung@stadtwerke.example", "de", "05566-12345-67");
        var xml = XRechnung.Erzeugen(beleg, empf, Einstellungen());
        Validieren(xml, Pfad("ubl-2.1", "maindoc", "UBL-Invoice-2.1.xsd")).ShouldBeEmpty();
    }
}
