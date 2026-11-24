using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class KorrekturregelTests
{
    private static readonly DateTimeOffset Eingetragen = new(2026, 11, 24, 18, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Eigener_Eintrag_30_Minuten_ohne_Grund()
    {
        Korrekturregel.Pruefen(true, Eingetragen, Eingetragen.AddMinutes(30), null).ShouldBeNull();
        Korrekturregel.Pruefen(true, Eingetragen, Eingetragen.AddMinutes(31), null)!.ShouldContain("nach 30 Minuten");
        Korrekturregel.Pruefen(true, Eingetragen, Eingetragen.AddMinutes(31), "Tippfehler").ShouldBeNull();
    }

    [Fact]
    public void Fremder_Eintrag_immer_mit_Grund()
    {
        Korrekturregel.Pruefen(false, Eingetragen, Eingetragen.AddMinutes(1), " ")!.ShouldContain("fremden Eintrags");
        Korrekturregel.Pruefen(false, Eingetragen, Eingetragen.AddMinutes(1), "Rückmeldung der Kursleitung").ShouldBeNull();
    }
}
