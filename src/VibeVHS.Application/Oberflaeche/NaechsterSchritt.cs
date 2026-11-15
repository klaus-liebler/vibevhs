namespace VibeVHS.Application.Oberflaeche;

/// <summary>
/// Ein sinnvoller Folgeschritt nach der aktuellen Aktion, z. B. "Bestätigung drucken" nach einer Anmeldung.
/// </summary>
/// <param name="Titel">Beschriftung der Schaltfläche.</param>
/// <param name="Ziel">Relative Adresse in der Anwendung.</param>
/// <param name="Hinweis">Kurze Erklärung, warum dieser Schritt sinnvoll ist.</param>
/// <param name="Freigabe">Ob der Schritt gerade möglich ist; ohne Angabe: ja.</param>
/// <param name="Hauptschritt">Hervorgehobener, empfohlener Schritt.</param>
public sealed record NaechsterSchritt(
    string Titel,
    string Ziel,
    string? Hinweis = null,
    Freigabe? Freigabe = null,
    bool Hauptschritt = false)
{
    public Freigabe Status => Freigabe ?? Oberflaeche.Freigabe.Ja;
}
