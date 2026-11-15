namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Schlüsselvergabe: GUID Version 7 (zeitlich sortierbar, gut für B-Baum-Indizes in SQLite).
/// </summary>
public static class Ids
{
    public static Guid Neu() => Guid.CreateVersion7();
}
