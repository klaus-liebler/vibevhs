using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Speicherformate in PostgreSQL (global für alle Spalten, siehe <see cref="VibeVhsDbContext"/>): Ids als <c>uuid</c> (UUIDv7, damit zeitlich
/// sortiert), Kalendertage als <c>date</c>, Zeitpunkte und Termine als <c>timestamptz</c> in UTC, auf Millisekunden gekürzt (gleiche Werte nach
/// dem Lesen, unabhängig davon, ob ein Wert aus der Datenbank oder aus dem Arbeitsspeicher kommt). Termine sind Ortszeiten (DateTime ohne
/// Zone); gespeichert wird der eindeutige UTC-Zeitpunkt, gelesen wieder die Ortszeit (Einstellung „Zeitzone“).
/// </summary>
public static class Speicherformate
{
    /// <summary>Gespeicherter Wert für „offenes Ende“ (NFA-15).</summary>
    public static readonly DateOnly Offen = new(9999, 12, 31);

    public static DateTimeOffset Utc(DateTimeOffset z)
    {
        var ticks = z.UtcTicks - (z.UtcTicks % TimeSpan.TicksPerMillisecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static DateTimeOffset Zeitpunkt(DateTimeOffset utc) => Zeitzone.Ortszeit(utc);

    public static DateTime Termin(DateTime ortszeit)
    {
        var utc = Zeitzone.NachUtc(ortszeit);
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }

    public static DateTime TerminAusUtc(DateTime utc) => Zeitzone.AusUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
}

/// <summary>
/// Ende einer Gültigkeit (NFA-15): offenes Ende als 9999-12-31 statt NULL. Abfragen „gilt am Tag“ werden so zu <c>GueltigBis &gt; tag</c>
/// ohne <c>OR … IS NULL</c> (indexfreundlich); in der Fachklasse bleibt das offene Ende <c>null</c>.
/// </summary>
// EF1001: Konverter, die NULL umwandeln („convertsNulls“), kennzeichnet EF Core als nicht stabile API. Bewusst genutzt (NFA-15), durch die
// Versions- und Zeitumstellungstests abgesichert.
#pragma warning disable EF1001
internal sealed class OffenesEnde() : ValueConverter<DateOnly?, DateOnly>(
    d => d == null ? Speicherformate.Offen : d.Value,
    z => z == Speicherformate.Offen ? null : z,
    convertsNulls: true);
#pragma warning restore EF1001

internal sealed class ZeitpunktInUtc() : ValueConverter<DateTimeOffset, DateTimeOffset>(z => Speicherformate.Utc(z), z => Speicherformate.Zeitpunkt(z));

internal sealed class TerminInUtc() : ValueConverter<DateTime, DateTime>(d => Speicherformate.Termin(d), u => Speicherformate.TerminAusUtc(u));
