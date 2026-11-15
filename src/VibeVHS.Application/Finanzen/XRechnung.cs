using System.Globalization;
using System.Xml.Linq;
using VibeVHS.Domain.Finanzen;

namespace VibeVHS.Application.Finanzen;

/// <summary>
/// XRechnung 3.0 im UBL-2.1-Format (EN 16931) für Firmenkunden und öffentliche Auftraggeber (FA-35).
/// Bildungsleistungen sind umsatzsteuerfrei (Steuerkategorie E mit Befreiungsgrund).
/// </summary>
public static class XRechnung
{
    private static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public const string Spezifikation = "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0";

    public static string Erzeugen(Beleg b, Belegempfaenger empfaenger, Finanzeinstellungen einst)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(empfaenger);
        ArgumentNullException.ThrowIfNull(einst);
        if (b.Nummer is null)
        {
            throw new InvalidOperationException("Eine XRechnung gibt es nur für festgeschriebene Belege.");
        }

        static string Betrag(long cent) => (cent / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        XElement Geld(XName name, long cent) => new(name, new XAttribute("currencyID", "EUR"), Betrag(cent));
        var ort = Ortsangaben(einst.Anschrift);
        var kundeOrt = Ortsangaben(string.Join('\n', empfaenger.Anschrift.Skip(1)));

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Inv + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac), new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                new XElement(Cbc + "CustomizationID", Spezifikation),
                new XElement(Cbc + "ProfileID", "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0"),
                new XElement(Cbc + "ID", b.Nummer),
                new XElement(Cbc + "IssueDate", b.Belegdatum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                b.Faellig is { } f ? new XElement(Cbc + "DueDate", f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
                new XElement(Cbc + "InvoiceTypeCode", "380"),
                new XElement(Cbc + "Note", einst.Steuerhinweis),
                new XElement(Cbc + "DocumentCurrencyCode", "EUR"),
                new XElement(Cbc + "BuyerReference", empfaenger.LeitwegId ?? b.Nummer),
                new XElement(Cac + "AccountingSupplierParty", new XElement(Cac + "Party",
                    new XElement(Cbc + "EndpointID", new XAttribute("schemeID", "EM"), "rechnung@vhs.example"),
                    new XElement(Cac + "PartyName", new XElement(Cbc + "Name", einst.Kontoinhaber)),
                    Anschrift(ort),
                    new XElement(Cac + "PartyTaxScheme",
                        new XElement(Cbc + "CompanyID", einst.UstId ?? einst.Steuernummer ?? "unbekannt"),
                        new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", einst.UstId is null ? "FC" : "VAT"))),
                    new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", einst.Kontoinhaber)),
                    new XElement(Cac + "Contact",
                        new XElement(Cbc + "Name", "Buchhaltung"),
                        new XElement(Cbc + "Telephone", "+49 0000 000000"),
                        new XElement(Cbc + "ElectronicMail", "rechnung@vhs.example")))),
                new XElement(Cac + "AccountingCustomerParty", new XElement(Cac + "Party",
                    new XElement(Cbc + "EndpointID", new XAttribute("schemeID", "EM"), empfaenger.EMail ?? "unbekannt@example.org"),
                    new XElement(Cac + "PartyName", new XElement(Cbc + "Name", empfaenger.Name)),
                    Anschrift(kundeOrt),
                    new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", empfaenger.Name)))),
                new XElement(Cac + "PaymentMeans",
                    new XElement(Cbc + "PaymentMeansCode", "58"),
                    new XElement(Cbc + "PaymentID", b.Nummer),
                    new XElement(Cac + "PayeeFinancialAccount",
                        new XElement(Cbc + "ID", einst.Iban),
                        new XElement(Cbc + "Name", einst.Kontoinhaber),
                        einst.Bic is null ? null : new XElement(Cac + "FinancialInstitutionBranch", new XElement(Cbc + "ID", einst.Bic)))),
                new XElement(Cac + "PaymentTerms", new XElement(Cbc + "Note", b.Faellig is { } z ? $"Zahlbar bis {z:dd.MM.yyyy} ohne Abzug." : "Zahlbar sofort ohne Abzug.")),
                new XElement(Cac + "TaxTotal",
                    Geld(Cbc + "TaxAmount", 0),
                    new XElement(Cac + "TaxSubtotal",
                        Geld(Cbc + "TaxableAmount", b.BetragCent),
                        Geld(Cbc + "TaxAmount", 0),
                        Steuerkategorie(einst, mitGrund: true))),
                new XElement(Cac + "LegalMonetaryTotal",
                    Geld(Cbc + "LineExtensionAmount", b.BetragCent),
                    Geld(Cbc + "TaxExclusiveAmount", b.BetragCent),
                    Geld(Cbc + "TaxInclusiveAmount", b.BetragCent),
                    Geld(Cbc + "PayableAmount", b.BetragCent)),
                b.Positionen.Select(p => new XElement(Cac + "InvoiceLine",
                    new XElement(Cbc + "ID", p.Nr.ToString(CultureInfo.InvariantCulture)),
                    new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "C62"), p.Menge.ToString("0.####", CultureInfo.InvariantCulture)),
                    Geld(Cbc + "LineExtensionAmount", p.BetragCent),
                    new XElement(Cac + "Item",
                        new XElement(Cbc + "Name", p.Text.Length > 100 ? p.Text[..100] : p.Text),
                        Steuerkategorie(einst, mitGrund: false, name: Cac + "ClassifiedTaxCategory")),
                    new XElement(Cac + "Price", Geld(Cbc + "PriceAmount", p.EinzelCent))))));
        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    private static XElement Steuerkategorie(Finanzeinstellungen einst, bool mitGrund, XName? name = null)
        => new(name ?? Cac + "TaxCategory",
            new XElement(Cbc + "ID", "E"),
            new XElement(Cbc + "Percent", "0"),
            mitGrund ? new XElement(Cbc + "TaxExemptionReason", einst.Steuerhinweis) : null,
            new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "VAT")));

    private static XElement Anschrift((string? Strasse, string Ort, string? Plz) a)
        => new(Cac + "PostalAddress",
            a.Strasse is null ? null : new XElement(Cbc + "StreetName", a.Strasse),
            new XElement(Cbc + "CityName", a.Ort),
            a.Plz is null ? null : new XElement(Cbc + "PostalZone", a.Plz),
            new XElement(Cac + "Country", new XElement(Cbc + "IdentificationCode", "DE")));

    /// <summary>Liest Straße, PLZ und Ort aus einer mehrzeiligen Anschrift („Musterstr. 1“ / „48282 Emsdetten“).</summary>
    private static (string? Strasse, string Ort, string? Plz) Ortsangaben(string anschrift)
    {
        var zeilen = anschrift.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var plzZeile = zeilen.LastOrDefault(z => z.Length > 6 && z[..5].All(char.IsDigit) && z[5] == ' ');
        var strasse = zeilen.FirstOrDefault(z => z != plzZeile && z.Any(char.IsDigit));
        return plzZeile is null ? (strasse, zeilen.LastOrDefault() ?? "unbekannt", null) : (strasse, plzZeile[6..], plzZeile[..5]);
    }
}
