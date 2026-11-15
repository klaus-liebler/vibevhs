using VibeVHS.Application.Programm;

namespace VibeVHS.Adapters.Mocks;

/// <summary>
/// Mock-Geokodierer: Ortsmittelpunkte der Postleitzahlen im Kreis Steinfurt und Umgebung (gerundet).
/// Unbekannte Postleitzahlen liefern nichts – dann rechnet das Honorarmodell mit der Pauschale aus dem Vertrag.
/// </summary>
public sealed class MockGeokodierer : IGeokodierer
{
    private static readonly Dictionary<string, (double, double)> Tabelle = new()
    {
        ["48282"] = (52.173, 7.527), // Emsdetten
        ["48268"] = (52.092, 7.609), // Greven
        ["48369"] = (52.175, 7.633), // Saerbeck
        ["48429"] = (52.280, 7.440), // Rheine
        ["48431"] = (52.275, 7.430),
        ["48432"] = (52.290, 7.460),
        ["48485"] = (52.240, 7.370), // Neuenkirchen
        ["48565"] = (52.150, 7.340), // Steinfurt
        ["48346"] = (52.040, 7.890), // Ostbevern
        ["48291"] = (52.110, 7.700), // Telgte
        ["48493"] = (52.170, 7.420), // Wettringen
        ["48366"] = (52.100, 7.520), // Laer
        ["48143"] = (51.962, 7.626), // Münster Innenstadt
        ["48147"] = (51.975, 7.630),
        ["48149"] = (51.965, 7.600),
        ["48151"] = (51.950, 7.610),
        ["48155"] = (51.955, 7.660),
        ["48157"] = (51.990, 7.650),
        ["48159"] = (51.990, 7.600),
        ["49477"] = (52.277, 7.715), // Ibbenbüren
        ["49479"] = (52.270, 7.750),
        ["48477"] = (52.390, 7.470), // Hörstel
        ["48496"] = (52.400, 7.580), // Hopsten
    };

    public (double Breite, double Laenge)? Postleitzahl(string? plz)
        => plz is not null && Tabelle.TryGetValue(plz.Trim(), out var k) ? k : null;
}
