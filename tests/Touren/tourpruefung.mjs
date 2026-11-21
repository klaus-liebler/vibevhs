// Tourprüfung: spielt alle geführten Touren (wwwroot/touren/abnahme.json, tour.js) im Headless-Browser durch.
//
//   node tests/Touren/tourpruefung.mjs [basis-url] [tour-id …]
//
// Voraussetzung: laufende VibeVHS-Instanz mit Demodaten und Abnahmemodus (Standard http://localhost:5199, eigene DB!).
// Je Schritt: Ziel muss erscheinen (≤ 8 s); dann tut der Prüfer, was die Sprechblase verlangt (klicken, Wert eintragen,
// „Weiter“), und erwartet, dass die Tour weiterschaltet. Fehlschläge werden gemeldet und der Schritt übersprungen.
// Ausgabe: je Tour die Schritte mit ok/FEHLER; Exitcode 1 bei Fehlern. Touren bauen aufeinander auf (Reihenfolge wie Drehbuch).
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const basis = (process.argv[2] ?? 'http://localhost:5199').replace(/\/?$/, '/');
const nur = process.argv.slice(3);
const browserPfad = process.env.BROWSER ?? 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const port = 9400 + Math.floor(Math.random() * 400);
const profil = mkdtempSync(join(tmpdir(), 'vhs-touren-'));
const browser = spawn(browserPfad, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profil}`, '--no-first-run', '--disable-extensions', '--window-size=1400,1000', 'about:blank'], { stdio: 'ignore' });
const warte = (ms) => new Promise((r) => setTimeout(r, ms));

let ziel;
for (let i = 0; i < 50 && !ziel; i++) { try { ziel = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json(); } catch { await warte(200); } }
const ws = new WebSocket(ziel.webSocketDebuggerUrl);
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let nr = 0; const offen = new Map();
ws.addEventListener('message', (e) => { const m = JSON.parse(e.data); if (m.id && offen.has(m.id)) { offen.get(m.id)(m); offen.delete(m.id); } });
const cdp = (method, params = {}) => new Promise((r) => { const i = ++nr; offen.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const js = async (a) => {
  const r = await cdp('Runtime.evaluate', { expression: a, awaitPromise: true, returnByValue: true });
  return r.result?.exceptionDetails ? undefined : r.result?.result?.value;
};
const bis = async (ausdruck, ms) => { const ende = Date.now() + ms; while (Date.now() < ende) { const v = await js(ausdruck); if (v) return v; await warte(200); } return null; };

// Wert so setzen, wie es eine Eingabe täte (Blazor/Radzen hören auf input/change); Wrapper (.rz-numeric …) → inneres Feld
const EINTRAGEN = `(wert) => {
  let el = window.vhsTour.ziel();
  if (el && !('value' in el && el.tagName !== 'BUTTON')) el = el.querySelector('input:not([type=hidden]), textarea');
  if (!el) return false;
  el.focus();
  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value')?.set;
  setter ? setter.call(el, wert) : (el.value = wert);
  el.dispatchEvent(new Event('input', { bubbles: true }));
  el.dispatchEvent(new Event('change', { bubbles: true }));
  el.blur();
  return true;
}`;

await cdp('Page.navigate', { url: basis + 'anmelden' });
await bis('!!window.vhsTour', 15000);
const touren = (await js('window.vhsTour.touren().then(t => t.map(x => ({ id: x.id, titel: x.titel, n: x.schritte.length })))')) ?? [];
if (!touren.length) { console.error('Keine Touren geladen (touren/abnahme.json?)'); browser.kill(); process.exit(1); }

let fehlerGesamt = 0;
let hinweise = 0;
for (const tour of touren.filter((t) => !nur.length || nur.includes(t.id))) {
  console.log(`\n== ${tour.titel} (${tour.n} Schritte)`);
  // Abmelden, damit jede Tour mit dem Kontowechsel beginnt (prüft auch diese Schritte)
  await cdp('Network.clearBrowserCookies');
  await cdp('Page.navigate', { url: basis + 'anmelden' });
  await bis('!!window.vhsTour', 15000);
  await js(`window.vhsTour.starten(${JSON.stringify(tour.id)})`);
  await warte(1500);

  let runden = 0;
  for (;;) {
    if (++runden > tour.n + 6) { console.log('  FEHLER: Tour endet nicht'); fehlerGesamt++; break; }
    const z = await bis('window.vhsTour && (window.vhsTour.zustand() ? JSON.stringify(window.vhsTour.zustand()) : "ENDE")', 15000);
    if (!z || z === 'ENDE') break;
    const { i } = JSON.parse(z);
    if (i >= tour.n) break; // letzter Schritt war ein Klick mit Seitenwechsel – Tour ist fertig
    const schritt = await bis('window.vhsTour.schritt() && JSON.stringify(window.vhsTour.schritt())', 8000);
    if (!schritt) { console.log(`  ${i + 1}: FEHLER Schritt nicht geladen`); fehlerGesamt++; break; }
    const s = JSON.parse(schritt);
    const kurz = s.text.replace(/\*\*/g, '').slice(0, 70);

    let gefunden = true;
    if (s.ziel) gefunden = !!(await bis('!!window.vhsTour.ziel()', 8000));
    const weiter = await js('window.vhsTour.zustand()?.i');
    if (weiter !== i) { console.log(`  ${i + 1}: ok (übersprungen/automatisch) – ${kurz}`); continue; }

    if (!gefunden && s.bedingt) {
      console.log(`  ${i + 1}: Hinweis – Ziel fehlt, setzt voraus: ${s.bedingt} – ${kurz}`);
      hinweise++;
    } else if (!gefunden) {
      const pfad = await js('location.pathname');
      console.log(`  ${i + 1}: FEHLER Ziel nicht gefunden ${JSON.stringify(s.ziel)} auf ${pfad} – ${kurz}`);
      fehlerGesamt++;
    } else if (s.pruefer?.auswahl) {
      // Auswahlliste (Radzen): öffnen und den Eintrag wählen, den die Sprechblase nennt
      await js('window.vhsTour.ziel().click()');
      await warte(500);
      const gewaehlt = await js(`(() => { const o = [...document.querySelectorAll('.rz-dropdown-item, [role=option]')].find((x) => x.offsetParent && x.textContent.includes(${JSON.stringify(s.pruefer.auswahl)})); if (!o) return false; o.click(); return true; })()`);
      if (!gewaehlt) { console.log(`  ${i + 1}: FEHLER Auswahl „${s.pruefer.auswahl}“ nicht gefunden – ${kurz}`); fehlerGesamt++; }
      await warte(500);
    } else if (s.weiter === 'klick') {
      await js('window.vhsTour.ziel().click()');
    } else if (s.weiter === 'eingabe') {
      await js(`(${EINTRAGEN})(${JSON.stringify(s.pruefer?.eingabe ?? s.wert ?? 'Test durch Tourprüfung')})`);
    }

    if (!gefunden || s.weiter === 'knopf' || s.weiter === 'seite') {
      await js(`document.querySelector('#vhs-tour-blase [data-tour-aktion="weiter"]')?.click()`);
    }
    const ok = await bis(`(() => { const z = window.vhsTour && window.vhsTour.zustand(); return !z || z.i !== ${i}; })()`, 10000);
    if (gefunden && !ok && s.bedingt) {
      console.log(`  ${i + 1}: Hinweis – schaltet nicht weiter, setzt voraus: ${s.bedingt} – ${kurz}`);
      hinweise++;
      await js(`document.querySelector('#vhs-tour-blase [data-tour-aktion="weiter"]')?.click()`);
      await warte(800);
    } else if (gefunden) {
      if (ok) console.log(`  ${i + 1}: ok – ${kurz}`);
      else {
        const grund = await js(`(() => { const el = window.vhsTour.ziel(); if (!el) return ''; const b = el.closest('button') || el; return b.disabled ? ' – Ziel gesperrt: ' + (b.closest('[data-why]')?.dataset.why || b.title || '?') : ''; })()`);
        console.log(`  ${i + 1}: FEHLER schaltet nicht weiter (${s.weiter})${grund ?? ''} – ${kurz}`);
        fehlerGesamt++;
        await js(`document.querySelector('#vhs-tour-blase [data-tour-aktion="weiter"]')?.click()`);
        await warte(800);
      }
    }
  }
}

console.log(`\n${fehlerGesamt === 0 ? 'Alle Touren ohne Fehler.' : `${fehlerGesamt} Fehler.`}${hinweise ? ` ${hinweise} Hinweis(e) zu Schritten, die echte Vorarbeit brauchen.` : ''}`);
browser.kill();
try { rmSync(profil, { recursive: true, force: true }); } catch { /* Browser hält noch Dateien */ }
process.exit(fehlerGesamt ? 1 : 0);
