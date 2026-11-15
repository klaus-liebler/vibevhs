using VibeVHS.Application.Dokumente;
using VibeVHS.Domain.Finanzen;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Finanzen;

/// <summary>Druckstück und Mailtext eines Belegs. Jeder Brief trägt den QR-Code „Translate this letter in your language“.</summary>
public static class Belegdruck
{
    public static Brief Brief(Beleg b, Belegempfaenger empfaenger, Finanzeinstellungen einst, string einrichtung, string? bezugNummer, Uri uebersetzung)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(empfaenger);
        ArgumentNullException.ThrowIfNull(einst);
        var de = FinanzDienst.De;
        var angaben = new List<(string, string)>
        {
            ("Belegnummer", b.Nummer ?? "Entwurf"),
            ("Belegdatum", b.Belegdatum.ToString("dd.MM.yyyy", de)),
        };
        if (bezugNummer is not null)
        {
            angaben.Add(("Bezug", bezugNummer));
        }

        if (b.Faellig is { } f)
        {
            angaben.Add((b.Art == Belegart.Honorarabrechnung ? "Auszahlung bis" : "Zahlbar bis", f.ToString("dd.MM.yyyy", de)));
        }

        if (empfaenger.LeitwegId is not null)
        {
            angaben.Add(("Leitweg-ID", empfaenger.LeitwegId));
        }

        var zeilen = b.Positionen.Select(p => (IReadOnlyList<string>)
        [
            p.Nr.ToString(de),
            p.Text,
            p.Menge == 1 ? "1" : p.Menge.ToString("0.##", de),
            new Geld(p.EinzelCent).ToString(),
            new Geld(p.BetragCent).ToString(),
        ]).ToList();
        zeilen.Add(["", "Summe", "", "", new Geld(b.BetragCent).ToString()]);

        return new Brief(einrichtung, einst.Anschrift.Replace('\n', ' '), empfaenger.Anschrift, b.Belegdatum.ToString("dd.MM.yyyy", de),
            $"{BelegartInfo.Name(b.Art)} {b.Nummer}", Absaetze(b, einst, bezugNummer), angaben, ["Pos.", "Bezeichnung", "Menge", "Einzelpreis", "Betrag"], zeilen,
            null, Hinweis(b, einst), uebersetzung);
    }

    private static List<string> Absaetze(Beleg b, Finanzeinstellungen einst, string? bezug) => b.Art switch
    {
        Belegart.Rechnung => b.Text?.Contains("Lastschrift", StringComparison.Ordinal) == true
            ? ["vielen Dank für Ihre Anmeldung. Wir berechnen Ihnen:", "Den Betrag ziehen wir per SEPA-Lastschrift ein; vorher erhalten Sie eine Vorabankündigung mit dem genauen Datum."]
            : ["vielen Dank für Ihre Anmeldung. Wir berechnen Ihnen:", $"Bitte überweisen Sie den Betrag bis zum Fälligkeitsdatum unter Angabe der Belegnummer {b.Nummer}."],
        Belegart.Mahnung => [b.Text ?? "leider konnten wir zu der unten genannten Rechnung noch keinen Zahlungseingang feststellen."],
        Belegart.Storno => [$"hiermit heben wir den Beleg {bezug} vollständig auf. Grund: {b.Text}", "Bereits gezahlte Beträge erstatten wir oder verrechnen sie mit offenen Posten."],
        Belegart.Honorarabrechnung => ["wir rechnen Ihre im Abrechnungszeitraum gehaltenen Unterrichtseinheiten wie folgt ab:", "Der Betrag wird auf Ihr hinterlegtes Konto überwiesen."],
        Belegart.Gutschrift => [$"zu Ihrem Beleg {bezug} schreiben wir Ihnen gut:"],
        _ => [b.Text ?? BelegartInfo.Name(b.Art)],
    };

    private static string Hinweis(Beleg b, Finanzeinstellungen einst)
    {
        var bank = $"Bankverbindung: {einst.Kontoinhaber} · IBAN {Iban.Anzeige(einst.Iban)}{(einst.Bic is null ? "" : $" · BIC {einst.Bic}")}";
        return b.Art switch
        {
            Belegart.Rechnung or Belegart.Mahnung => $"{einst.Steuerhinweis} {bank}{(einst.Steuernummer is null ? "" : $" · Steuernummer {einst.Steuernummer}")}",
            Belegart.Honorarabrechnung => "Freie Mitarbeit: Für die Versteuerung des Honorars und ggf. Sozialabgaben sind Sie selbst verantwortlich.",
            _ => bank,
        };
    }

    public static string Mailtext(Beleg b, Belegempfaenger empfaenger, Finanzeinstellungen einst, string einrichtung, string? bezugNummer)
    {
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(empfaenger);
        var betrag = new Geld(b.BetragCent);
        var kern = b.Art switch
        {
            Belegart.Rechnung => b.Text?.Contains("Lastschrift", StringComparison.Ordinal) == true
                ? $"anbei erhalten Sie Ihre Rechnung {b.Nummer} über {betrag}. Den Betrag ziehen wir per SEPA-Lastschrift ein; vorher kündigen wir den Einzug an."
                : $"anbei erhalten Sie Ihre Rechnung {b.Nummer} über {betrag}, zahlbar bis {b.Faellig:dd.MM.yyyy} auf das Konto {einst.Kontoinhaber}, IBAN {Iban.Anzeige(einst.Iban)}. Bitte geben Sie als Verwendungszweck {b.Nummer} an.",
            Belegart.Storno => $"der Beleg {bezugNummer} ist aufgehoben (Stornobeleg {b.Nummer} im Anhang). Grund: {b.Text}",
            Belegart.Mahnung => $"zu Ihrer Rechnung {bezugNummer} ist noch ein Betrag offen. Details finden Sie im Anhang ({b.Nummer}).",
            Belegart.Honorarabrechnung => $"anbei erhalten Sie Ihre Honorarabrechnung {b.Nummer} über {betrag}.",
            _ => $"anbei erhalten Sie den Beleg {b.Nummer} über {betrag}.",
        };
        return $"Guten Tag {empfaenger.Name},\n\n{kern}\n\nMit freundlichen Grüßen\n{einrichtung}";
    }
}
