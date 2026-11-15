using System.Globalization;
using System.Text;
using System.Xml.Linq;
using VibeVHS.Domain.Finanzen;

namespace VibeVHS.Application.Finanzen;

/// <summary>
/// SEPA-Dateien nach DK-Spezifikation (Anlage 3): Basislastschrift pain.008.001.08 und Überweisung pain.001.001.09.
/// Zeichen außerhalb des SEPA-Zeichensatzes werden ersetzt (ä → ae …).
/// </summary>
public static class SepaXml
{
    public const string Pain008 = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";
    public const string Pain001 = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";

    public static string Lastschrift(Zahlungslauf lauf, Finanzeinstellungen einst, DateTimeOffset erstellt)
    {
        ArgumentNullException.ThrowIfNull(lauf);
        ArgumentNullException.ThrowIfNull(einst);
        XNamespace ns = Pain008;
        var posten = lauf.Aktiv.ToList();
        var gruppen = posten.GroupBy(p => p.Sequenz ?? "RCUR").OrderBy(g => g.Key).ToList();
        var doc = new XElement(ns + "Document",
            new XElement(ns + "CstmrDrctDbtInitn",
                Kopf(ns, lauf.NachrichtId, erstellt, posten, einst),
                gruppen.Select((g, i) => new XElement(ns + "PmtInf",
                    new XElement(ns + "PmtInfId", Text($"{lauf.NachrichtId}-{i + 1}", 35)),
                    new XElement(ns + "PmtMtd", "DD"),
                    new XElement(ns + "BtchBookg", "true"),
                    new XElement(ns + "NbOfTxs", g.Count()),
                    new XElement(ns + "CtrlSum", Betrag(g.Sum(p => p.BetragCent))),
                    new XElement(ns + "PmtTpInf",
                        new XElement(ns + "SvcLvl", new XElement(ns + "Cd", "SEPA")),
                        new XElement(ns + "LclInstrm", new XElement(ns + "Cd", "CORE")),
                        new XElement(ns + "SeqTp", g.Key)),
                    new XElement(ns + "ReqdColltnDt", Datum(lauf.Ausfuehrung)),
                    new XElement(ns + "Cdtr", new XElement(ns + "Nm", Text(einst.Kontoinhaber, 70))),
                    new XElement(ns + "CdtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", einst.Iban))),
                    Agent(ns, "CdtrAgt", einst.Bic),
                    new XElement(ns + "ChrgBr", "SLEV"),
                    new XElement(ns + "CdtrSchmeId", new XElement(ns + "Id", new XElement(ns + "PrvtId", new XElement(ns + "Othr",
                        new XElement(ns + "Id", einst.GlaeubigerId),
                        new XElement(ns + "SchmeNm", new XElement(ns + "Prtry", "SEPA")))))),
                    g.Select(p => new XElement(ns + "DrctDbtTxInf",
                        new XElement(ns + "PmtId", new XElement(ns + "EndToEndId", Text(p.EndToEndId, 35))),
                        new XElement(ns + "InstdAmt", new XAttribute("Ccy", "EUR"), Betrag(p.BetragCent)),
                        new XElement(ns + "DrctDbtTx", new XElement(ns + "MndtRltdInf",
                            new XElement(ns + "MndtId", Text(p.MandatReferenz ?? "", 35)),
                            new XElement(ns + "DtOfSgntr", Datum(p.MandatDatum ?? lauf.Ausfuehrung)))),
                        Agent(ns, "DbtrAgt", p.Bic),
                        new XElement(ns + "Dbtr", new XElement(ns + "Nm", Text(p.Name, 70))),
                        new XElement(ns + "DbtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", p.Iban))),
                        new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", Text(p.Verwendungszweck, 140)))))))));
        return Ausgabe(doc);
    }

    public static string Ueberweisung(Zahlungslauf lauf, Finanzeinstellungen einst, DateTimeOffset erstellt)
    {
        ArgumentNullException.ThrowIfNull(lauf);
        ArgumentNullException.ThrowIfNull(einst);
        XNamespace ns = Pain001;
        var posten = lauf.Aktiv.ToList();
        var doc = new XElement(ns + "Document",
            new XElement(ns + "CstmrCdtTrfInitn",
                Kopf(ns, lauf.NachrichtId, erstellt, posten, einst),
                new XElement(ns + "PmtInf",
                    new XElement(ns + "PmtInfId", Text($"{lauf.NachrichtId}-1", 35)),
                    new XElement(ns + "PmtMtd", "TRF"),
                    new XElement(ns + "BtchBookg", "true"),
                    new XElement(ns + "NbOfTxs", posten.Count),
                    new XElement(ns + "CtrlSum", Betrag(posten.Sum(p => p.BetragCent))),
                    new XElement(ns + "PmtTpInf", new XElement(ns + "SvcLvl", new XElement(ns + "Cd", "SEPA"))),
                    new XElement(ns + "ReqdExctnDt", new XElement(ns + "Dt", Datum(lauf.Ausfuehrung))),
                    new XElement(ns + "Dbtr", new XElement(ns + "Nm", Text(einst.Kontoinhaber, 70))),
                    new XElement(ns + "DbtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", einst.Iban))),
                    Agent(ns, "DbtrAgt", einst.Bic),
                    new XElement(ns + "ChrgBr", "SLEV"),
                    posten.Select(p => new XElement(ns + "CdtTrfTxInf",
                        new XElement(ns + "PmtId", new XElement(ns + "EndToEndId", Text(p.EndToEndId, 35))),
                        new XElement(ns + "Amt", new XElement(ns + "InstdAmt", new XAttribute("Ccy", "EUR"), Betrag(p.BetragCent))),
                        p.Bic is null ? null : Agent(ns, "CdtrAgt", p.Bic),
                        new XElement(ns + "Cdtr", new XElement(ns + "Nm", Text(p.Name, 70))),
                        new XElement(ns + "CdtrAcct", new XElement(ns + "Id", new XElement(ns + "IBAN", p.Iban))),
                        new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", Text(p.Verwendungszweck, 140))))))));
        return Ausgabe(doc);
    }

    private static XElement Kopf(XNamespace ns, string msgId, DateTimeOffset erstellt, IReadOnlyCollection<Zahlungsposten> posten, Finanzeinstellungen einst)
        => new(ns + "GrpHdr",
            new XElement(ns + "MsgId", Text(msgId, 35)),
            new XElement(ns + "CreDtTm", erstellt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(ns + "NbOfTxs", posten.Count),
            new XElement(ns + "CtrlSum", Betrag(posten.Sum(p => p.BetragCent))),
            new XElement(ns + "InitgPty", new XElement(ns + "Nm", Text(einst.Kontoinhaber, 70))));

    private static XElement Agent(XNamespace ns, string name, string? bic)
        => new(ns + name, new XElement(ns + "FinInstnId", bic is { Length: > 0 }
            ? new XElement(ns + "BICFI", bic)
            : new XElement(ns + "Othr", new XElement(ns + "Id", "NOTPROVIDED"))));

    private static string Ausgabe(XElement doc) => new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + doc;

    public static string Betrag(long cent) => (cent / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Datum(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>SEPA-Zeichensatz: a–z A–Z 0–9 / - ? : ( ) . , ' + Leerzeichen; Umlaute werden umschrieben.</summary>
    public static string Text(string? text, int max)
    {
        var sb = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            sb.Append(c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss", '–' or '—' => "-", '&' => "+",
                _ when c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' || "/-?:().,'+ ".Contains(c) => c.ToString(),
                _ when char.IsLetter(c) => RemoveDiacritics(c),
                _ => " ",
            });
        }

        var s = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return s.Length > max ? s[..max] : s;
    }

    private static string RemoveDiacritics(char c)
    {
        var zerlegt = c.ToString().Normalize(NormalizationForm.FormD);
        var basis = zerlegt.FirstOrDefault(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark);
        return basis is >= 'a' and <= 'z' or >= 'A' and <= 'Z' ? basis.ToString() : " ";
    }
}

/// <summary>Ein Umsatz aus camt.053.</summary>
public sealed record CamtUmsatz(DateOnly Buchungstag, DateOnly? Valuta, long BetragCent, string? GegenName, string? GegenIban, string Verwendungszweck,
    string? EndToEndId, string? Rueckgabegrund);

public sealed record CamtAuszug(string AuszugId, string? Iban, DateOnly? Von, DateOnly? Bis, long? EndsaldoCent, IReadOnlyList<CamtUmsatz> Umsaetze);

/// <summary>Liest Kontoauszüge im Format camt.053 (Versionen 001.02 bis 001.08; namensraumunabhängig).</summary>
public static class Camt053
{
    public static CamtAuszug Lesen(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"Die Datei ist kein gültiges XML: {ex.Message}", ex);
        }

        var stmt = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Stmt")
                   ?? throw new InvalidOperationException("Die Datei enthält keinen Kontoauszug (camt.053: Element „Stmt“ fehlt).");
        static XElement? K(XElement? e, params string[] pfad)
        {
            foreach (var p in pfad)
            {
                e = e?.Elements().FirstOrDefault(x => x.Name.LocalName == p);
            }

            return e;
        }

        static long Cent(XElement? amt, XElement? ind)
        {
            var wert = decimal.Parse(amt?.Value ?? "0", CultureInfo.InvariantCulture);
            return (long)Math.Round(wert * 100m) * (ind?.Value == "DBIT" ? -1 : 1);
        }

        static DateOnly? Tag(XElement? e)
        {
            var d = K(e, "Dt")?.Value ?? K(e, "DtTm")?.Value;
            return d is null ? null : DateOnly.FromDateTime(DateTime.Parse(d, CultureInfo.InvariantCulture));
        }

        var schluss = stmt.Elements().Where(e => e.Name.LocalName == "Bal").FirstOrDefault(b => K(b, "Tp", "CdOrPrtry", "Cd")?.Value == "CLBD");
        var umsaetze = new List<CamtUmsatz>();
        foreach (var ntry in stmt.Elements().Where(e => e.Name.LocalName == "Ntry"))
        {
            var ind = K(ntry, "CdtDbtInd");
            var buchung = Tag(K(ntry, "BookgDt")) ?? DateOnly.FromDateTime(DateTime.Today);
            var valuta = Tag(K(ntry, "ValDt"));
            var details = ntry.Descendants().Where(e => e.Name.LocalName == "TxDtls").ToList();
            if (details.Count == 0)
            {
                umsaetze.Add(new(buchung, valuta, Cent(K(ntry, "Amt"), ind), null, null, K(ntry, "AddtlNtryInf")?.Value ?? "", null, null));
                continue;
            }

            foreach (var tx in details)
            {
                var betrag = K(tx, "AmtDtls", "TxAmt", "Amt") ?? K(tx, "Amt") ?? (details.Count == 1 ? K(ntry, "Amt") : null);
                var txInd = K(tx, "CdtDbtInd") ?? ind;
                var gutschrift = txInd?.Value != "DBIT";
                var partei = gutschrift ? "Dbtr" : "Cdtr";
                var name = K(tx, "RltdPties", partei, "Nm")?.Value ?? K(tx, "RltdPties", partei, "Pty", "Nm")?.Value;
                var iban = K(tx, "RltdPties", partei + "Acct", "Id", "IBAN")?.Value;
                var zweck = string.Join(" ", tx.Descendants().Where(e => e.Name.LocalName == "Ustrd").Select(e => e.Value.Trim()));
                umsaetze.Add(new(buchung, valuta, Cent(betrag, txInd), name, iban, zweck, K(tx, "Refs", "EndToEndId")?.Value,
                    K(tx, "RtrInf", "Rsn", "Cd")?.Value));
            }
        }

        var zeitraum = K(stmt, "FrToDt");
        var bal = schluss is null ? (long?)null : Cent(K(schluss, "Amt"), K(schluss, "CdtDbtInd"));
        return new(K(stmt, "Id")?.Value ?? Guid.NewGuid().ToString("N"), K(stmt, "Acct", "Id", "IBAN")?.Value,
            Tag(K(zeitraum, "FrDtTm") is { } f ? new XElement("x", new XElement("DtTm", f.Value)) : null),
            Tag(K(zeitraum, "ToDtTm") is { } t ? new XElement("x", new XElement("DtTm", t.Value)) : null),
            bal, umsaetze);
    }

    /// <summary>Erzeugt einen camt.053.001.08 (für Tests und die Demo „Kontoauszug simulieren“).</summary>
    public static string Erzeugen(string auszugId, string iban, DateOnly tag, IEnumerable<CamtUmsatz> umsaetze)
    {
        XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:camt.053.001.08";
        var liste = umsaetze.ToList();
        static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var doc = new XElement(ns + "Document",
            new XElement(ns + "BkToCstmrStmt",
                new XElement(ns + "GrpHdr", new XElement(ns + "MsgId", auszugId), new XElement(ns + "CreDtTm", $"{D(tag)}T20:00:00")),
                new XElement(ns + "Stmt",
                    new XElement(ns + "Id", auszugId),
                    new XElement(ns + "FrToDt", new XElement(ns + "FrDtTm", $"{D(tag)}T00:00:00"), new XElement(ns + "ToDtTm", $"{D(tag)}T23:59:59")),
                    new XElement(ns + "Acct", new XElement(ns + "Id", new XElement(ns + "IBAN", iban))),
                    liste.Select(u => new XElement(ns + "Ntry",
                        new XElement(ns + "Amt", new XAttribute("Ccy", "EUR"), SepaXml.Betrag(Math.Abs(u.BetragCent))),
                        new XElement(ns + "CdtDbtInd", u.BetragCent >= 0 ? "CRDT" : "DBIT"),
                        new XElement(ns + "Sts", new XElement(ns + "Cd", "BOOK")),
                        new XElement(ns + "BookgDt", new XElement(ns + "Dt", D(u.Buchungstag))),
                        new XElement(ns + "ValDt", new XElement(ns + "Dt", D(u.Valuta ?? u.Buchungstag))),
                        new XElement(ns + "NtryDtls", new XElement(ns + "TxDtls",
                            new XElement(ns + "Refs", new XElement(ns + "EndToEndId", u.EndToEndId ?? "NOTPROVIDED")),
                            new XElement(ns + "RltdPties",
                                new XElement(ns + (u.BetragCent >= 0 ? "Dbtr" : "Cdtr"), new XElement(ns + "Pty", new XElement(ns + "Nm", u.GegenName ?? ""))),
                                u.GegenIban is null ? null : new XElement(ns + (u.BetragCent >= 0 ? "DbtrAcct" : "CdtrAcct"), new XElement(ns + "Id", new XElement(ns + "IBAN", u.GegenIban)))),
                            new XElement(ns + "RmtInf", new XElement(ns + "Ustrd", u.Verwendungszweck)),
                            u.Rueckgabegrund is null ? null : new XElement(ns + "RtrInf", new XElement(ns + "Rsn", new XElement(ns + "Cd", u.Rueckgabegrund))))))))));
        return new XDeclaration("1.0", "UTF-8", null) + Environment.NewLine + doc;
    }
}
