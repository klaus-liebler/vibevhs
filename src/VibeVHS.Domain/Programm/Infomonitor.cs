using System.Security.Cryptography;
using System.Text;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Programm;

/// <summary>
/// Bildschirm im Foyer eines Standorts („Heute im Haus“, QA-65, aus SchoolCraft FA-412): zeigt die Kurse des Tages mit Uhrzeit und Raum und
/// markiert Ausfälle – nie Teilnehmende. Der Bildschirm ruft eine geheime Adresse auf; gespeichert wird nur der SHA-256-Prüfwert. Für die
/// Vorschau in der Verwaltung gibt es ein eigenes, 30 Minuten gültiges Geheimnis, damit die Adresse des Geräts gleich bleibt.
/// </summary>
public sealed class Infomonitor
{
    /// <summary>Wie lange eine Vorschau-Adresse gilt (echte Zeit).</summary>
    public static readonly TimeSpan VorschauDauer = TimeSpan.FromMinutes(30);

    private Infomonitor()
    {
        Name = Pruefwert = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Bezeichnung in der Verwaltung, z. B. „Foyer Emsdetten“.</summary>
    public string Name { get; private set; }

    /// <summary>Standort (Gebäude); gezeigt werden seine Räume. <c>null</c> = alle Orte.</summary>
    public Guid? OrtId { get; private set; }

    /// <summary>Überschrift auf dem Bildschirm (Standard: „Heute im Haus“).</summary>
    public string? Ueberschrift { get; private set; }

    /// <summary>SHA-256 des Geheimnisses aus der Adresse des Geräts.</summary>
    public string Pruefwert { get; private set; }

    public string? VorschauPruefwert { get; private set; }

    public DateTimeOffset? VorschauBis { get; private set; }

    public DateTimeOffset AngelegtAm { get; private set; }

    public DateTimeOffset? GesperrtAm { get; private set; }

    public DateTimeOffset? ZuletztAbgerufen { get; private set; }

    public bool Gesperrt => GesperrtAm is not null;

    public static (Infomonitor Monitor, string Geheimnis) Anlegen(string name, Guid? ortId, string? ueberschrift, DateTimeOffset jetzt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var m = new Infomonitor { Id = Ids.Neu(), Name = name.Trim(), OrtId = ortId, Ueberschrift = Leer(ueberschrift), AngelegtAm = jetzt };
        return (m, m.NeuesGeheimnis());
    }

    public void Aendern(string name, Guid? ortId, string? ueberschrift)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        OrtId = ortId;
        Ueberschrift = Leer(ueberschrift);
    }

    /// <summary>Neue Adresse für das Gerät; die alte gilt ab sofort nicht mehr.</summary>
    public string NeuesGeheimnis()
    {
        var geheimnis = Zufall();
        Pruefwert = HashVon(geheimnis);
        return geheimnis;
    }

    /// <summary>Vorschau-Adresse (30 Minuten); eine frühere Vorschau verfällt.</summary>
    public string Vorschau(DateTimeOffset jetzt)
    {
        if (Gesperrt)
        {
            throw new InvalidOperationException("Ein gesperrter Monitor hat keine Vorschau. Bitte erst entsperren.");
        }

        var geheimnis = Zufall();
        VorschauPruefwert = HashVon(geheimnis);
        VorschauBis = jetzt + VorschauDauer;
        return geheimnis;
    }

    /// <summary>Passt das Geheimnis zu diesem Monitor (Gerät oder gültige Vorschau)?</summary>
    public bool Passt(string pruefwert, DateTimeOffset jetzt)
        => !Gesperrt && (pruefwert == Pruefwert || (pruefwert == VorschauPruefwert && VorschauBis > jetzt));

    public void Abgerufen(DateTimeOffset jetzt) => ZuletztAbgerufen = jetzt;

    public void Sperren(DateTimeOffset jetzt) => GesperrtAm ??= jetzt;

    public void Entsperren() => GesperrtAm = null;

    public static string HashVon(string geheimnis) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(geheimnis.Trim())));

    private static string Zufall() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
