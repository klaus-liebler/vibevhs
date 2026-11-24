namespace VibeVHS.Infrastructure.Tests;

public class DatenbankverbindungTests
{
    [Fact]
    public void Gueltige_Verbindung_wird_gelesen()
    {
        var v = Registrierung.Datenbankverbindung("Host=127.0.0.1;Port=5432;Database=vibevhs;Username=vibevhs;Password=geheim");
        v.Host.ShouldBe("127.0.0.1");
        v.Database.ShouldBe("vibevhs");
    }

    [Fact]
    public void Fehlender_Wert_bricht_mit_Hinweis_auf_den_Entwicklungscontainer_ab()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Registrierung.Datenbankverbindung(null));
        ex.Message.ShouldContain("ConnectionStrings:VibeVHS");
        ex.Message.ShouldContain("docker compose");
    }

    [Fact]
    public void Von_systemd_abgeschnittener_Wert_bricht_mit_verstaendlicher_Meldung_ab()
    {
        // Environment=ConnectionStrings__VibeVHS=Host=… ohne Anführungszeichen → systemd trennt am Leerzeichen
        var ex = Should.Throw<InvalidOperationException>(() => Registrierung.Datenbankverbindung("Host"));
        ex.Message.ShouldContain("ConnectionStrings:VibeVHS");
        ex.Message.ShouldContain("Anführungszeichen");
        ex.InnerException.ShouldBeOfType<ArgumentException>();
    }

    [Fact]
    public void Wert_ohne_Datenbank_bricht_ab_und_zeigt_kein_Passwort()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Registrierung.Datenbankverbindung("Host=db;Password=geheim"));
        ex.Message.ShouldContain("keine Datenbank");
        ex.Message.ShouldNotContain("geheim");
    }

    [Fact]
    public void Kommentare_liegen_standardmaessig_in_eigener_Datenbank_daneben()
    {
        var konfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        new Npgsql.NpgsqlConnectionStringBuilder(Registrierung.KommentarVerbindung(konfiguration, "Host=db;Database=vibevhs;Username=u")).Database
            .ShouldBe("vibevhs_kommentare");
    }
}
