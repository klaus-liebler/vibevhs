namespace VibeVHS.Application.Betrieb;

/// <summary>
/// Steuerbare Uhr für den Schulungsbetrieb (QA-68, aus SchoolCraft FK-14): Spielzeit = Anker + (Realzeit − Realanker) × Tempo. Tempo 0 hält
/// an, 0,1 ist Zeitlupe, 60 Zeitraffer. Nur <see cref="GetUtcNow"/> folgt der Spielzeit; Zeitgeber und Messungen bleiben Realzeit, ebenso
/// Anmeldecookies (in Program.cs ausdrücklich mit <see cref="TimeProvider.System"/>).
/// </summary>
public sealed class Spielzeit : TimeProvider
{
    public const double MinTempo = 0.01, MaxTempo = 100;

    private readonly Lock sperre = new();
    private readonly TimeProvider echt;
    private DateTimeOffset ankerEcht, ankerSpiel;
    private double tempo, letztesTempo = 1;
    private readonly TimeZoneInfo zone;

    public Spielzeit(DateTimeOffset start, double tempo = 0, TimeProvider? echt = null, TimeZoneInfo? zone = null)
    {
        this.echt = echt ?? System;
        this.zone = zone ?? TimeZoneInfo.Local;
        ankerEcht = this.echt.GetUtcNow();
        ankerSpiel = start;
        this.tempo = Begrenzt(tempo);
    }

    /// <summary>Nach jeder Änderung von Zeitpunkt, Tempo, Wartung oder Weltstand (Spielzeitleiste, Zeitplaner).</summary>
    public event Action? Geaendert;

    public override TimeZoneInfo LocalTimeZone => zone;

    public override DateTimeOffset GetUtcNow()
    {
        lock (sperre)
        {
            return Berechnet(echt.GetUtcNow());
        }
    }

    public double Tempo
    {
        get
        {
            lock (sperre)
            {
                return tempo;
            }
        }
    }

    public bool Angehalten => Tempo == 0;

    /// <summary>Tempo, mit dem „Weiter“ fortsetzt.</summary>
    public double LetztesTempo
    {
        get
        {
            lock (sperre)
            {
                return letztesTempo;
            }
        }
    }

    /// <summary>Steigt bei jedem Sprung; geöffnete Seiten laden dann neu.</summary>
    public long Weltstand { get; private set; }

    /// <summary>Während eines Sprungs: Anfragen warten, der Zeitplaner pausiert.</summary>
    public bool Wartung { get; private set; }

    public void TempoSetzen(double neu)
    {
        lock (sperre)
        {
            var jetztEcht = echt.GetUtcNow();
            ankerSpiel = Berechnet(jetztEcht);
            ankerEcht = jetztEcht;
            tempo = Begrenzt(neu);
            if (tempo > 0)
            {
                letztesTempo = tempo;
            }
        }

        Geaendert?.Invoke();
    }

    public void Anhalten() => TempoSetzen(0);

    public void Weiter() => TempoSetzen(LetztesTempo);

    /// <summary>Nur vorwärts (sonst lägen Vorgänge in der Zukunft); zurück geht es nur mit einem Sprung zu einer Marke.</summary>
    public void Vorstellen(DateTimeOffset zeitpunkt)
    {
        if (zeitpunkt < GetUtcNow())
        {
            throw new InvalidOperationException("Die Spielzeit lässt sich nur vorwärts stellen. Für einen früheren Zeitpunkt bitte zu einer Sprungmarke springen – dann passt auch der Datenstand.");
        }

        Stellen(zeitpunkt);
    }

    /// <summary>Ohne Prüfung stellen (Sprung, Vorlagenbau); das Tempo bleibt.</summary>
    public void Stellen(DateTimeOffset zeitpunkt)
    {
        lock (sperre)
        {
            ankerEcht = echt.GetUtcNow();
            ankerSpiel = zeitpunkt;
        }

        Geaendert?.Invoke();
    }

    public void WartungSetzen(bool an)
    {
        Wartung = an;
        Geaendert?.Invoke();
    }

    /// <summary>Nach einem Sprung: Spielzeit auf der Marke, angehalten, neuer Weltstand.</summary>
    public void NeuerWeltstand(DateTimeOffset zeitpunkt)
    {
        lock (sperre)
        {
            ankerEcht = echt.GetUtcNow();
            ankerSpiel = zeitpunkt;
            if (tempo > 0)
            {
                letztesTempo = tempo;
            }

            tempo = 0;
            Weltstand++;
        }

        Geaendert?.Invoke();
    }

    // Immer mit Versatz 0 (UTC): TimeProvider.GetLocalNow rechnet mit der Uhrzeit von GetUtcNow, nicht mit deren Versatz.
    private DateTimeOffset Berechnet(DateTimeOffset jetztEcht) =>
        (tempo == 0 ? ankerSpiel : ankerSpiel + TimeSpan.FromTicks((long)((jetztEcht - ankerEcht).Ticks * tempo))).ToUniversalTime();

    private static double Begrenzt(double t) => t <= 0 || double.IsNaN(t) ? 0 : Math.Clamp(t, MinTempo, MaxTempo);
}

/// <summary>Benannter Spielzeitpunkt mit beschriebenem Zustand; je Marke eine Vorlage-Datenbank.</summary>
/// <param name="Versatz">Abstand vom Bezugstag (Montag 00:00 der Woche, in der die Vorlagen gebaut wurden).</param>
public sealed record Sprungmarke(string Id, string Titel, string Zustand, TimeSpan Versatz)
{
    public string Vorlage(string praefix) => $"{praefix}_{Id.Replace('-', '_')}";
}

/// <summary>
/// Sprungmarken der Schulungswoche (QA-68): eine Woche am Tresen und im Büro der vhs. Die Demodaten entstehen relativ zum Bezugstag; jede
/// spätere Marke entsteht aus der vorigen, indem die Spielzeit vorläuft und dabei die zeitgesteuerte Automatik einmal je fälligem Zeitpunkt läuft.
/// </summary>
public static class Schulungswoche
{
    public static IReadOnlyList<Sprungmarke> Marken { get; } =
    [
        new("ausgangslage", "Montag 7:30", "Tresen öffnet: frische Demodaten, Anmeldungen, Aufgaben in den Körben.", new TimeSpan(0, 7, 30, 0)),
        new("mo-1800", "Montag 18:00", "Abendkurse beginnen: Anwesenheit und Check-in; der Tag ist im Protokoll.", new TimeSpan(0, 18, 0, 0)),
        new("di-0800", "Dienstag 8:00", "Nach der ersten Nacht: Automatik gelaufen (Aufgaben, Erinnerungen, Rechnungs- und Mahnvorschläge).", new TimeSpan(1, 8, 0, 0)),
        new("fr-1300", "Freitag 13:00", "Wochenende naht: Mindestteilnehmerzahlen der nächsten Woche, offene Aufgaben.", new TimeSpan(4, 13, 0, 0)),
        new("mo2-0730", "Montag eine Woche später, 7:30", "Eine Woche Betrieb mit Automatik: Eskalationen, Fristen, Kursbeginne.", new TimeSpan(7, 7, 30, 0)),
    ];

    public static Sprungmarke? Marke(string? id) => Marken.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bezugstag: Montag 00:00 (Ortszeit) der Woche, in die <paramref name="tag"/> fällt.</summary>
    public static DateOnly Bezugstag(DateOnly tag) => tag.AddDays(-(((int)tag.DayOfWeek + 6) % 7));
}
