// Komponententest für <vhs-kurse> im Headless-Browser (Edge/Chrome über das DevTools-Protokoll, ohne npm-Pakete).
//
//   node tests/Shop.Komponente/komponententest.mjs [basis-url] [browser.exe]
//
// Voraussetzung: eine laufende VibeVHS-Instanz mit Demodaten (Standard http://localhost:5199). Geprüft wird gegen
// /shop/demo.html: Liste, Detail, Warenkorb, Kasse (in der Komponente und auf der Kassenseite), Tastaturbedienung,
// Fokusführung, Fehlerliste, Beschriftungen, Zielgrößen, Kontrast. Ausgabe: JSON; Exitcode 1 bei Fehlschlägen.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const basis = process.argv[2] ?? 'http://localhost:5199';
const browserPfad = process.argv[3] ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const bildOrdner = process.env.SHOP_TEST_BILDER;
const port = 9400 + Math.floor(Math.random() * 400);
const profil = mkdtempSync(join(tmpdir(), 'vhs-kurse-test-'));
const browser = spawn(browserPfad, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profil}`, '--no-first-run', '--disable-extensions', 'about:blank'], { stdio: 'ignore' });
const warte = (ms) => new Promise((r) => setTimeout(r, ms));

let ziel;
for (let i = 0; i < 50 && !ziel; i++) {
  try { ziel = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json(); } catch { await warte(200); }
}
const ws = new WebSocket(ziel.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let nr = 0; const offen = new Map();
ws.addEventListener('message', (e) => { const m = JSON.parse(e.data); if (m.id && offen.has(m.id)) { offen.get(m.id)(m); offen.delete(m.id); } });
const cdp = (method, params = {}) => new Promise((r) => { const i = ++nr; offen.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const js = async (a) => { const r = await cdp('Runtime.evaluate', { expression: a, awaitPromise: true, returnByValue: true }); if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description ?? 'JS-Fehler'); return r.result?.result?.value; };
const taste = async (key, code = key, keyCode = 0, shift = false) => {
  for (const type of ['keyDown', 'keyUp']) await cdp('Input.dispatchKeyEvent', { type, key, code, windowsVirtualKeyCode: keyCode, modifiers: shift ? 8 : 0, text: type === 'keyDown' && key === 'Enter' ? '\r' : undefined });
};
const enter = () => taste('Enter', 'Enter', 13);
const tab = (shift = false) => taste('Tab', 'Tab', 9, shift);
const S = `document.querySelector('vhs-kurse').shadowRoot`;
const aktiv = () => js(`(() => { const a = ${S}.activeElement; return a ? (a.tagName + ':' + (a.textContent || a.name || '').trim().replace(/\\s+/g, ' ').slice(0, 60)) : null; })()`);
const bis = async (ausdruck, ms = 15000) => { const ende = Date.now() + ms; while (Date.now() < ende) { if (await js(ausdruck)) return true; await warte(150); } return false; };
const shot = async (name) => { if (!bildOrdner) return; const r = await cdp('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true }); writeFileSync(join(bildOrdner, name), Buffer.from(r.result.data, 'base64')); };

const ergebnisse = [];
const pruefe = (name, ok, info) => { ergebnisse.push({ name, ok: !!ok, ...(info === undefined ? {} : { info }) }); };

/** Statische Barrierefreiheitsprüfung der aktuellen Ansicht im Shadow DOM. */
const a11y = (ansicht) => js(`(() => {
  const r = ${S}; const probleme = [];
  for (const el of r.querySelectorAll('input, select, textarea')) {
    if (el.type === 'hidden') continue;
    const label = (el.id && r.querySelector('label[for="' + el.id + '"]')) || el.closest('label') || el.getAttribute('aria-label');
    if (!label) probleme.push('ohne Label: ' + (el.name || el.id));
  }
  for (const b of r.querySelectorAll('button, a[href]')) {
    if (!(b.textContent.trim() || b.getAttribute('aria-label'))) probleme.push('ohne Namen: ' + b.outerHTML.slice(0, 60));
    const box = b.getBoundingClientRect();
    if (b.tagName === 'BUTTON' && box.width > 0 && (box.width < 24 || box.height < 24)) probleme.push('Ziel zu klein: ' + b.textContent.trim().slice(0, 30));
    if (b.tabIndex < 0) probleme.push('nicht per Tab erreichbar: ' + b.textContent.trim().slice(0, 30));
  }
  let vorher = 1;
  for (const h of r.querySelectorAll('h2, h3, h4')) { const n = +h.tagName[1]; if (n > vorher + 1) probleme.push('Überschrift springt: ' + h.tagName); vorher = n; }
  const ids = [...r.querySelectorAll('[id]')].map((e) => e.id); const doppelt = ids.filter((x, i) => ids.indexOf(x) !== i);
  if (doppelt.length) probleme.push('doppelte IDs: ' + [...new Set(doppelt)].join(','));
  for (const el of r.querySelectorAll('[aria-describedby]')) for (const id of el.getAttribute('aria-describedby').split(' ')) if (!r.getElementById(id)) probleme.push('aria-describedby ins Leere: ' + id);
  const lum = (c) => { const [R, G, B] = c.match(/\\d+/g).slice(0, 3).map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; }); return 0.2126 * R + 0.7152 * G + 0.0722 * B; };
  const kontrast = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
  for (const el of r.querySelectorAll('p, li, td, th, label, legend, button, a, h2, h3, span.status')) {
    if (!el.textContent.trim() || !el.getClientRects().length) continue;
    const s = getComputedStyle(el); let bg = 'rgb(255, 255, 255)';
    for (let e = el; e; e = e.parentElement || e.getRootNode().host) { const c = getComputedStyle(e).backgroundColor; if (c && !c.endsWith(', 0)') && c !== 'transparent') { bg = c; break; } }
    const k = kontrast(s.color, bg); const gross = parseFloat(s.fontSize) >= 24 || (parseFloat(s.fontSize) >= 18.66 && +s.fontWeight >= 700);
    if (k < (gross ? 3 : 4.5) && s.opacity === '1') probleme.push('Kontrast ' + k.toFixed(2) + ': ' + el.textContent.trim().slice(0, 30));
  }
  return { ansicht: ${JSON.stringify(ansicht)}, probleme: [...new Set(probleme)].slice(0, 12) };
})()`);

try {
  await cdp('Page.enable');
  await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
  await cdp('Page.navigate', { url: `${basis}/shop/demo.html` });
  pruefe('Liste lädt', await bis(`!!${S}?.querySelector('ul.kurse li')`), await js(`${S}?.querySelector('#anzahl')?.textContent`));
  pruefe('Fokus bleibt beim Laden auf der Seite', (await js(`document.activeElement === document.body`)));
  pruefe('Shadow DOM, Live-Region vorhanden', await js(`!!${S}.querySelector('[role=status][aria-live=polite]')`));
  pruefe('Verfügbarkeit nachgeladen (Text, nicht nur Farbe)', await bis(`[...${S}.querySelectorAll('p.verf')].some(p => /frei|Warteliste|buchbar|Ausgebucht/.test(p.textContent))`));
  let a = await a11y('Liste'); pruefe('A11y Liste', a.probleme.length === 0, a.probleme);
  await shot('shop-liste.png');

  // Einen Kurs mit mindestens zwei freien Plätzen wählen (Testdaten ändern sich mit jedem Lauf).
  await bis(`[...${S}.querySelectorAll('p.verf')].filter(p => /Noch [0-9]+ Plätze frei/.test(p.textContent)).length > 0`);
  const kurs = await js(`(() => { const p = [...${S}.querySelectorAll('p.verf')].find(p => /Noch [0-9]+ Plätze frei/.test(p.textContent));
    return { nummer: p.dataset.nummer, titel: p.closest('li').querySelector('h3').textContent.trim() }; })()`);
  pruefe('Kurs mit freien Plätzen gefunden', !!kurs?.nummer, kurs);

  // Suche per Tastatur
  await js(`${S}.querySelector('#q').focus()`);
  await js(`${S}.querySelector('#q').value = ${JSON.stringify(kurs.nummer)}`);
  await enter();
  pruefe('Suche per Enter', await bis(`${S}.querySelector('#anzahl')?.textContent.startsWith('1 ')`), await js(`${S}.querySelector('#anzahl')?.textContent`));
  pruefe('Fokus nach Suche im Suchfeld', (await aktiv())?.startsWith('INPUT'), await aktiv());
  pruefe('Ergebnis angesagt', await bis(`${S}.querySelector('#ansage').textContent.includes('Kurs')`));

  // Detail per Tastatur öffnen: Tab zum Kurstitel, Enter
  for (let i = 0; i < 4 && !(await aktiv())?.includes(kurs.titel); i++) await tab();
  pruefe('Kurstitel per Tab erreichbar', (await aktiv())?.includes(kurs.titel), await aktiv());
  await enter();
  pruefe('Detail öffnet, Fokus auf Überschrift', await bis(`${S}.activeElement?.tagName === 'H2' && ${S}.activeElement.textContent.includes(${JSON.stringify(kurs.titel)})`), await aktiv());
  pruefe('Detail: Termine-Tabelle mit caption und Orte mit Wegbeschreibung',
    await js(`!!${S}.querySelector('table caption') && ${S}.querySelector('section')?.textContent.includes('Wegbeschreibung')`));
  a = await a11y('Detail'); pruefe('A11y Detail', a.probleme.length === 0, a.probleme);
  await shot('shop-detail.png');

  for (let i = 0; i < 6 && !(await aktiv())?.includes('In den Warenkorb'); i++) await tab();
  pruefe('„In den Warenkorb“ per Tab erreichbar', (await aktiv())?.includes('Warenkorb'), await aktiv());
  await enter();
  pruefe('Warenkorb mit Reservierung (nach Arbeitsnachweis)', await bis(`${S}.querySelector('h2')?.textContent === 'Warenkorb' && ${S}.textContent.includes('reserviert')`, 30000),
    await js(`${S}.querySelector('p')?.textContent`));
  pruefe('Fokus auf Warenkorb-Überschrift', (await aktiv())?.startsWith('H2:Warenkorb'), await aktiv());
  pruefe('Angesagt: liegt im Warenkorb', await bis(`${S}.querySelector('#ansage').textContent.includes('Warenkorb')`));
  a = await a11y('Warenkorb'); pruefe('A11y Warenkorb', a.probleme.length === 0, a.probleme);
  const korb = await js(`sessionStorage.getItem('vhs-korb')`);
  pruefe('Warenkorb-ID gemerkt (sessionStorage)', !!korb);
  await shot('shop-warenkorb.png');

  // Kasse in der Komponente: leeres Absenden zeigt Fehlerliste mit Sprungmarken
  await js(`[...${S}.querySelectorAll('button')].find(b => b.textContent.includes('Zur Kasse')).focus()`);
  await enter();
  pruefe('Kasse öffnet', await bis(`${S}.querySelector('h2')?.textContent === 'Kasse'`));
  pruefe('Felder mit autocomplete', await js(`${S}.querySelector('#vorname').autocomplete === 'given-name' && ${S}.querySelector('#email').autocomplete === 'email'`));
  a = await a11y('Kasse'); pruefe('A11y Kasse', a.probleme.length === 0, a.probleme);
  await js(`${S}.querySelector('button[type=submit]').focus()`);
  await enter();
  pruefe('Fehlerliste erscheint und hat den Fokus', await bis(`${S}.activeElement?.classList.contains('fehlerliste')`), await aktiv());
  const fehlerzahl = await js(`${S}.querySelectorAll('.fehlerliste li').length`);
  pruefe('Alle Pflichtfehler gelistet (11)', fehlerzahl === 11, fehlerzahl);
  pruefe('Felder als ungültig markiert', await js(`${S}.querySelector('#vorname').getAttribute('aria-invalid') === 'true' && ${S}.querySelector('#f-vorname').textContent.length > 0`));
  await js(`${S}.querySelector('.fehlerliste a').focus()`);
  await enter();
  pruefe('Sprungmarke setzt Fokus ins Feld', (await aktiv()) === 'INPUT:vorname', await aktiv());
  await shot('shop-kasse-fehler.png');

  // Kasse auf der VibeVHS-Kassenseite (Weiterleitung, kasse="extern")
  await cdp('Page.navigate', { url: `${basis}/shop/kasse.html#korb=${korb}` });
  pruefe('Kassenseite übernimmt den Warenkorb', await bis(`${S}?.querySelector('h2')?.textContent === 'Kasse'`));
  const werte = { vorname: 'Erika', nachname: 'Test', email: 'erika.test@example.org', strasse: 'Hauptstr. 1', postleitzahl: '48282', ort: 'Emsdetten', kontoinhaber: 'Erika Test', iban: 'DE89370400440532013000' };
  await js(`(() => { const r = ${S}; for (const [k, v] of Object.entries(${JSON.stringify(werte)})) r.querySelector('#' + k).value = v;
    for (const n of ['agb', 'datenschutz', 'sepa']) r.querySelector('[name=' + n + ']').checked = true; return true; })()`);
  await js(`${S}.querySelector('button[type=submit]').focus()`);
  await enter();
  pruefe('Buchung abgeschlossen (Arbeitsnachweis, Idempotency-Key)', await bis(`${S}.querySelector('h2')?.textContent.startsWith('Vielen Dank')`, 40000),
    await js(`${S}.querySelector('h2')?.textContent + ' | ' + ${S}.querySelector('p')?.textContent`));
  pruefe('Bestätigung nennt Mandatsreferenz', await js(`${S}.textContent.includes('Mandatsreferenz VHS-')`));
  pruefe('Warenkorb nach Buchung vergessen', (await js(`sessionStorage.getItem('vhs-korb')`)) === null);
  a = await a11y('Bestätigung'); pruefe('A11y Bestätigung', a.probleme.length === 0, a.probleme);
  await shot('shop-bestaetigung.png');

  // Mein Konto: unbekannte Adresse wird abgewiesen, Registrierung (Double-Opt-in) mit Arbeitsnachweis
  // „Mein Konto“ im Shop führt ins Teilnehmendenportal; die Konto-Ansicht der Komponente bleibt für eigene Websites nutzbar.
  await cdp('Page.navigate', { url: `${basis}/shop/konto.html` });
  pruefe('Mein Konto leitet ins Teilnehmendenportal', await bis(`location.pathname.startsWith('/portal/teilnehmende')`), await js('location.pathname'));
  await cdp('Page.navigate', { url: `${basis}/shop/demo.html` });
  await bis(`!!${S}?.querySelector('ul.kurse li')`);
  await js(`(() => { const alt = document.querySelector('vhs-kurse'); const neu = document.createElement('vhs-kurse'); neu.setAttribute('ansicht', 'konto'); alt.replaceWith(neu); return true; })()`);
  pruefe('Kontoseite lädt', await bis(`!!${S}?.querySelector('#anmelden')`));
  a = await a11y('Konto'); pruefe('A11y Konto', a.probleme.length === 0, a.probleme);
  await js(`${S}.querySelector('#a-email').value = 'niemand@example.org'`);
  await js(`${S}.querySelector('#anmelden button').click()`);
  pruefe('Anmeldung ohne bestätigtes Konto abgewiesen', await bis(`${S}.querySelector('#a-fehler').textContent.length > 0`), await js(`${S}.querySelector('#a-fehler').textContent`));
  await js(`(() => { const r = ${S}; r.querySelector('#r-vorname').value = 'Rita'; r.querySelector('#r-nachname').value = 'Registriert'; r.querySelector('#r-email').value = 'rita.' + Date.now() + '@example.org'; r.querySelector('#registrieren button').click(); return true; })()`);
  pruefe('Registrierung: Hinweis auf Bestätigungslink', await bis(`${S}.querySelector('#registrieren')?.textContent.includes('Bestätigungslink')`, 30000),
    await js(`${S}.querySelector('#registrieren')?.textContent.slice(0, 120)`));

  // Schmale Ansicht (320 px, Zoom/Reflow): kein horizontales Scrollen
  await cdp('Emulation.setDeviceMetricsOverride', { width: 320, height: 800, deviceScaleFactor: 1, mobile: true });
  await cdp('Page.navigate', { url: `${basis}/shop/demo.html` });
  await bis(`!!${S}?.querySelector('ul.kurse li')`);
  pruefe('Reflow bei 320 px ohne horizontales Scrollen', await js(`document.documentElement.scrollWidth <= 320`), await js(`document.documentElement.scrollWidth`));
  await shot('shop-schmal.png');
} catch (f) {
  pruefe('Ablauf ohne Ausnahme', false, String(f));
} finally {
  ws.close();
  browser.kill();
  await warte(500);
  try { rmSync(profil, { recursive: true, force: true }); } catch { /* Profil wird vom Browser noch gehalten */ }
}

const fehl = ergebnisse.filter((e) => !e.ok);
console.log(JSON.stringify({ basis, geprueft: ergebnisse.length, fehlgeschlagen: fehl.length, ergebnisse }, null, 1));
process.exit(fehl.length ? 1 : 0);
