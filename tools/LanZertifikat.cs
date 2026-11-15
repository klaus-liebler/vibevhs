// Erzeugt ein selbst signiertes HTTPS-Zertifikat für den Handy-Test im lokalen Netz (Profil "lan").
// Aufruf:  dotnet run tools/LanZertifikat.cs
// Ablage außerhalb des Repos:  %USERPROFILE%\.vibevhs\lan\vibevhs-lan.pfx  und  passwort.txt
// Das Zertifikat enthält die aktuellen LAN-Adressen (Schnittstellen mit Gateway), localhost und den Rechnernamen.
// Ändert sich die LAN-IP (DHCP), einfach erneut aufrufen. Es wird nichts im Windows-Zertifikatspeicher eingetragen.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var ordner = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vibevhs", "lan");
Directory.CreateDirectory(ordner);

var adressen = NetworkInterface.GetAllNetworkInterfaces()
    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
    .Select(n => n.GetIPProperties())
    .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
    .SelectMany(p => p.UnicastAddresses.Select(u => u.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork))
    .Distinct()
    .ToList();

using var rsa = RSA.Create(2048);
var anfrage = new CertificateRequest("CN=VibeVHS LAN-Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder();
san.AddDnsName("localhost");
san.AddDnsName(Environment.MachineName);
san.AddIpAddress(IPAddress.Loopback);
foreach (var a in adressen)
{
    san.AddIpAddress(a);
}

anfrage.CertificateExtensions.Add(san.Build());
anfrage.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
anfrage.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
anfrage.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));

using var zertifikat = anfrage.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(365));
var passwort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
File.WriteAllBytes(Path.Combine(ordner, "vibevhs-lan.pfx"), zertifikat.Export(X509ContentType.Pkcs12, passwort));
File.WriteAllText(Path.Combine(ordner, "passwort.txt"), passwort);

Console.WriteLine($"Zertifikat erstellt: {Path.Combine(ordner, "vibevhs-lan.pfx")}");
Console.WriteLine($"Gültig bis {zertifikat.NotAfter:dd.MM.yyyy} für: localhost, {Environment.MachineName}, {string.Join(", ", adressen)}");
Console.WriteLine($"SHA-256-Fingerabdruck: {zertifikat.GetCertHashString(HashAlgorithmName.SHA256)}");
