namespace VibeVHS.Infrastructure.Tests;

public class DatenbankverbindungTests
{
    [Fact]
    public void Absoluter_Pfad_bleibt_erhalten()
    {
        var v = Registrierung.Datenbankverbindung("Data Source=/var/lib/vibevhs/vibevhs.db", "/opt/vibevhs");
        v.DataSource.ShouldBe("/var/lib/vibevhs/vibevhs.db");
    }

    [Fact]
    public void Fehlender_Wert_nimmt_Standard_relativ_zum_Basisverzeichnis()
    {
        var basis = Path.GetTempPath();
        var v = Registrierung.Datenbankverbindung(null, basis);
        v.DataSource.ShouldBe(Path.GetFullPath(Path.Combine(basis, "data/vibevhs.db")));
    }

    [Fact]
    public void Von_systemd_abgeschnittener_Wert_bricht_mit_verstaendlicher_Meldung_ab()
    {
        // Environment=ConnectionStrings__VibeVHS=Data Source=… ohne Anführungszeichen → Wert „Data“
        var ex = Should.Throw<InvalidOperationException>(() => Registrierung.Datenbankverbindung("Data", "/opt/vibevhs"));
        ex.Message.ShouldContain("ConnectionStrings:VibeVHS");
        ex.Message.ShouldContain("Anführungszeichen");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void Wert_ohne_Datei_bricht_ab()
    {
        Should.Throw<InvalidOperationException>(() => Registrierung.Datenbankverbindung("Cache=Shared", "/opt/vibevhs"))
            .Message.ShouldContain("Data Source fehlt");
    }
}
