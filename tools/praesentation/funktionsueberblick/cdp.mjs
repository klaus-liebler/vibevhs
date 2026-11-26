import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
// Kleiner Headless-Edge/Chrome-Treiber über das DevTools-Protokoll (ohne Zusatzpakete) für aufnahme.mjs.
import { existsSync } from 'node:fs';
export const basis = (process.env.VIBEVHS_URL ?? 'http://localhost:5199/').replace(/\/?$/, '/');
const browserPfad = process.env.CHROME ?? ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  '/usr/bin/chromium', '/usr/bin/google-chrome'].find((p) => existsSync(p));
export const warte = (ms) => new Promise((r) => setTimeout(r, ms));
export async function start() {
  const port = 9400 + Math.floor(Math.random() * 400);
  const profil = mkdtempSync(join(tmpdir(), 'vhs-shots-'));
  const browser = spawn(browserPfad, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profil}`, '--no-first-run', '--disable-extensions', '--hide-scrollbars', '--window-size=1440,900', 'about:blank'], { stdio: 'ignore' });
  let ziel;
  for (let i = 0; i < 50 && !ziel; i++) { try { ziel = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json(); } catch { await warte(200); } }
  const ws = new WebSocket(ziel.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener('open', r, { once: true }));
  let nr = 0; const offen = new Map();
  ws.addEventListener('message', (e) => { const m = JSON.parse(e.data); if (m.id && offen.has(m.id)) { offen.get(m.id)(m); offen.delete(m.id); } });
  const cdp = (method, params = {}) => new Promise((r) => { const i = ++nr; offen.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
  const js = async (a) => { const r = await cdp('Runtime.evaluate', { expression: a, awaitPromise: true, returnByValue: true }); return r.result?.exceptionDetails ? undefined : r.result?.result?.value; };
  const bis = async (a, ms = 10000) => { const ende = Date.now() + ms; while (Date.now() < ende) { const v = await js(a); if (v) return v; await warte(200); } return null; };
  await cdp('Page.enable');
  const groesse = async (w, h, mobil = false) => cdp('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: mobil ? 3 : 1.5, mobile: mobil });
  await groesse(1440, 900);
  const geh = async (pfad, ms = 2500) => { await cdp('Page.navigate', { url: pfad.startsWith('http') ? pfad : basis + pfad.replace(/^\//, '') }); await warte(400); await bis('document.readyState==="complete"'); await warte(ms); };
  const anmelden = async (login) => {
    await cdp('Network.enable'); await cdp('Network.clearBrowserCookies');
    await geh('anmelden', 500);
    await js(`(()=>{const b=document.querySelector('button[value="${login}"]'); b.form.requestSubmit(b); return true})()`);
    await warte(1500); await bis('document.readyState==="complete"'); await warte(1500);
  };
  const shot = async (datei, { voll = false } = {}) => {
    await js(`document.querySelectorAll('.vhs-tour, .vhs-tour-blase').forEach(e=>e.remove())`);
    const r = await cdp('Page.captureScreenshot', { format: 'png', captureBeyondViewport: voll });
    writeFileSync(join('shots', datei), Buffer.from(r.result.data, 'base64'));
    console.log('shot', datei);
  };
  return { cdp, js, bis, geh, anmelden, shot, groesse, ende: () => browser.kill() };
}
