// Bildschirmfotos der laufenden Anwendung für Präsentationen (echte Oberfläche, Demodaten der vhs EGS) – nach dem Vorbild von SchoolCraft.
// Headless Edge/Chrome über das DevTools-Protokoll, ohne Zusatzpakete. Die Instanz läuft im Schulungsbetrieb (Spielzeit, siehe
// docs/schulungsbetrieb.md); Ereignisse der Geschichte entstehen durch Klicks in der echten Oberfläche, die Uhr läuft über „+1 Stunde“/„+1 Tag“.
//
//   node tools/praesentation/app-bilder.mjs <aufnahmen.json> <ausgabeordner> [ab-datei|-] [http://localhost:5199]
//
// aufnahmen.json: Liste von Schritten, jeweils optional:
//   "springen": "ausgangslage"      Sprungmarke (Datenstand wird ersetzt!), Kopf X-Schulung-Schluessel aus SCHULUNG_SCHLUESSEL
//   "vorstellen": ["+1 Stunde", …]   Knöpfe der Seite /spielzeit nacheinander (Automatik läuft je übersprungenem Zeitpunkt)
//   "konto": "Sonja Eickelberg"     Testanmeldung (Name auf /anmelden); "rolle": "Teilnehmerservice" wechselt die aktive Rolle
//   "pfad": "/personen"             Seite laden (ohne pfad: auf der aktuellen Seite bleiben)
//   "tippe": [["Feld", "Text"], …]   Eingabefeld per Beschriftung, Platzhalter oder aria-label füllen
//   "klick": ["Knopftext", …]       nacheinander klicken ("liste:Name" öffnet Radzen-Auswahl, "wahl:Text" wählt Eintrag, "css:Selektor")
//   "scroll": "Überschrift"         zur Überschrift scrollen; "app": true Handyansicht 390 × 844; "hoehe" Fensterhöhe (Vorgabe 1000)
//   "datei": "mo-start.png"         Bildschirmfoto; ohne datei nur Schritt
//   "warte"/"klickwarte" (ms), "erwarte": "Text" (bricht ab, wenn der Text nach den Schritten fehlt)
import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

const [, , liste, ausgabe, ab = '-', server = 'http://localhost:5199'] = process.argv;
if (!liste || !ausgabe) { console.error('Aufruf: node tools/praesentation/app-bilder.mjs <aufnahmen.json> <ausgabeordner> [ab-datei|-] [server]'); process.exit(2); }
let aufnahmen = JSON.parse(readFileSync(liste, 'utf8').replace(/^﻿/, ''));
if (ab !== '-') { const i = aufnahmen.findIndex(a => a.datei === ab); if (i < 0) throw new Error('Unbekannt: ' + ab); aufnahmen = aufnahmen.slice(i); }
mkdirSync(ausgabe, { recursive: true });
const SCHLUESSEL = process.env.SCHULUNG_SCHLUESSEL ?? 'woche-lokal';

const CHROME = process.env.CHROME ?? [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/chromium', '/usr/bin/google-chrome', '/opt/pw-browsers/chromium',
].find(p => { try { readFileSync(p); return true; } catch { return false; } });
const PORT = 9337;
const PROFIL = join(tmpdir(), 'vhs-app-bilder');
const schlaf = ms => new Promise(r => setTimeout(r, ms));
rmSync(PROFIL, { recursive: true, force: true });
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${PROFIL}`, '--no-first-run', '--no-default-browser-check',
  '--lang=de-DE', '--hide-scrollbars', '--window-size=1600,1000', 'about:blank'], { stdio: 'ignore' });
let bereit = false;
for (let i = 0; i < 60 && !bereit; i++) { try { await (await fetch(`http://127.0.0.1:${PORT}/json/version`)).json(); bereit = true; } catch { await schlaf(200); } }
const tab = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: 'PUT' })).json();
const ws = new WebSocket(tab.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let nr = 0; const offen = new Map(); const warter = [];
ws.addEventListener('message', e => {
  const m = JSON.parse(e.data);
  if (m.id && offen.has(m.id)) { offen.get(m.id)(m); offen.delete(m.id); }
  if (m.method) for (const w of [...warter]) if (w.name === m.method) { warter.splice(warter.indexOf(w), 1); w.r(m.params); }
});
const cdp = (method, params = {}) => new Promise((r, f) => { const id = ++nr; offen.set(id, m => m.error ? f(new Error(`${method}: ${m.error.message}`)) : r(m.result)); ws.send(JSON.stringify({ id, method, params })); });
const ereignis = (name, ms = 20000) => new Promise(r => { const w = { name, r }; warter.push(w); setTimeout(() => r(null), ms); });
await cdp('Page.enable'); await cdp('Runtime.enable');
// Bestätigungsdialoge (confirm) automatisch annehmen
ws.addEventListener('message', e => { const m = JSON.parse(e.data); if (m.method === 'Page.javascriptDialogOpening') cdp('Page.handleJavaScriptDialog', { accept: true }); });
async function js(a) { const r = await cdp('Runtime.evaluate', { expression: a, awaitPromise: true, returnByValue: true }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text); return r.result.value; }
async function gehe(pfad, warte = 2500) { const g = ereignis('Page.loadEventFired'); await cdp('Page.navigate', { url: server + pfad }); await g; await schlaf(warte); }
async function groesse(app, hoehe, breite = 1600, dichte = 2) {
  await cdp('Emulation.setDeviceMetricsOverride', app ? { width: 390, height: 844, deviceScaleFactor: 2, mobile: true } : { width: breite, height: hoehe, deviceScaleFactor: dichte, mobile: false });
}
let angemeldet = null;
async function anmelden(name) {
  if (angemeldet === name) return;
  await groesse(false, 1000);
  await gehe('/anmelden', 300);
  await js(`fetch('/abmelden', { method: 'POST' }).then(() => true).catch(() => true)`);
  await gehe('/anmelden', 800);
  const g = ereignis('Page.loadEventFired');
  const ok = await js(`(() => { const z = [...document.querySelectorAll('.vhs-list > div')].find(d => (d.querySelector('b')?.innerText ?? '').trim() === ${JSON.stringify(name)});
    const b = z?.querySelector('button'); if (!b) return false; b.click(); return true; })()`);
  if (!ok) throw new Error('Testkonto nicht gefunden: ' + name);
  await g; await schlaf(2000);
  angemeldet = name;
}

const KLICK = `(z) => {
  const text = b => { const c = b.cloneNode(true); c.querySelectorAll('i,.rzi,.material-icons,svg').forEach(x => x.remove()); return (c.textContent || b.getAttribute('aria-label') || b.getAttribute('title') || '').replace(/\\s+/g, ' ').trim(); };
  const sichtbar = b => b.getClientRects().length > 0;
  if (z.startsWith('css:')) { const b = [...document.querySelectorAll(z.slice(4))].find(sichtbar); if (!b) return false; b.click(); return true; }
  if (z.startsWith('dialog:')) { const n = z.slice(7); const b = [...document.querySelectorAll('.rz-dialog button, .rz-dialog-wrapper button, [role=dialog] button')].find(b => sichtbar(b) && text(b) === n);
    if (!b) return false; b.click(); return true; }
  if (z.startsWith('zeile:')) { const n = z.slice(6); const r = [...document.querySelectorAll('tbody tr')].find(r => sichtbar(r) && r.innerText.includes(n));
    if (!r) return false; r.scrollIntoView({ block: 'center' }); (r.querySelector('td') ?? r).click(); return true; }
  if (z.startsWith('spalte:')) { const n = z.slice(7); const t = [...document.querySelectorAll('th')].find(t => sichtbar(t) && text(t).startsWith(n));
    if (!t) return false; (t.querySelector('.rz-column-title, span') ?? t).click(); return true; }
  if (z.startsWith('liste:')) { const n = z.slice(6); const d = [...document.querySelectorAll('.rz-dropdown')].find(d => sichtbar(d)
    && (text(d) === n || (d.querySelector('input')?.getAttribute('aria-label') ?? '') === n || (d.getAttribute('aria-label') ?? '') === n)); if (!d) return false; d.click(); return true; }
  if (z.startsWith('wahl:')) { const n = z.slice(5); const o = [...document.querySelectorAll('.rz-dropdown-item,[role=option]')].find(o => sichtbar(o) && text(o) === n);
    if (!o) return false; o.click(); return true; }
  const b = [...document.querySelectorAll('button,a,summary,[role=tab],label,[role=button]')].find(b => sichtbar(b) && !b.disabled && text(b) === z)
    ?? [...document.querySelectorAll('button,a,summary,[role=tab],label,[role=button]')].find(b => sichtbar(b) && !b.disabled && text(b).startsWith(z))
    ?? [...document.querySelectorAll('li,td,.rz-dropdown,[role=option]')].find(b => sichtbar(b) && text(b) === z)
    ?? [...document.querySelectorAll('main *')].filter(b => sichtbar(b) && text(b).startsWith(z)).pop();
  if (!b) return false; b.scrollIntoView({ block: 'center' }); b.click(); return true; }`;

const TIPPE = `(feld, wert) => {
  const sichtbar = b => b.getClientRects().length > 0;
  const norm = s => (s || '').replace(/\\s+/g, ' ').replace(/\\*$/, '').trim();
  let el = null;
  for (const l of document.querySelectorAll('label')) {
    if (!sichtbar(l) || !norm(l.innerText).startsWith(feld)) continue;
    el = l.htmlFor ? document.getElementById(l.htmlFor) : l.querySelector('input,textarea,select');
    if (!el) el = l.parentElement?.querySelector('input:not([type=hidden]),textarea,select');
    if (el) break;
  }
  el ??= [...document.querySelectorAll('input,textarea,select')].find(i => sichtbar(i) && (i.placeholder === feld || i.getAttribute('aria-label') === feld || i.name === feld));
  if (!el) return false;
  el.focus();
  if (el.tagName === 'SELECT') { const o = [...el.options].find(o => o.text.trim() === wert || o.value === wert); if (!o) return false; el.value = o.value; }
  else if (el.type === 'checkbox') { el.checked = wert === 'ja'; }
  else { const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value').set; setter.call(el, wert); }
  el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); el.blur();
  return true; }`;

async function sichtbareKnoepfe() {
  return js(`[...document.querySelectorAll('button,[role=tab],a,label')].filter(b => b.getClientRects().length > 0)
    .map(b => (b.textContent || '').replace(/\\s+/g, ' ').trim().slice(0, 30)).filter(t => t).slice(0, 80).join(' | ')`);
}

const fehler = [];
for (const a of aufnahmen) {
  const name = a.datei ?? a.titel ?? JSON.stringify(a).slice(0, 60);
  try {
    if (a.springen) {
      const r = await fetch(`${server}/schulung/springen/${a.springen}`, { method: 'POST', headers: { 'X-Schulung-Schluessel': SCHLUESSEL } });
      if (!r.ok) throw new Error('Sprung fehlgeschlagen: ' + await r.text());
      angemeldet = null; await schlaf(1500);
    }
    if (a.konto) await anmelden(a.konto);
    if (a.stunden) a.vorstellen = Array(a.stunden).fill('+1 Stunde');
    if (a.vorstellen) {
      await groesse(false, 1000);
      await gehe('/spielzeit', 1500);
      for (const k of a.vorstellen) {
        if (!await js(`(${KLICK})(${JSON.stringify(k)})`)) throw new Error(`Spielzeit-Knopf „${k}“ fehlt`);
        await schlaf(k.includes('Tag') ? 6000 : 2500);
      }
      console.log('  Spielzeit:', await js(`document.querySelector('.vhs-card h2')?.innerText ?? ''`));
    }
    if (a.rolle) {
      const r = await js(`fetch('/rolle-wechseln?rolle=' + encodeURIComponent(${JSON.stringify(a.rolle)}), { method: 'POST' }).then(r => r.status)`);
      if (r >= 400) throw new Error('Rollenwechsel ' + r);
    }
    await groesse(!!a.app, a.hoehe ?? 1000, a.breite ?? 1600, a.dichte ?? 2);
    // "pfadAus": Adresse aus localStorage (z. B. eine nur einmal angezeigte Monitor-Adresse, vorher per "js" gemerkt)
    if (a.pfadAus) a.pfad = new URL(await js(`localStorage.getItem(${JSON.stringify(a.pfadAus)})`)).pathname;
    if (a.pfad) await gehe(a.pfad, a.warte ?? (a.app ? 4000 : 3000));
    for (const [feld, wert] of a.tippe ?? []) {
      if (!await js(`(${TIPPE})(${JSON.stringify(feld)}, ${JSON.stringify(wert)})`)) throw new Error(`Feld „${feld}“ nicht gefunden`);
      await schlaf(700);
    }
    for (const k of a.klick ?? []) {
      if (!await js(`(${KLICK})(${JSON.stringify(k)})`)) throw new Error(`Knopf „${k}“ nicht gefunden. Sichtbar: ${await sichtbareKnoepfe()}`);
      await schlaf(a.klickwarte ?? 2500);
    }
    if (a.js) { const r = await js(a.js); if (r !== undefined && r !== null && r !== true) console.log('  js:', typeof r === 'string' ? r : JSON.stringify(r)); }
    if (a.scroll) {
      const ok = await js(`(() => { const el = [...document.querySelectorAll('h1,h2,h3,summary,th,legend')].find(e => (e.innerText || '').trim().startsWith(${JSON.stringify(a.scroll)}));
        if (!el) return false; el.scrollIntoView({ block: 'start' }); window.scrollBy(0, -16); return true; })()`);
      if (!ok) throw new Error(`Überschrift „${a.scroll}“ nicht gefunden`);
      await schlaf(600);
    } else if (!a.lassen) { await js('window.scrollTo(0, 0)'); await schlaf(300); }
    if (a.erwarte && !await js(`document.body.innerText.includes(${JSON.stringify(a.erwarte)})`)) throw new Error(`Text „${a.erwarte}“ fehlt`);
    if (!a.datei) { console.log('·', name); continue; }
    // Verwaltung ohne Spielzeitleiste, Seitenleiste und Kopfzeile: nur der Inhaltsbereich (main), "voll": true behält alles
    const clip = a.app || a.voll ? null : await js(`(() => { const m = document.querySelector('main.vhs-content'), h = document.querySelector('header.vhs-top');
      if (!m) return null; const x = Math.round(m.getBoundingClientRect().left);
      const oben = Math.max(0, ...[...document.body.querySelectorAll('*')].filter(e => ['fixed', 'sticky'].includes(getComputedStyle(e).position) && e.getBoundingClientRect().top <= 1 && e.getBoundingClientRect().height < 200)
        .map(e => e.getBoundingClientRect().bottom), h ? h.getBoundingClientRect().bottom : 0);
      const y = Math.round(oben);
      return { x: x + scrollX, y: y + scrollY, width: innerWidth - x, height: innerHeight - y, scale: 1 }; })()`);
    const bild = await cdp('Page.captureScreenshot', clip ? { format: 'png', clip } : { format: 'png' });
    writeFileSync(join(ausgabe, a.datei), Buffer.from(bild.data, 'base64'));
    console.log('✓', a.datei);
  } catch (e) {
    fehler.push(`${name}: ${e.message}`);
    console.log('✗', name, e.message);
    if (a.pflicht) break;
  }
}
ws.close(); chrome.kill();
if (fehler.length) { console.error('Fehler:\n' + fehler.join('\n')); process.exit(1); }
