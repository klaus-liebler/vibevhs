namespace VibeVHS.Domain.Programm;

/// <summary>Entfernungen aus Koordinaten (Mock für Fahrtkosten): Luftlinie mal Umwegfaktor.</summary>
public static class Geo
{
    /// <summary>Faktor von der Luftlinie zur Straßenentfernung (Erfahrungswert im ländlichen Raum).</summary>
    public const double Umwegfaktor = 1.3;

    public static double LuftlinieKm(double breite1, double laenge1, double breite2, double laenge2)
    {
        const double R = 6371.0;
        static double Rad(double g) => g * Math.PI / 180;
        var dB = Rad(breite2 - breite1);
        var dL = Rad(laenge2 - laenge1);
        var a = (Math.Sin(dB / 2) * Math.Sin(dB / 2)) + (Math.Cos(Rad(breite1)) * Math.Cos(Rad(breite2)) * Math.Sin(dL / 2) * Math.Sin(dL / 2));
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Geschätzte einfache Straßenentfernung in km (eine Nachkommastelle).</summary>
    public static decimal StrasseKm(double breite1, double laenge1, double breite2, double laenge2)
        => Math.Round((decimal)(LuftlinieKm(breite1, laenge1, breite2, laenge2) * Umwegfaktor), 1);
}
