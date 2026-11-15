namespace VibeVHS.Domain.Personen;

/// <summary>In welcher Beziehung eine Person zur VHS steht. Eine Person kann mehrere haben.</summary>
[Flags]
public enum Personenrolle
{
    Keine = 0,
    Teilnehmende = 1,
    Dozierende = 2,
    Mitarbeitende = 4,
}
