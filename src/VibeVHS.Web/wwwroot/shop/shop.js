/**
 * <vhs-kurse> – Kursliste, Detail, Warenkorb und Kasse der VibeVHS-Buchungs-API als Web-Komponente.
 *
 * Einbinden:  <script type="module" src="https://vhs.example/shop/shop.js"></script>
 *             <vhs-kurse fachbereich="3" ort="Rheda" suche="Yoga"></vhs-kurse>
 *
 * Attribute:  fachbereich, ort, suche   Vorfilter (Nutzer können weiter suchen)
 *             ansicht="buchen" kurs="…"   nur Verfügbarkeit und „In den Warenkorb“ (für serverseitige Detailseiten, z. B. TYPO3)
 *             api                       Basis der API (Standard: /api/v1/shop am Server dieses Skripts)
 *             kasse="extern"            „Zur Kasse“ leitet auf die Kassenseite von VibeVHS weiter
 *             ansicht="kasse"           (Kassenseite) Warenkorb aus #korb=… laden und Kasse zeigen
 *             ansicht="konto"           Mein Konto: Anmeldung, Registrierung (Double-Opt-in), eigene Buchungen, Abmeldung nach AGB
 * Gestaltung: CSS-Variablen --vhs-farbe, --vhs-farbe-text, --vhs-farbe-hintergrund, --vhs-farbe-rand, --vhs-farbe-hinweis,
 *             --vhs-schrift, --vhs-radius, --vhs-abstand, --vhs-fokus (siehe :host unten).
 * Barrierefreiheit (BITV 2.0 / WCAG 2.2 AA): echte Überschriften, Formular-Labels, Fehlerliste mit Sprungmarken,
 * Live-Region für Statusmeldungen, Fokus beim Ansichtswechsel auf die Überschrift, Bedienung vollständig per Tastatur,
 * Status nie nur über Farbe, Zielgrößen ≥ 24 px, prefers-reduced-motion und forced-colors.
 */

const SKRIPT = new URL(import.meta.url);
const STANDARD_API = new URL('../api/v1/shop', SKRIPT).href;
const euro = new Intl.NumberFormat('de-DE', { style: 'currency', currency: 'EUR' });
const datum = new Intl.DateTimeFormat('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' });
const uhrzeit = new Intl.DateTimeFormat('de-DE', { hour: '2-digit', minute: '2-digit' });

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const geld = (cent) => (cent == null ? 'auf Anfrage' : euro.format(cent / 100));
const tag = (iso) => (iso ? datum.format(new Date(iso)) : '–');
const zeit = (iso) => (iso ? uhrzeit.format(new Date(iso)) : '');

function speicherLesen(k) { try { return sessionStorage.getItem(k); } catch { return null; } }
function speicherSchreiben(k, v) { try { if (v == null) sessionStorage.removeItem(k); else sessionStorage.setItem(k, v); } catch { /* ohne Speicher weiter */ } }

// ---------- SHA-256 (für den Arbeitsnachweis; synchron, auch ohne sicheren Kontext) ----------
const K = new Uint32Array([0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2]);
const W = new Uint32Array(64);
export function sha256(bytes) {
  const n = bytes.length, bloecke = ((n + 9 + 63) >> 6) << 6, m = new Uint8Array(bloecke);
  m.set(bytes); m[n] = 0x80;
  const bits = n * 8; m[bloecke - 4] = bits >>> 24; m[bloecke - 3] = bits >>> 16; m[bloecke - 2] = bits >>> 8; m[bloecke - 1] = bits;
  let h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  for (let o = 0; o < bloecke; o += 64) {
    for (let i = 0; i < 16; i++) W[i] = (m[o + 4 * i] << 24) | (m[o + 4 * i + 1] << 16) | (m[o + 4 * i + 2] << 8) | m[o + 4 * i + 3];
    for (let i = 16; i < 64; i++) {
      const a = W[i - 15], b = W[i - 2];
      W[i] = (((a >>> 7) | (a << 25)) ^ ((a >>> 18) | (a << 14)) ^ (a >>> 3)) + W[i - 16] + (((b >>> 17) | (b << 15)) ^ ((b >>> 19) | (b << 13)) ^ (b >>> 10)) + W[i - 7];
    }
    let a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (let i = 0; i < 64; i++) {
      const t1 = (h + (((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7))) + ((e & f) ^ (~e & g)) + K[i] + W[i]) | 0;
      const t2 = ((((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10))) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
      h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
    }
    h0 = (h0 + a) | 0; h1 = (h1 + b) | 0; h2 = (h2 + c) | 0; h3 = (h3 + d) | 0; h4 = (h4 + e) | 0; h5 = (h5 + f) | 0; h6 = (h6 + g) | 0; h7 = (h7 + h) | 0;
  }
  return [h0, h1, h2, h3, h4, h5, h6, h7];
}

/** Sucht n mit SHA-256("challenge:n") beginnend mit `bits` Null-Bits; gibt zwischendurch den Browser frei. */
export async function arbeitsnachweis(challenge, bits) {
  const enc = new TextEncoder();
  const maske = bits >= 32 ? 0xffffffff : ~(0xffffffff >>> bits);
  for (let n = 0; ; n++) {
    const kandidat = `${challenge}:${n}`;
    if ((sha256(enc.encode(kandidat))[0] & maske) === 0) return kandidat;
    if (n % 4000 === 3999) await new Promise((r) => setTimeout(r, 0));
  }
}

class ApiFehler extends Error {
  constructor(status, problem) { super(problem?.detail || `Fehler ${status}`); this.status = status; this.problem = problem || {}; }
}

const STIL = `
:host { --vhs-farbe: #005a70; --vhs-farbe-text: #1a1a1a; --vhs-farbe-hintergrund: #ffffff; --vhs-farbe-rand: #6b6b6b;
  --vhs-farbe-hinweis: #8a1c1c; --vhs-farbe-gut: #1d5e20; --vhs-farbe-flaeche: #f3f6f7; --vhs-schrift: system-ui, sans-serif;
  --vhs-radius: 6px; --vhs-abstand: 1rem; --vhs-fokus: #b35c00;
  display: block; font-family: var(--vhs-schrift); color: var(--vhs-farbe-text); background: var(--vhs-farbe-hintergrund); line-height: 1.5; }
* { box-sizing: border-box; }
h2 { font-size: 1.5rem; margin: 0 0 .5rem; } h3 { font-size: 1.15rem; margin: 0 0 .25rem; }
h2:focus { outline: none; }
a { color: var(--vhs-farbe); }
.nur-sr { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0,0,0,0); white-space: nowrap; border: 0; }
button, .knopf { font: inherit; min-height: 44px; min-width: 44px; padding: .5rem 1rem; border-radius: var(--vhs-radius); border: 2px solid var(--vhs-farbe);
  background: var(--vhs-farbe-hintergrund); color: var(--vhs-farbe); cursor: pointer; text-decoration: none; display: inline-flex; align-items: center; gap: .4rem; }
button.primaer { background: var(--vhs-farbe); color: #fff; }
button[aria-disabled="true"] { opacity: .65; cursor: not-allowed; }
:focus-visible { outline: 3px solid var(--vhs-fokus); outline-offset: 2px; }
input, select { font: inherit; padding: .5rem; min-height: 44px; border: 1px solid var(--vhs-farbe-rand); border-radius: var(--vhs-radius); width: 100%; color: inherit; background: #fff; }
input[type=checkbox], input[type=radio] { width: 1.4rem; height: 1.4rem; min-height: 0; margin: .15rem .5rem 0 0; flex: none; accent-color: var(--vhs-farbe); }
label { font-weight: 600; display: block; margin-bottom: .25rem; }
.feld { margin-bottom: var(--vhs-abstand); }
.wahl { display: flex; align-items: flex-start; font-weight: 400; margin-bottom: .5rem; }
fieldset { border: 1px solid var(--vhs-farbe-rand); border-radius: var(--vhs-radius); margin: 0 0 var(--vhs-abstand); padding: var(--vhs-abstand); }
legend { font-weight: 700; padding: 0 .25rem; }
.raster { display: grid; gap: 0 var(--vhs-abstand); grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); }
.suche { display: flex; gap: .5rem; align-items: flex-end; flex-wrap: wrap; margin-bottom: var(--vhs-abstand); }
.suche .feld { flex: 1 1 16rem; margin: 0; }
ul.kurse { list-style: none; padding: 0; margin: 0; display: grid; gap: var(--vhs-abstand); }
.karte { border: 1px solid var(--vhs-farbe-rand); border-radius: var(--vhs-radius); padding: var(--vhs-abstand); }
.karte h3 button { all: unset; cursor: pointer; color: var(--vhs-farbe); text-decoration: underline; font-size: 1.15rem; font-weight: 700; }
.karte h3 button:focus-visible { outline: 3px solid var(--vhs-fokus); outline-offset: 2px; }
.meta { display: flex; flex-wrap: wrap; gap: .25rem 1rem; margin: .25rem 0; padding: 0; list-style: none; }
.status { display: inline-block; font-weight: 600; padding: .1rem .5rem; border-radius: var(--vhs-radius); border: 1px solid currentColor; }
.status.frei { color: var(--vhs-farbe-gut); } .status.warte { color: #6b4e00; } .status.nein { color: var(--vhs-farbe-hinweis); }
table { border-collapse: collapse; width: 100%; margin-bottom: var(--vhs-abstand); }
caption { text-align: left; font-weight: 700; margin-bottom: .25rem; }
th, td { text-align: left; padding: .35rem .5rem; border-bottom: 1px solid #ccc; vertical-align: top; }
.ausgefallen { text-decoration: line-through; }
.flaeche { background: var(--vhs-farbe-flaeche); border-radius: var(--vhs-radius); padding: var(--vhs-abstand); margin-bottom: var(--vhs-abstand); }
.fehler { color: var(--vhs-farbe-hinweis); font-weight: 600; margin-top: .25rem; }
.fehlerliste { border: 3px solid var(--vhs-farbe-hinweis); border-radius: var(--vhs-radius); padding: var(--vhs-abstand); margin-bottom: var(--vhs-abstand); }
.fehlerliste:focus { outline: 3px solid var(--vhs-fokus); }
.zeile { display: flex; gap: .5rem; flex-wrap: wrap; align-items: center; margin: var(--vhs-abstand) 0; }
.mandat { font-size: .95rem; }
@media (prefers-reduced-motion: reduce) { * { transition: none !important; animation: none !important; } }
@media (forced-colors: active) { .status, button, .karte { border: 1px solid CanvasText; } }
`;

export class VhsKurse extends HTMLElement {
  static observedAttributes = ['fachbereich', 'ort', 'suche'];

  constructor() {
    super();
    this.attachShadow({ mode: 'open' });
    this.shadowRoot.innerHTML = `<style>${STIL}</style><div class="nur-sr" role="status" aria-live="polite" aria-atomic="true" id="ansage"></div><div id="inhalt"></div>`;
    this.$ = this.shadowRoot.getElementById('inhalt');
    this.einstellungen = null;
  }

  get api() { return (this.getAttribute('api') || STANDARD_API).replace(/\/$/, ''); }
  get korbId() { return speicherLesen('vhs-korb'); }
  set korbId(v) { speicherSchreiben('vhs-korb', v); }

  async connectedCallback() {
    this.suche = this.getAttribute('suche') || '';
    try { this.einstellungen = await this.abrufen('/einstellungen'); } catch { this.einstellungen = null; }
    if (this.getAttribute('ansicht') === 'buchen' && this.getAttribute('kurs')) {
      await this.buchungsbox(this.getAttribute('kurs'));
    } else if (this.getAttribute('ansicht') === 'konto') {
      await this.konto();
    } else if (this.getAttribute('ansicht') === 'kasse') {
      const aus = new URLSearchParams(location.hash.slice(1)).get('korb');
      if (aus) this.korbId = aus;
      await this.kasse();
    } else {
      await this.liste(false);
    }
  }

  attributeChangedCallback(name, alt, neu) {
    if (!this.isConnected || alt === neu || !this.einstellungen) return;
    if (name === 'suche') this.suche = neu || '';
    this.liste(false);
  }

  ansagen(text) { const a = this.shadowRoot.getElementById('ansage'); a.textContent = ''; setTimeout(() => { a.textContent = text; }, 50); }

  zeigen(html, fokus = true) {
    this.$.innerHTML = html;
    if (fokus) this.$.querySelector('h2')?.focus();
  }

  async abrufen(pfad, optionen = {}) {
    const kopf = { Accept: 'application/json', ...(optionen.headers || {}) };
    if (optionen.body) kopf['Content-Type'] = 'application/json';
    const r = await fetch(this.api + pfad, { ...optionen, headers: kopf, body: optionen.body ? JSON.stringify(optionen.body) : undefined });
    const text = await r.text();
    const daten = text ? JSON.parse(text) : null;
    if (!r.ok) throw new ApiFehler(r.status, daten);
    return daten;
  }

  async nachweis() {
    const a = await this.abrufen('/pow');
    return a.bits > 0 ? arbeitsnachweis(a.challenge, a.bits) : `${a.challenge}:0`;
  }

  fehlerAnzeigen(f) {
    this.zeigen(`<h2 tabindex="-1">Das hat nicht geklappt</h2><p role="alert">${esc(f.message)}</p><button type="button" data-aktion="liste">Zur Kursliste</button>`);
    this.verbinden();
  }

  verbinden() {
    this.$.querySelectorAll('[data-aktion]').forEach((el) => el.addEventListener('click', (e) => {
      const d = e.currentTarget.dataset;
      ({ liste: () => this.liste(), detail: () => this.detail(d.nummer), korb: () => this.warenkorb(), legen: () => this.inDenKorb(d.nummer),
        entfernen: () => this.entfernen(d.nummer), kasse: () => this.zurKasse() })[d.aktion]?.();
    }));
  }

  // ---------- Liste ----------

  async liste(fokus = true) {
    const q = new URLSearchParams();
    if (this.getAttribute('fachbereich')) q.set('fachbereich', this.getAttribute('fachbereich'));
    if (this.getAttribute('ort')) q.set('ort', this.getAttribute('ort'));
    if (this.suche) q.set('q', this.suche);
    let daten;
    try { daten = await this.abrufen('/kurse?' + q); } catch (f) { this.fehlerAnzeigen(f); return; }
    const korb = this.korbId;
    this.zeigen(`
      <h2 tabindex="-1">Kurse</h2>
      <form class="suche" role="search" aria-label="Kurse durchsuchen">
        <div class="feld"><label for="q">Suchbegriff</label><input id="q" name="q" type="search" value="${esc(this.suche)}" autocomplete="off"></div>
        <button type="submit" class="primaer">Suchen</button>
        ${korb ? '<button type="button" data-aktion="korb">Warenkorb ansehen</button>' : ''}
      </form>
      <p id="anzahl">${daten.kurse.length === 1 ? '1 Kurs gefunden.' : `${daten.kurse.length} Kurse gefunden.`}</p>
      <ul class="kurse" aria-labelledby="anzahl">
        ${daten.kurse.map((k) => `
          <li class="karte">
            <h3><button type="button" data-aktion="detail" data-nummer="${esc(k.nummer)}">${esc(k.titel)}</button></h3>
            <ul class="meta">
              <li><span class="nur-sr">Kursnummer: </span>${esc(k.nummer)}</li>
              <li>ab ${esc(tag(k.beginn))}${k.zeiten ? `, ${esc(k.zeiten)}` : ''}</li>
              <li>${k.termine} ${k.termine === 1 ? 'Termin' : 'Termine'}</li>
              ${k.ort ? `<li>${esc(k.ort)}</li>` : ''}
              <li>${esc(geld(k.entgeltCent))}${k.ermaessigtCent != null ? ` (ermäßigt ${esc(geld(k.ermaessigtCent))})` : ''}</li>
              ${k.barrierefrei ? '<li>♿ barrierefrei</li>' : ''}
            </ul>
            <p class="verf" data-nummer="${esc(k.nummer)}">${k.onlineBuchbar ? 'Verfügbarkeit wird geladen …' : '<span class="status nein">Nur über die vhs buchbar</span>'}</p>
          </li>`).join('')}
      </ul>`, fokus);
    this.$.querySelector('form').addEventListener('submit', (e) => {
      e.preventDefault();
      this.suche = this.$.querySelector('#q').value.trim();
      this.liste(false).then(() => { this.$.querySelector('#q')?.focus(); this.ansagen(this.$.querySelector('#anzahl').textContent); });
    });
    this.verbinden();
    // Verfügbarkeit nachladen (zwischengespeichert, daher günstig)
    for (const p of this.$.querySelectorAll('p.verf')) {
      if (!daten.kurse.find((k) => k.nummer === p.dataset.nummer)?.onlineBuchbar) continue;
      this.abrufen(`/kurse/${encodeURIComponent(p.dataset.nummer)}/verfuegbarkeit`).then((v) => { p.innerHTML = this.statusHtml(v); }).catch(() => { p.textContent = ''; });
    }
  }

  statusHtml(v) {
    const art = v.buchbar ? (v.warteliste ? 'warte' : 'frei') : 'nein';
    return `<span class="status ${art}">${esc(v.text)}</span>`;
  }

  // ---------- Detail ----------

  async detail(nummer) {
    let d, v;
    try {
      [d, v] = await Promise.all([this.abrufen(`/kurse/${encodeURIComponent(nummer)}`),
        this.abrufen(`/kurse/${encodeURIComponent(nummer)}/verfuegbarkeit`).catch(() => null)]);
    } catch (f) { this.fehlerAnzeigen(f); return; }
    const k = d.kurs;
    const buchbar = k.onlineBuchbar && v?.buchbar;
    const grund = !k.onlineBuchbar ? 'Dieser Kurs ist online nicht buchbar; bitte wenden Sie sich an die vhs.' : (v && !v.buchbar ? v.text : '');
    this.zeigen(`
      <button type="button" data-aktion="liste">← Zurück zur Liste</button>
      <h2 tabindex="-1">${esc(k.titel)}</h2>
      <p>Kursnummer ${esc(k.nummer)} · ${esc(k.fachbereichName)} · ${esc(k.semester)}</p>
      ${d.beschreibung ? `<p>${esc(d.beschreibung)}</p>` : ''}
      <div class="flaeche">
        <p><strong>Entgelt:</strong> ${esc(geld(k.entgeltCent))}${k.ermaessigtCent != null ? `, ermäßigt ${esc(geld(k.ermaessigtCent))}` : ''}</p>
        <p><strong>Abmeldung:</strong> bis ${d.stornofristTage} Tage vor Beginn ${d.bearbeitungsgebuehrCent ? `gegen eine Bearbeitungsgebühr von ${esc(geld(d.bearbeitungsgebuehrCent))}` : 'kostenlos'}.</p>
        ${d.bildungsurlaub ? `<p><strong>Bildungsurlaub:</strong> ${esc(d.bildungsurlaub)}</p>` : ''}
        <p role="status">${v ? this.statusHtml(v) : ''}</p>
        <div class="zeile">
          <button type="button" class="primaer" data-aktion="legen" data-nummer="${esc(k.nummer)}" ${buchbar ? '' : 'aria-disabled="true" aria-describedby="grund"'}>
            ${v?.warteliste ? 'Auf die Warteliste setzen' : 'In den Warenkorb'}</button>
          ${grund ? `<span id="grund">${esc(grund)}</span>` : ''}
        </div>
      </div>
      <table><caption>Termine (${d.termine.length})</caption>
        <thead><tr><th scope="col">Nr.</th><th scope="col">Datum</th><th scope="col">Uhrzeit</th><th scope="col">Ort</th></tr></thead>
        <tbody>${d.termine.map((t) => `<tr class="${t.ausgefallen ? 'ausgefallen' : ''}"><td>${t.nummer}</td><td>${esc(tag(t.beginn))}${t.ausgefallen ? ' <span>(fällt aus)</span>' : ''}</td>
          <td>${esc(zeit(t.beginn))}–${esc(zeit(t.ende))}</td><td>${esc(t.orte.join(', '))}</td></tr>`).join('')}</tbody></table>
      ${d.orte.map((o) => `<section aria-labelledby="ort-${esc(o.name).replace(/\W/g, '')}">
        <h3 id="ort-${esc(o.name).replace(/\W/g, '')}">${esc(o.name)}</h3>
        ${o.strasse ? `<p>${esc(o.strasse)}, ${esc(o.postleitzahl)} ${esc(o.stadt)}</p>` : ''}
        <p><strong>Wegbeschreibung:</strong> ${esc(o.wegbeschreibung)}</p>
        ${o.merkmale.length ? `<p><strong>Ausstattung und Zugang:</strong></p><ul>${o.merkmale.map((m) => `<li>${esc(m)}</li>`).join('')}</ul>` : ''}
      </section>`).join('')}`);
    this.verbinden();
  }

  // ---------- Kundenkonto (Anmeldung über den Identitätsdienst; vorerst Test-Identitätsdienst) ----------

  get token() { return speicherLesen('vhs-token'); }
  set token(v) { speicherSchreiben('vhs-token', v); }

  async konto(meldung) {
    if (!this.token) {
      this.zeigen(`<h2 tabindex="-1">Mein Konto</h2>
        <form id="anmelden" novalidate><h3>Anmelden</h3>
          <div class="feld"><label for="a-email">E-Mail</label><input id="a-email" name="email" type="email" autocomplete="email" aria-describedby="a-hinweis"></div>
          <p id="a-hinweis" class="mandat">Testbetrieb: Die Anmeldung über das Servicekonto der Kommune (OIDC) folgt; bis dahin genügt die E-Mail eines bestätigten Kontos.</p>
          <div class="fehler" id="a-fehler" role="alert"></div><button type="submit" class="primaer">Anmelden</button></form>
        <form id="registrieren" novalidate><h3>Neues Kundenkonto</h3><div class="raster">
          ${this.feld('r-vorname', 'Vorname *', 'given-name')}${this.feld('r-nachname', 'Nachname *', 'family-name')}${this.feld('r-email', 'E-Mail *', 'email', 'email')}</div>
          <div class="fehler" id="r-fehler" role="alert"></div><button type="submit">Registrieren</button></form>`);
      const an = this.$.querySelector('#anmelden');
      an.addEventListener('submit', async (e) => {
        e.preventDefault();
        try {
          const t = await this.abrufen('/konto/anmelden', { method: 'POST', body: { eMail: an.email.value.trim() } });
          this.token = t.accessToken; await this.konto('Sie sind angemeldet.');
        } catch (f) { this.$.querySelector('#a-fehler').textContent = f.message; }
      });
      const re = this.$.querySelector('#registrieren');
      re.addEventListener('submit', async (e) => {
        e.preventDefault();
        const v = (n) => re.querySelector('#' + n).value.trim();
        try {
          this.ansagen('Sicherheitsprüfung läuft …');
          const pow = await this.nachweis();
          await this.abrufen('/konto/registrierung', { method: 'POST', body: { eMail: v('r-email'), vorname: v('r-vorname'), nachname: v('r-nachname'), sprache: (navigator.language || 'de').slice(0, 2) }, headers: { 'X-VHS-PoW': pow } });
          re.innerHTML = '<p role="status">Fast geschafft: Bitte öffnen Sie den Bestätigungslink in der E-Mail, die wir Ihnen geschickt haben.</p>';
        } catch (f) { this.$.querySelector('#r-fehler').textContent = f.message; }
      });
      return;
    }
    let liste;
    try { liste = await this.abrufen('/konto/buchungen', { headers: { Authorization: `Bearer ${this.token}` } }); } catch (f) {
      if (f.status === 401 || f.status === 403) { this.token = null; await this.konto('Bitte erneut anmelden.'); return; }
      this.fehlerAnzeigen(f); return;
    }
    this.zeigen(`<h2 tabindex="-1">Mein Konto</h2>
      <table><caption>Meine Buchungen (${liste.length})</caption>
        <thead><tr><th scope="col">Kurs</th><th scope="col">Beginn</th><th scope="col">Status</th><th scope="col">Abmeldung</th></tr></thead>
        <tbody>${liste.map((b) => `<tr><td>${esc(b.titel)} (${esc(b.kursnummer)})</td><td>${esc(tag(b.beginn))}</td><td>${esc(b.status)}</td>
          <td>${b.stornierbar ? `<p>${esc(b.stornoText)}</p><button type="button" data-storno="${esc(b.belegungId)}">Abmelden<span class="nur-sr"> von ${esc(b.titel)}</span></button>` : '–'}</td></tr>`).join('')}</tbody></table>
      <button type="button" id="abmelden">Konto verlassen (abmelden)</button>`);
    this.$.querySelectorAll('[data-storno]').forEach((k) => k.addEventListener('click', async () => {
      if (k.dataset.sicher !== '1') { k.dataset.sicher = '1'; k.firstChild.textContent = 'Wirklich abmelden? Erneut klicken'; this.ansagen('Zum Bestätigen erneut klicken.'); return; }
      try {
        const r = await this.abrufen(`/konto/buchungen/${k.dataset.storno}/storno`, { method: 'POST', body: { grund: 'Abmeldung im Kundenkonto' }, headers: { Authorization: `Bearer ${this.token}` } });
        await this.konto(`Abgemeldet. ${r.erklaerung}`);
      } catch (f) { this.ansagen(f.message); }
    }));
    this.$.querySelector('#abmelden').addEventListener('click', () => { this.token = null; this.konto('Sie sind abgemeldet.'); });
    if (meldung) this.ansagen(meldung);
  }

  // ---------- Buchungsbox (für serverseitig gerenderte Detailseiten, z. B. TYPO3) ----------

  async buchungsbox(nummer) {
    let v;
    try { v = await this.abrufen(`/kurse/${encodeURIComponent(nummer)}/verfuegbarkeit`); } catch (f) { this.$.innerHTML = `<p>${esc(f.message)}</p>`; return; }
    this.$.innerHTML = `<div class="flaeche"><h2 class="nur-sr" tabindex="-1">Buchung</h2><p role="status">${this.statusHtml(v)}</p>
      <div class="zeile"><button type="button" class="primaer" data-aktion="legen" data-nummer="${esc(nummer)}" ${v.buchbar ? '' : 'aria-disabled="true" aria-describedby="grund"'}>
      ${v.warteliste ? 'Auf die Warteliste setzen' : 'In den Warenkorb'}</button>${v.buchbar ? '' : `<span id="grund">${esc(v.text)}</span>`}</div></div>`;
    this.verbinden();
  }

  // ---------- Warenkorb ----------

  async inDenKorb(nummer) {
    const knopf = this.$.querySelector(`[data-aktion="legen"]`);
    if (knopf?.getAttribute('aria-disabled') === 'true') { this.ansagen(this.$.querySelector('#grund')?.textContent || 'Nicht buchbar.'); return; }
    try {
      let korb = this.korbId ? await this.abrufen(`/warenkorb/${this.korbId}`).catch(() => null) : null;
      if (korb && korb.status === 'offen') {
        const nummern = [...new Set([...korb.positionen.map((p) => p.kursnummer), nummer])];
        korb = await this.abrufen(`/warenkorb/${korb.id}/positionen`, { method: 'PUT', body: { kursnummern: nummern } });
      } else {
        this.ansagen('Sicherheitsprüfung läuft …');
        const pow = await this.nachweis();
        korb = await this.abrufen('/warenkorb', { method: 'POST', body: { kursnummern: [nummer] }, headers: { 'X-VHS-PoW': pow } });
      }
      this.korbId = korb.id;
      await this.warenkorb(korb, `${nummer} liegt im Warenkorb.`);
    } catch (f) {
      this.ansagen(f.message);
      const p = this.$.querySelector('[role="status"]');
      if (p) p.innerHTML = `<span class="status nein">${esc(f.message)}</span>`;
    }
  }

  async entfernen(nummer) {
    try {
      const alt = await this.abrufen(`/warenkorb/${this.korbId}`);
      const korb = await this.abrufen(`/warenkorb/${this.korbId}/positionen`, { method: 'PUT', body: { kursnummern: alt.positionen.map((p) => p.kursnummer).filter((n) => n !== nummer) } });
      await this.warenkorb(korb, `${nummer} wurde entfernt.`);
    } catch (f) { this.fehlerAnzeigen(f); }
  }

  async warenkorb(korb, meldung) {
    try { korb ??= this.korbId ? await this.abrufen(`/warenkorb/${this.korbId}`) : null; } catch { korb = null; }
    if (!korb || korb.status !== 'offen' || korb.positionen.length === 0) {
      if (korb?.status !== 'offen') this.korbId = null;
      this.zeigen(`<h2 tabindex="-1">Warenkorb</h2><p>${korb?.status === 'verfallen' ? 'Die Reservierung ist abgelaufen; die Plätze wurden freigegeben.' : 'Ihr Warenkorb ist leer.'}</p>
        <button type="button" data-aktion="liste">Kurse ansehen</button>`);
      this.verbinden();
      return;
    }
    const bis = new Date(korb.reserviertBis);
    this.zeigen(`
      <h2 tabindex="-1">Warenkorb</h2>
      <p>Ihre Plätze sind bis <strong>${esc(uhrzeit.format(bis))} Uhr</strong> reserviert (${Math.max(1, Math.round(korb.restSekunden / 60))} Minuten). Danach werden sie automatisch freigegeben.</p>
      <table><caption>Ausgewählte Kurse</caption>
        <thead><tr><th scope="col">Kurs</th><th scope="col">Hinweis</th><th scope="col">Entgelt</th><th scope="col"><span class="nur-sr">Aktion</span></th></tr></thead>
        <tbody>${korb.positionen.map((p) => `<tr><td>${esc(p.titel)} <span>(${esc(p.kursnummer)})</span></td><td>${esc(p.text)}</td>
          <td>${p.warteliste ? '–' : esc(geld(p.entgeltCent))}</td>
          <td><button type="button" data-aktion="entfernen" data-nummer="${esc(p.kursnummer)}">Entfernen<span class="nur-sr"> ${esc(p.titel)}</span></button></td></tr>`).join('')}</tbody>
        <tfoot><tr><th scope="row" colspan="2">Summe</th><td>${esc(geld(korb.summeCent))}</td><td></td></tr></tfoot></table>
      <div class="zeile"><button type="button" data-aktion="liste">Weitere Kurse</button><button type="button" class="primaer" data-aktion="kasse">Zur Kasse</button></div>`);
    this.verbinden();
    if (meldung) this.ansagen(meldung);
  }

  zurKasse() {
    if (this.getAttribute('kasse') === 'extern') {
      location.href = new URL(`../shop/kasse.html#korb=${encodeURIComponent(this.korbId)}`, this.api + '/').href;
    } else {
      this.kasse();
    }
  }

  // ---------- Kasse ----------

  async kasse() {
    let korb;
    try { korb = this.korbId ? await this.abrufen(`/warenkorb/${this.korbId}`) : null; } catch { korb = null; }
    if (!korb || korb.status !== 'offen') { await this.warenkorb(korb); return; }
    const e = this.einstellungen || {};
    this.zeigen(`
      <h2 tabindex="-1">Kasse</h2>
      <p>Summe: <strong>${esc(geld(korb.summeCent))}</strong> für ${korb.positionen.map((p) => esc(p.titel)).join(', ')}. Pflichtfelder sind mit * gekennzeichnet.</p>
      <div id="fehler"></div>
      <form novalidate>
        <fieldset><legend>Teilnehmende Person</legend><div class="raster">
          ${this.feld('vorname', 'Vorname *', 'given-name')}${this.feld('nachname', 'Nachname *', 'family-name')}
          ${this.feld('geburtsdatum', 'Geburtsdatum', 'bday', 'date')}${this.feld('email', 'E-Mail *', 'email', 'email')}
          ${this.feld('strasse', 'Straße und Hausnummer *', 'street-address')}${this.feld('postleitzahl', 'Postleitzahl *', 'postal-code', 'text', 'inputmode="numeric"')}
          ${this.feld('ort', 'Ort *', 'address-level2')}</div>
        </fieldset>
        <fieldset><legend>Zahlart *</legend>
          <label class="wahl"><input type="radio" name="zahlart" value="lastschrift" checked> SEPA-Lastschrift</label>
          <label class="wahl"><input type="radio" name="zahlart" value="rechnung"> Rechnung (Überweisung)</label>
          <div id="lastschrift"><div class="raster">${this.feld('kontoinhaber', 'Kontoinhaber/in *', 'cc-name')}${this.feld('iban', 'IBAN *', 'off', 'text', 'spellcheck="false"')}
            ${this.feld('bic', 'BIC (optional)', 'off')}</div>
            <p class="mandat">${esc(e.mandatText || '')}</p>
            <label class="wahl"><input type="checkbox" name="sepa"> Ich erteile das SEPA-Lastschriftmandat. *</label><div class="fehler" id="f-sepa"></div>
          </div>
        </fieldset>
        <fieldset><legend>Einwilligungen</legend>
          <label class="wahl"><input type="checkbox" name="agb" aria-describedby="f-agb"> <span>Ich habe die <a href="${esc(e.agbUrl || '#')}" target="_blank" rel="noopener">AGB (Fassung ${esc(e.agbVersion)}, öffnet neues Fenster)</a> gelesen und akzeptiere sie. *</span></label><div class="fehler" id="f-agb"></div>
          <label class="wahl"><input type="checkbox" name="datenschutz" aria-describedby="f-datenschutz"> <span>Ich habe die <a href="${esc(e.datenschutzUrl || '#')}" target="_blank" rel="noopener">Datenschutzhinweise (Fassung ${esc(e.datenschutzVersion)}, öffnet neues Fenster)</a> zur Kenntnis genommen. *</span></label><div class="fehler" id="f-datenschutz"></div>
          <label class="wahl"><input type="checkbox" name="werbung"> Ich möchte das Programmheft und Kursempfehlungen per E-Mail erhalten (jederzeit widerruflich).</label>
        </fieldset>
        <div class="zeile"><button type="button" data-aktion="korb">Zurück zum Warenkorb</button><button type="submit" class="primaer">Zahlungspflichtig buchen</button></div>
      </form>`);
    const form = this.$.querySelector('form');
    const ls = this.$.querySelector('#lastschrift');
    form.addEventListener('change', () => { ls.hidden = form.zahlart.value !== 'lastschrift'; });
    form.addEventListener('submit', (ev) => { ev.preventDefault(); this.buchen(form, korb); });
    this.verbinden();
  }

  feld(name, text, autocomplete, typ = 'text', extra = '') {
    const pflicht = text.includes('*');
    return `<div class="feld"><label for="${name}">${esc(text)}</label><input id="${name}" name="${name}" type="${typ}" autocomplete="${autocomplete}" ${pflicht ? 'aria-required="true"' : ''}
      aria-describedby="f-${name}" ${extra}><div class="fehler" id="f-${name}"></div></div>`;
  }

  zeigeFehler(liste) {
    this.$.querySelectorAll('[aria-invalid]').forEach((el) => el.removeAttribute('aria-invalid'));
    this.$.querySelectorAll('.fehler').forEach((el) => { el.textContent = ''; });
    const box = this.$.querySelector('#fehler');
    if (!liste.length) { box.innerHTML = ''; return; }
    for (const { feld, text } of liste) {
      const el = this.$.querySelector(`[name="${feld}"]`); const f = this.$.querySelector(`#f-${feld}`);
      if (el) el.setAttribute('aria-invalid', 'true');
      if (f) f.textContent = text;
    }
    box.innerHTML = `<div class="fehlerliste" tabindex="-1" role="alert"><h3>Bitte prüfen Sie Ihre Angaben (${liste.length})</h3>
      <ul>${liste.map(({ feld, text }) => `<li><a href="#${esc(feld)}" data-feld="${esc(feld)}">${esc(text)}</a></li>`).join('')}</ul></div>`;
    box.querySelectorAll('a[data-feld]').forEach((a) => a.addEventListener('click', (e) => { e.preventDefault(); this.$.querySelector(`[name="${a.dataset.feld}"]`)?.focus(); }));
    box.firstElementChild.focus();
  }

  async buchen(form, korb) {
    const w = (n) => form.elements[n]?.value.trim() ?? '';
    const zahlart = form.zahlart.value;
    const fehler = [];
    const pflicht = { vorname: 'Bitte den Vornamen angeben.', nachname: 'Bitte den Nachnamen angeben.', email: 'Bitte eine E-Mail-Adresse angeben.',
      strasse: 'Bitte Straße und Hausnummer angeben.', postleitzahl: 'Bitte die Postleitzahl angeben.', ort: 'Bitte den Ort angeben.' };
    if (zahlart === 'lastschrift') Object.assign(pflicht, { kontoinhaber: 'Bitte den Kontoinhaber angeben.', iban: 'Bitte die IBAN angeben.' });
    for (const [n, t] of Object.entries(pflicht)) if (!w(n)) fehler.push({ feld: n, text: t });
    if (w('email') && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(w('email'))) fehler.push({ feld: 'email', text: 'Die E-Mail-Adresse ist nicht gültig.' });
    if (!form.agb.checked) fehler.push({ feld: 'agb', text: 'Bitte die AGB akzeptieren.' });
    if (!form.datenschutz.checked) fehler.push({ feld: 'datenschutz', text: 'Bitte die Datenschutzhinweise bestätigen.' });
    if (zahlart === 'lastschrift' && !form.sepa.checked) fehler.push({ feld: 'sepa', text: 'Bitte das SEPA-Mandat erteilen oder „Rechnung“ wählen.' });
    this.zeigeFehler(fehler);
    if (fehler.length) return;

    const e = this.einstellungen || {};
    const anfrage = {
      teilnehmer: { vorname: w('vorname'), nachname: w('nachname'), geburtsdatum: w('geburtsdatum') || null, eMail: w('email'), strasse: w('strasse'),
        postleitzahl: w('postleitzahl'), ort: w('ort'), sprache: (navigator.language || 'de').slice(0, 2) },
      zahlart,
      lastschrift: zahlart === 'lastschrift' ? { kontoinhaber: w('kontoinhaber'), iban: w('iban'), bic: w('bic') || null } : null,
      einwilligungen: [
        { art: 'agb', version: e.agbVersion, erteilt: true }, { art: 'datenschutz', version: e.datenschutzVersion, erteilt: true },
        ...(zahlart === 'lastschrift' ? [{ art: 'sepa', version: e.mandatVersion, erteilt: true }] : []),
        { art: 'werbung', version: e.datenschutzVersion, erteilt: form.werbung.checked }],
    };
    // Ein Schlüssel je Warenkorb: ein erneutes Absenden (Netzabbruch, Doppelklick) bucht nicht doppelt.
    let schluessel = speicherLesen(`vhs-idem-${korb.id}`);
    if (!schluessel) { schluessel = (crypto.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`); speicherSchreiben(`vhs-idem-${korb.id}`, schluessel); }
    const knopf = form.querySelector('button[type=submit]');
    knopf.setAttribute('aria-disabled', 'true'); knopf.disabled = true;
    this.ansagen('Sicherheitsprüfung und Buchung laufen …');
    try {
      const pow = await this.nachweis();
      const b = await this.abrufen(`/warenkorb/${korb.id}/buchen`, { method: 'POST', body: anfrage, headers: { 'Idempotency-Key': schluessel, 'X-VHS-PoW': pow } });
      this.korbId = null; speicherSchreiben(`vhs-idem-${korb.id}`, null);
      this.bestaetigung(b, anfrage.teilnehmer.eMail);
    } catch (f) {
      knopf.disabled = false; knopf.removeAttribute('aria-disabled');
      const name = (f.problem?.feld || '').split('.').pop();
      this.zeigeFehler([{ feld: this.$.querySelector(`[name="${name}"]`) ? name : 'vorname', text: f.message }]);
    }
  }

  bestaetigung(b, mail) {
    this.zeigen(`
      <h2 tabindex="-1">Vielen Dank für Ihre Buchung</h2>
      <p>Wir haben Ihnen eine Bestätigung an <strong>${esc(mail)}</strong> geschickt${b.mandatsreferenz ? ` (Mandatsreferenz ${esc(b.mandatsreferenz)})` : ''}.</p>
      <ul>${b.positionen.map((p) => `<li>${esc(p.kursnummer)}: ${p.status === 'angemeldet' ? 'angemeldet' : 'auf der Warteliste'}${p.hinweis ? ` – ${esc(p.hinweis)}` : ''}</li>`).join('')}</ul>
      <button type="button" data-aktion="liste">Weitere Kurse ansehen</button>`);
    this.verbinden();
    this.ansagen('Buchung abgeschlossen.');
  }
}

if (!customElements.get('vhs-kurse')) customElements.define('vhs-kurse', VhsKurse);
