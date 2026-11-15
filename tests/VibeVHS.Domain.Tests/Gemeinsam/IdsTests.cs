using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Domain.Tests.Gemeinsam;

public class IdsTests
{
    [Fact]
    public void Neue_Ids_sind_Version_7_und_aufsteigend_sortierbar()
    {
        var ids = Enumerable.Range(0, 50).Select(_ => Ids.Neu()).ToList();

        ids.ShouldAllBe(id => id.Version == 7);
        ids.Distinct().Count().ShouldBe(50);
    }
}
