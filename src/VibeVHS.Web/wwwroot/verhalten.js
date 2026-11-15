// Verhalten statt Inline-Handlern (Content-Security-Policy ohne 'unsafe-inline'): Ereignisse werden am Dokument abgefangen,
// funktionieren also auch für Elemente, die Blazor später rendert.
//   data-auto-absenden     Auswahl ändert sich → Formular absenden (Rollenwechsel, Sprache)
//   data-markieren         Klick markiert den Inhalt (z. B. Kalenderadresse zum Kopieren)
//   data-selbst-checkin    "leser,feld,formular,knopf" → Kamera für den Selbst-Check-in starten
document.addEventListener('change', (e) => {
    const el = e.target.closest && e.target.closest('[data-auto-absenden]');
    if (el && el.form) { el.form.submit(); }
});
document.addEventListener('click', (e) => {
    const markieren = e.target.closest && e.target.closest('[data-markieren]');
    if (markieren && typeof markieren.select === 'function') { markieren.select(); }
    const checkin = e.target.closest && e.target.closest('[data-selbst-checkin]');
    if (checkin && typeof window.vhsSelbstCheckin === 'function') {
        const [leser, feld, formular, knopf] = checkin.dataset.selbstCheckin.split(',');
        window.vhsSelbstCheckin(leser, feld, formular, knopf);
    }
});
