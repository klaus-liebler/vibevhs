using Radzen;

namespace VibeVHS.Web;

/// <summary>
/// Einheitliche Rückfragen (QA-58, aus SchoolCraft FA-3230): Löschen, Entfernen, Stornieren, Sperren, Widerrufen und alles, was sich nicht
/// mit einem Klick rückgängig machen lässt, erst nach ausdrücklicher Bestätigung – mit dem Namen dessen, was betroffen ist, der Folge und
/// einer Schaltfläche, die die Folge nennt („Löschen“, nicht „OK“). Klick neben den Dialog schließt ihn nicht; <kbd>Esc</kbd> bricht ab.
/// Statische Seiten (Portale) nutzen dafür <c>data-rueckfrage</c> in <c>verhalten.js</c>.
/// </summary>
public static class Rueckfragen
{
    public static async Task<bool> BestaetigenAsync(this DialogService dialoge, string frage, string titel, string ja, string nein = "Abbrechen") =>
        await dialoge.Confirm(frage, titel, new ConfirmOptions
        {
            OkButtonText = ja,
            CancelButtonText = nein,
            CloseDialogOnOverlayClick = false,
            CloseDialogOnEsc = true,
            Width = "480px",
            CssClass = "vhs-rueckfrage",
        }) == true;

    /// <summary>„‚Was‘ löschen?“ mit der Folge, z. B. „Das lässt sich nicht rückgängig machen.“</summary>
    public static Task<bool> LoeschenAsync(this DialogService dialoge, string was, string? folge = null) =>
        dialoge.BestaetigenAsync($"{was} wird gelöscht.{(folge is null ? "" : " " + folge)}", $"{was} löschen?", "Löschen");

    /// <summary>Schaltflächentext ohne Auslassungspunkte („Absagen …“ → „Absagen“) als Text der bestätigenden Schaltfläche.</summary>
    public static string Folge(string text) => text.Replace("…", "", StringComparison.Ordinal).Trim();
}
