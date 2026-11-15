using VibeVHS.Application.Dokumente;

namespace VibeVHS.Adapters.Mocks;

/// <summary>
/// Platzhalter für einen Übersetzungsdienst (später z. B. ein kommunal freigegebener Dienst). Zeigt den Originaltext mit einem
/// Hinweis in der Zielsprache; es werden keine Daten nach außen gegeben.
/// </summary>
public sealed class MockUebersetzer : IUebersetzer
{
    private static readonly Dictionary<string, string> Hinweise = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "Originaltext (Deutsch)",
        ["en"] = "[English translation – placeholder; the translation service will be connected later]",
        ["tr"] = "[Türkçe çeviri – yer tutucu; çeviri hizmeti daha sonra bağlanacak]",
        ["uk"] = "[Переклад українською – заповнювач; службу перекладу буде підключено пізніше]",
        ["ar"] = "[ترجمة عربية – نص مؤقت؛ سيتم ربط خدمة الترجمة لاحقًا]",
        ["ru"] = "[Перевод на русский – заглушка; сервис перевода будет подключён позже]",
        ["pl"] = "[Tłumaczenie na polski – symbol zastępczy; usługa tłumaczenia zostanie podłączona później]",
        ["fa"] = "[ترجمه فارسی – متن موقت؛ سرویس ترجمه بعداً متصل می‌شود]",
        ["es"] = "[Traducción al español – marcador; el servicio de traducción se conectará más adelante]",
        ["ro"] = "[Traducere în română – substituent; serviciul de traducere va fi conectat ulterior]",
    };

    public string Uebersetzen(string text, string sprache)
        => string.Equals(sprache, "de", StringComparison.OrdinalIgnoreCase)
            ? text
            : $"{Hinweise.GetValueOrDefault(sprache) ?? $"[{sprache}: translation placeholder]"}\n\n{text}";
}
