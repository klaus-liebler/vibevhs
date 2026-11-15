using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Application.Protokoll;

/// <summary>
/// Unveränderlicher Protokolleintrag: wer hat wann was getan. Einträge werden nie geändert oder gelöscht.
/// </summary>
public sealed class AuditEintrag
{
    private AuditEintrag()
    {
        Benutzer = string.Empty;
        BenutzerKennung = string.Empty;
        Aktion = string.Empty;
        Beschreibung = string.Empty;
    }

    public AuditEintrag(DateTimeOffset zeitpunkt, string benutzerKennung, string benutzer, string? rolle,
        string aktion, string? objektTyp, Guid? objektId, string beschreibung, bool erfolgreich = true)
    {
        Id = Ids.Neu();
        Zeitpunkt = zeitpunkt;
        BenutzerKennung = benutzerKennung;
        Benutzer = benutzer;
        Rolle = rolle;
        Aktion = aktion;
        ObjektTyp = objektTyp;
        ObjektId = objektId;
        Beschreibung = beschreibung;
        Erfolgreich = erfolgreich;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset Zeitpunkt { get; private set; }

    public string BenutzerKennung { get; private set; }

    public string Benutzer { get; private set; }

    public string? Rolle { get; private set; }

    /// <summary>Technischer Name der Aktion, z. B. "Person.NameGeaendert".</summary>
    public string Aktion { get; private set; }

    public string? ObjektTyp { get; private set; }

    public Guid? ObjektId { get; private set; }

    /// <summary>Lesbare Beschreibung für Mitarbeitende.</summary>
    public string Beschreibung { get; private set; }

    /// <summary>Bei automatischen Abläufen: ist der Schritt gelungen oder braucht er Aufmerksamkeit?</summary>
    public bool Erfolgreich { get; private set; }
}
