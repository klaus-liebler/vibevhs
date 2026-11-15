namespace VibeVHS.Application.Postausgang;

/// <summary>
/// Übersetzungslinks zeigen auf eine Seite der VHS, die den Nachrichtentext in die gewünschte Sprache übersetzt.
/// Der Link enthält nur die Nachrichten-ID, keine personenbezogenen Daten.
/// </summary>
public sealed class Uebersetzungslinks(Uri basisAdresse) : IUebersetzungslinks
{
    public Uri FuerNachricht(Guid nachrichtId, string sprache)
    {
        var basis = basisAdresse.ToString().TrimEnd('/');
        var sp = Uri.EscapeDataString(string.IsNullOrWhiteSpace(sprache) ? "de" : sprache);
        return new Uri($"{basis}/uebersetzen/{nachrichtId:N}?sprache={sp}");
    }
}

/// <summary>Textbausteine, die jede Kundenmail bekommt.</summary>
public static class MailBausteine
{
    /// <summary>Hinweiszeile "Diese Nachricht in Ihrer Sprache lesen" in der Wunschsprache und auf Deutsch.</summary>
    public static string Uebersetzungshinweis(string sprache, Uri link)
    {
        var eigene = Hinweise.GetValueOrDefault(sprache);
        var zeile = eigene is null || sprache == "de"
            ? "Diese Nachricht in Ihrer Sprache lesen / Translate this message into your language:"
            : $"{eigene} / Diese Nachricht in Ihrer Sprache lesen:";
        return $"➜ {zeile}{Environment.NewLine}  {link}";
    }

    private static readonly Dictionary<string, string> Hinweise = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "Read this message in English",
        ["tr"] = "Bu mesajı Türkçe okuyun",
        ["uk"] = "Прочитати це повідомлення українською",
        ["ar"] = "اقرأ هذه الرسالة بالعربية",
        ["ru"] = "Прочитать это сообщение на русском",
        ["pl"] = "Przeczytaj tę wiadomość po polsku",
        ["es"] = "Leer este mensaje en español",
        ["fa"] = "این پیام را به فارسی بخوانید",
        ["ro"] = "Citiți acest mesaj în limba română",
    };
}
