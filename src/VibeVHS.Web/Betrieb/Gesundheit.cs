namespace VibeVHS.Web.Betrieb;

/// <summary>Healthcheck-Aufruf für den Container: fragt den eigenen Endpunkt /gesund ab (Exitcode 0 = gesund).</summary>
internal static class Gesundheit
{
    public static async Task<int> PruefenAsync(string adresse)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var antwort = await client.GetAsync(new Uri(adresse));
            return antwort.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"Nicht gesund: {ex.Message}");
            return 1;
        }
    }
}
