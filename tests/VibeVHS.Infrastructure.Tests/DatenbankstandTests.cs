using Microsoft.EntityFrameworkCore;
using VibeVHS.Infrastructure.Datenbank;

namespace VibeVHS.Infrastructure.Tests;

/// <summary>Vorabprüfung beim Ausrollen (NFA-27, deploy): passt die Datenbank zur neuen Version, oder braucht es einen Neuaufbau?</summary>
public sealed class DatenbankstandTests
{
    [Fact]
    public async Task Passende_Datenbank_ist_aktuell_abweichende_veraltet_mit_Exitcode_3()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var t = await TestDatenbank.ErzeugenAsync();
        await using var db = t.NeuerKontext();

        var aktuell = await Datenbankstand.HauptAsync("haupt:standard", db, ct);
        aktuell.Stand.ShouldBe(Standart.Aktuell);
        Datenbankstand.Exitcode([aktuell]).ShouldBe(0);

        await db.Database.ExecuteSqlRawAsync("UPDATE \"Schemainfo\" SET \"Version\" = 'ALT'", ct);
        var veraltet = await Datenbankstand.HauptAsync("haupt:standard", db, ct);
        veraltet.Stand.ShouldBe(Standart.Veraltet);
        veraltet.Ist.ShouldBe("ALT");
        veraltet.Hinweis.ShouldContain("-DbNeu");
        Datenbankstand.Exitcode([veraltet]).ShouldBe(3);
        Datenbankstand.Exitcode([veraltet with { Rolle = "kommentare" }]).ShouldBe(4);
        Datenbankstand.Exitcode([veraltet, veraltet with { Stand = Standart.KeineVerbindung }]).ShouldBe(5);
    }
}
