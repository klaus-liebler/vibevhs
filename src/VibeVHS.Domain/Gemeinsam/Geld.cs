using System.Globalization;

namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Geldbetrag in Euro-Cent. Es gibt bewusst keine Gleitkomma-Beträge im System.
/// </summary>
public readonly record struct Geld(long Cent) : IComparable<Geld>
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    public static Geld Null => new(0);

    public static Geld Euro(long euro, int cent = 0)
    {
        if (cent is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(cent), cent, "Cent muss zwischen 0 und 99 liegen.");
        }

        return new Geld(checked((euro * 100) + (euro < 0 ? -cent : cent)));
    }

    public decimal InEuro => Cent / 100m;

    public static Geld operator +(Geld a, Geld b) => new(checked(a.Cent + b.Cent));

    public static Geld operator -(Geld a, Geld b) => new(checked(a.Cent - b.Cent));

    public static Geld operator -(Geld a) => new(checked(-a.Cent));

    public static Geld operator *(Geld a, int faktor) => new(checked(a.Cent * faktor));

    public static bool operator <(Geld a, Geld b) => a.Cent < b.Cent;

    public static bool operator >(Geld a, Geld b) => a.Cent > b.Cent;

    public static bool operator <=(Geld a, Geld b) => a.Cent <= b.Cent;

    public static bool operator >=(Geld a, Geld b) => a.Cent >= b.Cent;

    public int CompareTo(Geld other) => Cent.CompareTo(other.Cent);

    /// <summary>
    /// Verteilt den Betrag ohne Rundungsverlust auf <paramref name="teile"/> Teile
    /// (z. B. Ratenzahlung). Übrige Cent landen in den ersten Teilen.
    /// </summary>
    public IReadOnlyList<Geld> Aufteilen(int teile)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(teile, 1);
        var basis = Cent / teile;
        var rest = Cent % teile;
        var ergebnis = new Geld[teile];
        for (var i = 0; i < teile; i++)
        {
            ergebnis[i] = new Geld(basis + (i < Math.Abs(rest) ? Math.Sign(rest) : 0));
        }

        return ergebnis;
    }

    public override string ToString() => InEuro.ToString("N2", Deutsch) + " €";
}
