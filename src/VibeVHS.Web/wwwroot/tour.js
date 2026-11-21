// Abnahme- und Schulungsmodus: geführte Touren mit Sprechblasen direkt an der Oberfläche.
//
// Touren stehen in touren/abnahme.json. Ein Schritt zeigt auf ein Element (über sichtbaren Text, Feldbeschriftung oder CSS),
// erklärt, was zu tun ist, und geht weiter, sobald die Person es getan hat:
//   weiter: "klick"   – Klick auf das Ziel (oder etwas darin)
//           "eingabe" – Wert im Feld eingetragen (mit "wert": enthält diesen Text; sonst: nicht leer und geändert)
//           "seite"   – Adresse passt zu "bisSeite" (Präfix, relativ zur Basis)
//           "knopf"   – Person klickt in der Sprechblase auf „Weiter“ (Erklärschritte)
// Weitere Angaben: "seite" (wo der Schritt spielt; sonst Hinweis mit Link), "ueberspringenWennAngemeldet" (Kontoname),
// "ziel": { "text": "…", "feld": "…", "css": "…", "in": "<css-Bereich>", "genau": true }.
// Zustand je Tab in sessionStorage (übersteht Seitenwechsel und Neuladen). Kein Inline-Code (CSP); Start über data-tour-start.
(() => {
  const SCHLUESSEL = 'vhs-tour';
  const speicher = (() => { try { return window.sessionStorage; } catch { return null; } })();
  const lies = () => { try { return JSON.parse(speicher?.getItem(SCHLUESSEL) || 'null'); } catch { return null; } };
  const schreib = (z) => { try { z ? speicher?.setItem(SCHLUESSEL, JSON.stringify(z)) : speicher?.removeItem(SCHLUESSEL); } catch { /* gesperrt */ } };

  const basis = () => new URL(document.baseURI).pathname;
  const pfad = () => { const b = basis(); const p = location.pathname; return (p.startsWith(b) ? p.slice(b.length) : p.replace(/^\//, '')) + location.search; };
  // Seite passt: Präfix der Adresse; "" heißt genau die Startseite
  const passtSeite = (seite) => (seite === '' ? pfad().split('?')[0] === '' : pfad().startsWith(seite));
  const norm = (t) => (t || '').replace(/[\s ]+/g, ' ').replace(/\s*\*\s*$/, '').trim().toLowerCase();
  // Sichtbar: im Layout, nicht versteckt und nicht in einem zugeklappten <details> (dessen Inhalt meldet sonst Maße)
  const sichtbar = (el) => {
    if (!el || !el.isConnected) return false;
    const zu = el.closest('details:not([open])');
    if (zu && !(el.tagName === 'SUMMARY' && el.parentElement === zu) && !zu.querySelector(':scope > summary')?.contains(el)) return false;
    if (typeof el.checkVisibility === 'function' && !el.checkVisibility({ visibilityProperty: true })) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 || r.height > 0;
  };
  // Sichtbarer Text ohne Icon-Namen (Material-Icons stehen als Text im Knopf, z. B. „person_add“)
  const reinerText = (el) => {
    let t = '';
    const w = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    for (let n = w.nextNode(); n; n = w.nextNode()) { if (!n.parentElement.closest('.rzi, .notranslate, .material-icons, .material-symbols-outlined')) t += n.textContent; }
    return t;
  };
  const esc = (t) => String(t).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  // **fett** und `Code` in Sprechblasen (Text wird vorher maskiert)
  const formatiert = (t) => esc(t).replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/`(.+?)`/g, '<code>$1</code>');

  let touren = null;
  // Jede Tour beginnt mit dem passenden Testkonto: „Konto wechseln“ → Konto wählen (entfällt, wenn es schon angemeldet ist)
  const kontoSchritte = (t) => !t.kontoName ? [] : [
    { text: `Diese Tour ist für **${t.konto}**. Klicken Sie oben rechts auf **Konto wechseln**.`, ziel: { text: 'Konto wechseln', genau: true }, weiter: 'klick',
      ueberspringenWennAngemeldet: t.kontoName, ueberspringenAufSeite: 'anmelden' },
    { seite: 'anmelden', text: `Wählen Sie **${t.kontoName}** und klicken Sie in dieser Zeile auf **Anmelden** (Testanmeldung ohne Passwort).`,
      ziel: { text: 'Anmelden', inText: t.kontoName, genau: true }, weiter: 'klick', ueberspringenWennAngemeldet: t.kontoName },
  ];
  const ladeTouren = async () => {
    if (touren) return touren;
    try {
      const r = await fetch(new URL('touren/abnahme.json', document.baseURI), { cache: 'no-cache' });
      touren = ((await r.json()).touren || []).map((t) => ({ ...t, schritte: [...kontoSchritte(t), ...t.schritte] }));
    } catch { touren = []; }
    return touren;
  };

  // ---------- Ziel finden ----------
  const KLICKBAR = 'button, a[href], [role="button"], [role="tab"], [role="menuitem"], [role="radio"], [role="checkbox"], [role="option"], summary, label, .rz-button, .vhs-chip, '
    + 'input[type="submit"], input[type="button"], .rz-dropdown, .rz-tabview-nav li, [style*="cursor:pointer"], [style*="cursor: pointer"]';
  const textVon = (el) => norm(reinerText(el) || el.value || el.getAttribute('aria-label') || el.getAttribute('title'));

  const findeText = (bereich, gesucht, genau) => {
    const g = norm(gesucht);
    const kandidaten = [...bereich.querySelectorAll(KLICKBAR)].filter((el) => sichtbar(el) && !el.closest('#vhs-tour-blase'));
    const exakt = kandidaten.filter((el) => textVon(el) === g);
    if (exakt.length || genau) return innerstes(exakt);
    const anfang = kandidaten.filter((el) => textVon(el).startsWith(g));
    if (anfang.length) return innerstes(anfang);
    return innerstes(kandidaten.filter((el) => textVon(el).includes(g)));
  };
  // Bei verschachtelten Treffern (li > a) das innerste Element nehmen
  const innerstes = (liste) => liste.find((el) => !liste.some((x) => x !== el && el.contains(x))) || null;

  const EINGABE = 'input:not([type="hidden"]), select, textarea, .rz-dropdown, .rz-datepicker, .rz-numeric';
  const findeFeld = (bereich, gesucht) => {
    const g = norm(gesucht);
    for (const label of bereich.querySelectorAll('label, .rz-form-field-label, legend')) {
      if (!sichtbar(label) || !norm(reinerText(label)).startsWith(g)) continue;
      const fuer = label.getAttribute('for');
      const el = (fuer && document.getElementById(fuer)) || label.querySelector(EINGABE)
        || label.closest('.rz-form-field, .vhs-feld, fieldset, div')?.querySelector(EINGABE);
      if (el && sichtbar(el.closest('.rz-form-field, .rz-dropdown, .rz-datepicker, .rz-numeric') || el)) return el.closest('.rz-dropdown, .rz-datepicker, .rz-numeric') || el;
    }
    return [...bereich.querySelectorAll(EINGABE)].find((el) => sichtbar(el)
      && [el.getAttribute('aria-label'), el.getAttribute('placeholder'), el.getAttribute('name')].some((a) => norm(a).startsWith(g))) || null;
  };

  // Bereich: CSS ("in") und/oder die innerste sichtbare Zeile/Karte, die einen Text enthält ("inText", z. B. ein Name)
  const ZEILEN = '.vhs-list > div, tr, li, .vhs-card, section, details, form';
  const findeBereich = (ziel) => {
    let kandidaten = ziel.in ? [...document.querySelectorAll(ziel.in)].filter(sichtbar) : [document.body];
    if (ziel.inText) {
      const g = norm(ziel.inText);
      const zeilen = kandidaten.flatMap((k) => [...k.querySelectorAll(ZEILEN)]).filter((z) => sichtbar(z) && norm(reinerText(z)).includes(g));
      kandidaten = zeilen.filter((z) => !zeilen.some((x) => x !== z && z.contains(x)));
    }
    return kandidaten[0] || null;
  };

  const findeZiel = (ziel) => {
    if (!ziel) return null;
    const bereich = findeBereich(ziel);
    if (!bereich) return null;
    if (ziel.css) {
      const treffer = [...bereich.querySelectorAll(ziel.css)].filter(sichtbar);
      return (ziel.text ? treffer.find((el) => textVon(el).startsWith(norm(ziel.text))) : treffer[0]) || null;
    }
    if (ziel.feld) return findeFeld(bereich, ziel.feld);
    if (ziel.text) return findeText(bereich, ziel.text, ziel.genau);
    return null;
  };

  const wertVon = (el) => {
    if (!el) return '';
    if ('value' in el && typeof el.value === 'string' && el.tagName !== 'BUTTON') return el.value;
    const innen = el.querySelector && el.querySelector('input:not([type="hidden"])');
    if (innen) return innen.value;
    return (el.querySelector && el.querySelector('.rz-dropdown-label, .rz-inputtext')?.textContent) || el.textContent || '';
  };
  const vergleich = (t) => norm(t).replace(/[\s.\-/]/g, '');

  // ---------- Anzeige ----------
  let ring, blase, aktuell = null, zielEl = null, startwert = null, suchtSeit = 0, weiterGeplant = false;

  const bauen = () => {
    if (blase?.isConnected) return;
    ring = document.createElement('div');
    ring.id = 'vhs-tour-ring';
    ring.setAttribute('aria-hidden', 'true');
    blase = document.createElement('div');
    blase.id = 'vhs-tour-blase';
    blase.setAttribute('role', 'dialog');
    blase.setAttribute('aria-live', 'polite');
    blase.setAttribute('aria-label', 'Geführte Tour');
    document.body.append(ring, blase);
    blase.addEventListener('click', (e) => {
      const k = e.target.closest('[data-tour-aktion]');
      if (!k) return;
      e.preventDefault();
      const a = k.dataset.tourAktion;
      if (a === 'weiter') gehe(+1);
      else if (a === 'zurueck') gehe(-1);
      else if (a === 'beenden') beenden();
      else if (a === 'oeffnen') { location.href = new URL(k.dataset.ziel, document.baseURI).href; }
    });
  };

  const abbauen = () => { ring?.remove(); blase?.remove(); ring = blase = null; zielEl = null; };

  const zeit = (z) => { const s = Math.max(0, Math.round((Date.now() - z.start) / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; };

  const zeichnen = (tour, z, schritt, el, hinweis) => {
    bauen();
    const letzter = z.i >= tour.schritte.length - 1;
    const kopf = `<div class="vhs-tour-kopf"><span>Tour: ${esc(tour.titel)}</span><span>Schritt ${z.i + 1}/${tour.schritte.length} · <span data-tour-zeit>${zeit(z)}</span></span></div>`;
    const tun = schritt.weiter === 'klick' ? '<div class="vhs-tour-tun">→ Klicken Sie auf die markierte Stelle.</div>'
      : schritt.weiter === 'eingabe' ? `<div class="vhs-tour-tun">→ ${schritt.wert ? `Tragen Sie <b>${esc(schritt.wert)}</b> ein.` : 'Füllen Sie das markierte Feld aus.'}</div>` : '';
    const hin = hinweis ? `<div class="vhs-tour-hinweis">${hinweis}</div>` : '';
    const knoepfe = `<div class="vhs-tour-knoepfe">
        <button type="button" data-tour-aktion="zurueck" ${z.i === 0 ? 'disabled' : ''}>Zurück</button>
        <button type="button" data-tour-aktion="beenden">Beenden</button>
        <button type="button" data-tour-aktion="weiter" class="haupt">${letzter ? 'Tour abschließen' : schritt.weiter === 'knopf' ? 'Weiter' : 'Überspringen'}</button>
      </div>`;
    const inhalt = kopf + `<div class="vhs-tour-text">${formatiert(schritt.text)}</div>` + tun + hin + knoepfe;
    if (blase.dataset.inhalt !== inhalt) { blase.innerHTML = inhalt; blase.dataset.inhalt = inhalt; }
    platzieren(el);
  };

  const platzieren = (el) => {
    if (!blase) return;
    const b = blase.getBoundingClientRect();
    const vw = window.innerWidth, vh = window.innerHeight, rand = 12;
    if (!el) {
      ring.style.display = 'none';
      blase.dataset.lage = 'frei';
      blase.style.left = `${Math.max(rand, vw - b.width - 20)}px`;
      blase.style.top = `${Math.max(rand, vh - b.height - 20)}px`;
      return;
    }
    const r = el.getBoundingClientRect();
    ring.style.display = 'block';
    Object.assign(ring.style, { left: `${r.left - 4}px`, top: `${r.top - 4}px`, width: `${r.width + 8}px`, height: `${r.height + 8}px` });
    let lage = 'unten', top = r.bottom + 14;
    if (top + b.height > vh - rand && r.top - b.height - 14 > rand) { lage = 'oben'; top = r.top - b.height - 14; }
    top = Math.min(Math.max(rand, top), Math.max(rand, vh - b.height - rand));
    const left = Math.min(Math.max(rand, r.left + r.width / 2 - b.width / 2), Math.max(rand, vw - b.width - rand));
    blase.dataset.lage = lage;
    blase.style.left = `${left}px`;
    blase.style.top = `${top}px`;
    blase.style.setProperty('--pfeil', `${Math.min(Math.max(16, r.left + r.width / 2 - left), b.width - 16)}px`);
  };

  // ---------- Ablauf ----------
  const angemeldetAls = () => norm(document.querySelector('.vhs-role b')?.textContent);

  const gehe = (richtung) => {
    const z = lies();
    if (!z || !aktuell) return;
    z.i += richtung;
    if (z.i >= aktuell.schritte.length) { beenden(true); return; }
    z.i = Math.max(0, z.i);
    schreib(z);
    zielEl = null; startwert = null; suchtSeit = Date.now(); weiterGeplant = false;
    takt();
  };

  const beenden = (fertig) => {
    const z = lies();
    schreib(null);
    abbauen();
    if (fertig && z && aktuell) {
      bauen();
      blase.innerHTML = `<div class="vhs-tour-kopf"><span>Tour: ${esc(aktuell.titel)}</span><span>${zeit(z)}</span></div>
        <div class="vhs-tour-text"><b>Geschafft.</b> ${formatiert(aktuell.abschluss || 'Alle Schritte erledigt.')}</div>
        <div class="vhs-tour-knoepfe"><button type="button" data-tour-aktion="oeffnen" data-ziel="hilfe#touren">Weitere Touren</button>
        <button type="button" data-tour-aktion="beenden" class="haupt">Schließen</button></div>`;
      platzieren(null);
    }
    aktuell = null;
  };

  const takt = async () => {
    const z = lies();
    if (!z) { if (aktuell) { aktuell = null; abbauen(); } return; }
    const liste = await ladeTouren();
    aktuell = liste.find((t) => t.id === z.id) || null;
    if (!aktuell) { schreib(null); abbauen(); return; }
    const schritt = aktuell.schritte[z.i];
    if (!schritt) { beenden(true); return; }

    if (schritt.ueberspringenWennAngemeldet && angemeldetAls() === norm(schritt.ueberspringenWennAngemeldet)) { gehe(+1); return; }
    if (schritt.ueberspringenAufSeite != null && passtSeite(schritt.ueberspringenAufSeite)) { gehe(+1); return; }
    if (schritt.weiter === 'seite' && schritt.bisSeite != null && passtSeite(schritt.bisSeite)) { gehe(+1); return; }

    const el = findeZiel(schritt.ziel);
    if (el !== zielEl) {
      zielEl = el;
      startwert = el ? wertVon(el) : null;
      if (el) el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }

    let hinweis = '';
    if (schritt.ziel && !el && Date.now() - suchtSeit > 1500) {
      const falscheSeite = schritt.seite != null && !passtSeite(schritt.seite);
      hinweis = falscheSeite
        ? `Dieser Schritt spielt auf einer anderen Seite. <button type="button" data-tour-aktion="oeffnen" data-ziel="${esc(schritt.seite)}">Seite öffnen</button>`
        : 'Die markierte Stelle ist gerade nicht zu sehen – vielleicht ist ein Bereich zugeklappt oder die Seite lädt noch. Sie können den Schritt auch überspringen.';
    }

    if (schritt.weiter === 'eingabe' && el && !weiterGeplant) {
      const w = wertVon(el);
      const passt = schritt.wert ? vergleich(w).includes(vergleich(schritt.wert)) : (norm(w) !== '' && w !== startwert);
      if (passt && document.activeElement !== el && !el.contains(document.activeElement)) { weiterGeplant = true; setTimeout(() => gehe(+1), 250); }
    }
    zeichnen(aktuell, z, schritt, el, hinweis);
    const uhr = blase?.querySelector('[data-tour-zeit]');
    if (uhr) uhr.textContent = zeit(z);
  };

  // Klick auf das Ziel → nächster Schritt (Erfassung vor Blazor, damit auch Navigation den Stand mitnimmt)
  document.addEventListener('click', (e) => {
    const start = e.target.closest && e.target.closest('[data-tour-start]');
    if (start) { e.preventDefault(); starten(start.dataset.tourStart); return; }
    const z = lies();
    if (!z || !aktuell || !zielEl) return;
    const schritt = aktuell.schritte[z.i];
    if (schritt?.weiter === 'klick' && (zielEl === e.target || zielEl.contains(e.target))) {
      z.i += 1; schreib(z); zielEl = null; suchtSeit = Date.now(); weiterGeplant = false;
      setTimeout(takt, 350);
    }
  }, true);
  // Eingaben: beim Verlassen des Felds prüfen (Wert vollständig)
  document.addEventListener('focusout', () => setTimeout(takt, 50), true);
  document.addEventListener('change', () => setTimeout(takt, 50), true);
  window.addEventListener('resize', () => platzieren(zielEl));
  window.addEventListener('scroll', () => platzieren(zielEl), true);

  const starten = async (id) => {
    const liste = await ladeTouren();
    const tour = liste.find((t) => t.id === id);
    if (!tour) return;
    schreib({ id, i: 0, start: Date.now() });
    aktuell = tour; suchtSeit = Date.now();
    if (tour.start != null && !passtSeite(tour.start)) { location.href = new URL(tour.start, document.baseURI).href; return; }
    takt();
  };

  // Tourenliste auf der Hilfeseite
  const listeZeichnen = async () => {
    const box = document.querySelector('[data-touren-liste]');
    if (!box || box.dataset.gezeichnet) return;
    box.dataset.gezeichnet = '1';
    const liste = await ladeTouren();
    box.querySelector('[data-touren-laden]')?.remove();
    const ul = document.createElement('ul');
    ul.className = 'vhs-tourenliste';
    ul.innerHTML = liste.map((t) => `<li><div><b>${esc(t.titel)}</b><br><span class="vhs-small vhs-muted">${esc(t.konto)} · ${t.schritte.length} Schritte · Richtzeit ${esc(t.richtzeit || '–')}</span>
        <br><span class="vhs-small">${esc(t.beschreibung || '')}</span></div>
        <button type="button" class="vhs-btn" data-tour-start="${esc(t.id)}">Tour starten</button></li>`).join('');
    box.append(ul);
  };

  setInterval(() => { takt(); listeZeichnen(); }, 400);
  window.vhsTour = { starten, beenden: () => beenden(false), zustand: lies, ziel: () => zielEl, tour: () => aktuell, touren: ladeTouren, schritt: () => (aktuell && lies() ? aktuell.schritte[lies().i] : null) };
})();
