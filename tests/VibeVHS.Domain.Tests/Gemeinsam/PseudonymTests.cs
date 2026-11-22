using VibeVHS.Domain.Datenschutz;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class PseudonymTests
{
    [Fact]
    public void Pseudonyme_unterscheiden_sich_auch_bei_Ids_aus_derselben_Millisekunde()
    {
        // UUIDv7: Der Anfang ist der Zeitstempel. Früher bestand das Pseudonym nur daraus – alle Personen eines Imports hießen gleich.
        var ids = Enumerable.Range(0, 200).Select(_ => Ids.Neu()).ToList();
        ids.Select(id => id.ToString("N")[..8]).Distinct().Count().ShouldBeLessThanOrEqualTo(2); // das alte Pseudonym: fast immer nur eins

        var pseudonyme = ids.Select(Pseudonym.Kurz).ToList();

        pseudonyme.Distinct().Count().ShouldBe(200);
        pseudonyme.ShouldAllBe(p => p.Length == 8 && p.All(c => char.IsAsciiHexDigitUpper(c) || char.IsAsciiDigit(c)));
        Pseudonym.Person(ids[0]).ShouldBe($"Person #{pseudonyme[0]}");
    }
}
