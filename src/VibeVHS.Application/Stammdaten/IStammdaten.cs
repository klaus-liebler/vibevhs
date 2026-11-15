using VibeVHS.Domain.Stammdaten;

namespace VibeVHS.Application.Stammdaten;

/// <summary>Lesezugriff auf die Stammdaten der VHS (Briefkopf, Fachbereiche).</summary>
public interface IStammdaten
{
    Task<Einrichtung?> EinrichtungAsync(CancellationToken abbruch = default);

    Task<IReadOnlyList<Fachbereich>> FachbereicheAsync(CancellationToken abbruch = default);
}
