namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Einheitliche Korrekturregel für erfasste Einträge (QA-63, aus SchoolCraft FA-3235): Wer etwas eingetragen hat, darf es 30 Minuten lang
/// ohne Begründung berichtigen; danach – und an fremden Einträgen (Verwaltung) jederzeit – nur mit Begründung. Der vorige Stand bleibt im
/// Protokoll nachvollziehbar. Gilt nicht für festgeschriebene Belege (GoBD: Storno und Neubeleg, FA-126) und nicht für bitemporale
/// Stammdaten (dort „Korrektur“ mit Grund, QA-06).
/// </summary>
public static class Korrekturregel
{
    public static readonly TimeSpan Fenster = TimeSpan.FromMinutes(30);

    /// <summary>Ob die Person noch ohne Begründung berichtigen darf.</summary>
    public static bool OhneGrund(bool eigener, DateTimeOffset eingetragen, DateTimeOffset jetzt) => eigener && jetzt <= eingetragen + Fenster;

    /// <summary>Bis wann eine Berichtigung ohne Begründung möglich ist (für die Anzeige).</summary>
    public static DateTimeOffset OhneGrundBis(DateTimeOffset eingetragen) => eingetragen + Fenster;

    /// <summary>Fehlertext, wenn die Berichtigung so nicht zulässig ist; sonst <c>null</c>.</summary>
    public static string? Pruefen(bool eigener, DateTimeOffset eingetragen, DateTimeOffset jetzt, string? grund)
        => OhneGrund(eigener, eingetragen, jetzt) || !string.IsNullOrWhiteSpace(grund)
            ? null
            : eigener
                ? $"Berichtigung nach {Fenster.TotalMinutes:0} Minuten: Bitte einen Grund angeben – der vorige Eintrag bleibt im Protokoll erhalten."
                : "Berichtigung eines fremden Eintrags: Bitte einen Grund angeben – der vorige Eintrag bleibt im Protokoll erhalten.";
}
