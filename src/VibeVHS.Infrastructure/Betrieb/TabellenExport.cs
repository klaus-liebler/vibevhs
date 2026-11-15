using ClosedXML.Excel;
using VibeVHS.Application.Betrieb;

namespace VibeVHS.Infrastructure.Betrieb;

/// <summary>Tabelle als XLSX: Kopfzeile fett und fixiert, Zahlen/Geld/Datum als echte Werte, Autofilter.</summary>
internal sealed class TabellenExport : ITabellenExport
{
    public byte[] Xlsx(Tabelle tabelle)
    {
        ArgumentNullException.ThrowIfNull(tabelle);
        using var mappe = new XLWorkbook();
        var name = new string(tabelle.Titel.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray());
        var blatt = mappe.Worksheets.Add(name.Length > 31 ? name[..31] : name.Length == 0 ? "Auswertung" : name);
        for (var i = 0; i < tabelle.Spalten.Count; i++)
        {
            blatt.Cell(1, i + 1).Value = tabelle.Spalten[i].Titel;
        }

        for (var z = 0; z < tabelle.Zeilen.Count; z++)
        {
            for (var s = 0; s < tabelle.Spalten.Count; s++)
            {
                var zelle = blatt.Cell(z + 2, s + 1);
                switch (tabelle.Zeilen[z][s])
                {
                    case null:
                        break;
                    case decimal d:
                        zelle.Value = tabelle.Spalten[s].Typ == Spaltentyp.Prozent ? d / 100m : d;
                        zelle.Style.NumberFormat.Format = tabelle.Spalten[s].Typ switch
                        {
                            Spaltentyp.Geld => "#,##0.00 €",
                            Spaltentyp.Prozent => "0.0%",
                            _ => "#,##0.##",
                        };
                        break;
                    case int i:
                        zelle.Value = i;
                        break;
                    case long l:
                        zelle.Value = l;
                        break;
                    case DateOnly d:
                        zelle.Value = d.ToDateTime(TimeOnly.MinValue);
                        zelle.Style.DateFormat.Format = "dd.MM.yyyy";
                        break;
                    case DateTime d:
                        zelle.Value = d.Date;
                        zelle.Style.DateFormat.Format = "dd.MM.yyyy";
                        break;
                    case var o:
                        zelle.Value = o.ToString();
                        break;
                }
            }
        }

        var kopf = blatt.Range(1, 1, 1, tabelle.Spalten.Count);
        kopf.Style.Font.Bold = true;
        blatt.SheetView.FreezeRows(1);
        if (tabelle.Zeilen.Count > 0)
        {
            blatt.Range(1, 1, tabelle.Zeilen.Count + 1, tabelle.Spalten.Count).SetAutoFilter();
        }

        blatt.Columns().AdjustToContents(1, Math.Min(tabelle.Zeilen.Count + 1, 200));
        using var ms = new MemoryStream();
        mappe.SaveAs(ms);
        return ms.ToArray();
    }
}
