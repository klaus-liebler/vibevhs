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

// Tastenkürzel (QA-59): Strg+K (bzw. ⌘K) → Alles-Suche, F8 → Schnellauswahl (Schaltfläche data-schnellauswahl).
document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'k' || e.key === 'K')) {
        const suche = document.getElementById('vhs-allessuche');
        if (suche) { e.preventDefault(); suche.focus(); suche.select(); }
    } else if (e.key === 'F8' && !e.ctrlKey && !e.altKey) {
        const knopf = document.querySelector('[data-schnellauswahl]');
        if (knopf) { e.preventDefault(); knopf.click(); }
    }
});

// Rückfrage vor dem Absenden statischer Formulare (Portale): eigener Dialog statt confirm(), damit die Schaltfläche die Folge nennt.
document.addEventListener('submit', (e) => {
    const form = e.target;
    if (!form.matches || !form.matches('form[data-rueckfrage]') || form.dataset.bestaetigt === '1') { return; }
    e.preventDefault();
    const knopf = e.submitter || form.querySelector('[type=submit]');
    const ja = form.dataset.rueckfrageJa || (knopf && knopf.textContent.trim()) || 'Ja';
    const dlg = document.createElement('dialog');
    dlg.className = 'vhs-rueckfrage-dialog';
    dlg.setAttribute('aria-label', ja + '?');
    const text = document.createElement('p');
    text.textContent = form.dataset.rueckfrage;
    const zeile = document.createElement('div');
    zeile.className = 'vhs-row';
    const nein = document.createElement('button');
    nein.type = 'button'; nein.className = 'vhs-btn'; nein.textContent = 'Abbrechen';
    const ok = document.createElement('button');
    ok.type = 'button'; ok.className = 'vhs-btn vhs-btn-gefahr'; ok.textContent = ja;
    zeile.append(ok, nein);
    dlg.append(text, zeile);
    document.body.append(dlg);
    const schliessen = () => { dlg.close(); dlg.remove(); };
    nein.addEventListener('click', schliessen);
    dlg.addEventListener('cancel', () => dlg.remove());
    ok.addEventListener('click', () => {
        schliessen();
        form.dataset.bestaetigt = '1';
        if (form.requestSubmit) { form.requestSubmit(knopf || undefined); } else { form.submit(); }
        delete form.dataset.bestaetigt;
    });
    dlg.showModal();
    nein.focus();
}, true);

// Rücksprung zur Liste (Komponente ZurueckZurListe): Listen tragen data-liste="<schlüssel>", Zeilen data-zeile="<id>".
//   Klick auf eine Zeile merkt Zeile und Scrollposition (sessionStorage, nur in diesem Tab).
//   „← Zurück zur Liste“ oder Browser-Zurück → auf der Liste wird die Scrollposition wiederhergestellt, die Zeile
//   (bei virtuellen Listen, sobald sie nachgeladen ist) in den Blick gescrollt und hervorgehoben (Stylesheet-Regel, siehe markieren).
(() => {
    const speicher = (() => { try { return window.sessionStorage; } catch { return null; } })();
    if (!speicher) { return; }
    const lies = (k) => { try { return JSON.parse(speicher.getItem(k) || 'null'); } catch { return null; } };
    const schreib = (k, v) => { try { speicher.setItem(k, JSON.stringify(v)); } catch { /* voll oder gesperrt */ } };
    const scroller = (liste) => liste.matches('.vhs-vliste') ? liste : liste.querySelector('.vhs-vliste');

    document.addEventListener('click', (e) => {
        const zeile = e.target.closest && e.target.closest('[data-liste] [data-zeile]');
        if (zeile) {
            const liste = zeile.closest('[data-liste]');
            const s = scroller(liste);
            schreib('vhs-liste:' + liste.dataset.liste, { zeile: zeile.dataset.zeile, scroll: s ? s.scrollTop : null, y: window.scrollY });
        }
        const zurueck = e.target.closest && e.target.closest('a[data-ruecksprung]');
        if (zurueck) { schreib('vhs-rueckkehr', { liste: zurueck.dataset.ruecksprung, zeit: Date.now() }); }
    }, true);
    window.addEventListener('popstate', () => schreib('vhs-rueckkehr', { liste: '*', zeit: Date.now() }));

    // Hervorhebung als Stylesheet-Regel statt als Attribut: Blazor verwendet Zeilen virtueller Listen beim Nachladen wieder,
    // ein Attribut wanderte dann mit dem DOM-Element zu einer anderen Person. Constructable Stylesheets sind CSP-konform.
    const blatt = new CSSStyleSheet();
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, blatt];
    const markieren = (liste, zeile) => blatt.replaceSync(
        `[data-liste="${CSS.escape(liste)}"] [data-zeile="${CSS.escape(zeile)}"] { outline: 2px solid var(--vhs-accent); outline-offset: -2px; ` +
        'background: color-mix(in srgb, var(--vhs-accent) 10%, transparent); }');
    window.vhsZuletzt = () => blatt.cssRules.length ? blatt.cssRules[0].selectorText : null;

    let schritt = 0;
    setInterval(() => {
        const r = lies('vhs-rueckkehr');
        if (!r) { schritt = 0; return; }
        if (Date.now() - r.zeit > 8000) { speicher.removeItem('vhs-rueckkehr'); return; }
        const liste = document.querySelector(r.liste === '*' ? '[data-liste]' : `[data-liste="${CSS.escape(r.liste)}"]`);
        if (!liste) { return; }
        const merk = lies('vhs-liste:' + liste.dataset.liste);
        if (!merk) { speicher.removeItem('vhs-rueckkehr'); return; }
        const s = scroller(liste);
        if (schritt++ === 0) {
            if (s && merk.scroll != null) { s.scrollTop = merk.scroll; }
            if (merk.y) { window.scrollTo(0, merk.y); }
        }
        const zeile = liste.querySelector(`[data-zeile="${CSS.escape(merk.zeile)}"]`);
        if (!zeile) { return; } // virtuelle Liste lädt noch nach
        markieren(liste.dataset.liste, merk.zeile);
        zeile.scrollIntoView({ block: 'nearest' });
        speicher.removeItem('vhs-rueckkehr');
        schritt = 0;
    }, 150);
})();
