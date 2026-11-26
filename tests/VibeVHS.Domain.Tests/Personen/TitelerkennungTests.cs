using VibeVHS.Domain.Personen;

namespace VibeVHS.Domain.Tests.Personen;

/// <summary>Akademische Grade im Vor- oder Nachnamen werden erkannt und abgetrennt; echte Namensteile bleiben unangetastet.</summary>
public class TitelerkennungTests
{
    [Theory]
    [InlineData("Dr. Kai", "Lüdeking", "Dr.", "Kai", "Lüdeking")]
    [InlineData("Prof. Dr. Anna", "Berg", "Prof. Dr.", "Anna", "Berg")]
    [InlineData("Dipl.-Ing. Jens", "Ahlers", "Dipl.-Ing.", "Jens", "Ahlers")]
    [InlineData("Kai", "Dr. Lüdeking", "Dr.", "Kai", "Lüdeking")]
    [InlineData("Dr. med. Eva", "Kranz", "Dr. med.", "Eva", "Kranz")]
    [InlineData("Mag. Lisa", "Huber", "Mag.", "Lisa", "Huber")]
    [InlineData("J. R.", "Tolkien", null, "J. R.", "Tolkien")]
    [InlineData("Anna", "St. John", null, "Anna", "St. John")]
    [InlineData("Drita", "Dr.", null, "Drita", "Dr.")] // „Dr.“ allein ist hier der (seltsame) Nachname – nichts abtrennen
    public void Abtrennen(string vorname, string nachname, string? titel, string v, string n)
        => Titelerkennung.Abtrennen(vorname, nachname).ShouldBe((titel, v, n));

    [Fact]
    public void Zusammenfuehren_ohne_Doppelung()
    {
        Titelerkennung.Zusammen("Prof.", "Dr.").ShouldBe("Prof. Dr.");
        Titelerkennung.Zusammen("Dr.", "Dr.").ShouldBe("Dr.");
        Titelerkennung.Zusammen(null, null).ShouldBeNull();
    }
}
