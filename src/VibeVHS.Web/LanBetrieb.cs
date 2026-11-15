using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace VibeVHS.Web;

/// <summary>
/// Handy-Test im lokalen Netz (Profil "lan", Lan:Aktiv=true): HTTPS auf allen Schnittstellen mit dem Zertifikat aus
/// %USERPROFILE%\.vibevhs\lan (erzeugt mit tools/LanZertifikat.cs), öffentliche Adresse = https://LAN-IP:Port/.
/// Die Kamera im Handy-Browser (getUserMedia) funktioniert nur über HTTPS.
/// </summary>
internal static class LanBetrieb
{
    public static void Einrichten(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue("Lan:Aktiv", false))
        {
            return;
        }

        var port = builder.Configuration.GetValue("Lan:Port", 7189);
        var ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vibevhs", "lan");
        var pfx = Path.Combine(ordner, "vibevhs-lan.pfx");
        var passwortDatei = Path.Combine(ordner, "passwort.txt");
        if (!File.Exists(pfx) || !File.Exists(passwortDatei))
        {
            throw new InvalidOperationException($"Für das Profil \"lan\" fehlt das Zertifikat in {ordner}. Bitte zuerst ausführen: dotnet run tools/LanZertifikat.cs");
        }

        var zertifikat = X509CertificateLoader.LoadPkcs12FromFile(pfx, File.ReadAllText(passwortDatei).Trim());
        builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(port, l => l.UseHttps(zertifikat)));

        var ip = LanAdresse();
        builder.Configuration["OeffentlicheAdresse"] = $"https://{ip ?? "localhost"}:{port}/";
        Console.WriteLine($"LAN-Betrieb: https://{ip ?? "localhost"}:{port}/ (Zertifikat bis {zertifikat.NotAfter:dd.MM.yyyy})");
    }

    /// <summary>IPv4-Adresse der Schnittstelle mit Standard-Gateway (das eigentliche LAN, keine virtuellen Adapter).</summary>
    public static string? LanAdresse()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .SelectMany(p => p.UnicastAddresses.Select(u => u.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            .FirstOrDefault()?.ToString();
}
