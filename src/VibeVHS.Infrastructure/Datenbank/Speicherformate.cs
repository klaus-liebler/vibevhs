using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Speicherformate (global für alle Spalten, siehe <see cref="VibeVhsDbContext"/>):
/// Ids als 16-Byte-BLOB in Big-Endian-Reihenfolge (UUIDv7 sortiert damit zeitlich), Kalendertage als Ganzzahl JJJJMMTT,
/// Zeitpunkte als Millisekunden seit 1970 (UTC), Termin-Uhrzeiten (Ortszeit) ebenfalls als UTC-Millisekunden.
/// </summary>
public static class Speicherformate
{
    public static byte[] IdBytes(Guid id) => id.ToByteArray(bigEndian: true);

    /// <summary>Gespeicherter Wert für „offenes Ende“ (NFA-15).</summary>
    public const int Offen = 99991231;

    public static int Tag(DateOnly d) => (d.Year * 10000) + (d.Month * 100) + d.Day;

    public static DateOnly Tag(int z) => new(z / 10000, z / 100 % 100, z % 100);

    public static long Millisekunden(DateTimeOffset z) => z.ToUnixTimeMilliseconds();

    public static DateTimeOffset Zeitpunkt(long ms) => Zeitzone.Ortszeit(DateTimeOffset.FromUnixTimeMilliseconds(ms));

    public static long Termin(DateTime ortszeit) => new DateTimeOffset(Zeitzone.NachUtc(ortszeit), TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static DateTime Termin(long ms) => Zeitzone.AusUtc(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);
}

internal sealed class IdAlsBytes() : ValueConverter<Guid, byte[]>(g => Speicherformate.IdBytes(g), b => new Guid(b, true));

/// <summary>
/// Ende einer Gültigkeit (NFA-15): offenes Ende als 99991231 statt NULL. Abfragen „gilt am Tag“ werden so zu <c>GueltigBis &gt; tag</c>
/// ohne <c>OR … IS NULL</c> (indexfreundlich); in der Fachklasse bleibt das offene Ende <c>null</c>.
/// </summary>
// EF1001: Konverter, die NULL umwandeln („convertsNulls“), kennzeichnet EF Core als nicht stabile API. Bewusst genutzt (NFA-15), durch die
// Versions- und Zeitumstellungstests abgesichert.
#pragma warning disable EF1001
internal sealed class OffenesEnde() : ValueConverter<DateOnly?, int>(
    d => d == null ? Speicherformate.Offen : Speicherformate.Tag(d.Value),
    z => z == Speicherformate.Offen ? null : Speicherformate.Tag(z),
    convertsNulls: true);
#pragma warning restore EF1001

internal sealed class TagAlsZahl() : ValueConverter<DateOnly, int>(d => Speicherformate.Tag(d), z => Speicherformate.Tag(z));

internal sealed class ZeitpunktAlsMillisekunden() : ValueConverter<DateTimeOffset, long>(z => Speicherformate.Millisekunden(z), ms => Speicherformate.Zeitpunkt(ms));

internal sealed class TerminAlsUtcMillisekunden() : ValueConverter<DateTime, long>(d => Speicherformate.Termin(d), ms => Speicherformate.Termin(ms));
