using System.Security.Cryptography;

namespace VibeVHS.Domain.Gemeinsam;

/// <summary>
/// Zentrale Id-Erzeugung: UUIDv7 (RFC 9562) – die ersten 48 Bit sind die Millisekunden seit 1970, gespeichert als 16-Byte-BLOB in
/// Big-Endian-Reihenfolge. Damit sortieren Ids nach Erfassungszeit (gute Einfügeposition im Index). Innerhalb derselben Millisekunde
/// zählen die 12 Bit „rand_a“ hoch, die Reihenfolge der Erfassung bleibt also auch bei Gleichstand der Uhr erhalten (monoton).
/// </summary>
public static class Ids
{
    private static readonly Lock Sperre = new();
    private static long _letzteMs;
    private static int _zaehler;

    public static Guid Neu() => Neu(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Für Tests: Id zu einem vorgegebenen Zeitpunkt (weiterhin monoton).</summary>
    public static Guid Neu(long millisekunden)
    {
        long ms;
        int zaehler;
        lock (Sperre)
        {
            if (millisekunden > _letzteMs)
            {
                _letzteMs = millisekunden;
                _zaehler = RandomNumberGenerator.GetInt32(0, 0x400); // Startwert zufällig, Platz nach oben
            }
            else if (++_zaehler > 0xFFF)
            {
                _letzteMs++;
                _zaehler = 0;
            }

            ms = _letzteMs;
            zaehler = _zaehler;
        }

        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b[8..]);
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;
        b[6] = (byte)(0x70 | ((zaehler >> 8) & 0x0F));
        b[7] = (byte)zaehler;
        b[8] = (byte)(0x80 | (b[8] & 0x3F));
        return new Guid(b, bigEndian: true);
    }
}
