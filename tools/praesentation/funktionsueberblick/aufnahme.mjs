// Alle Bildschirmfotos für den Funktionsüberblick in einem Lauf – gegen eine eigene Instanz mit Demodaten (siehe README.md).
//   node aufnahme.mjs [schritt …]     ohne Angabe alle: verwaltung suche aufgaben integration betrieb kursneu mobil dozierende shop
// Umgebung: VIBEVHS_URL (Vorgabe http://localhost:5199/), DOZ_LOGIN = Anmeldelink einer Honorarkraft (sonst Testkonto ruben.daubner)
import { start, warte } from './cdp.mjs';
import sharp from 'sharp';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
process.chdir(dirname(fileURLToPath(import.meta.url))); // shots/ und crops/ liegen neben dem Skript
mkdirSync('shots', { recursive: true }); mkdirSync('crops', { recursive: true });

const b = await start();
const nur = process.argv.slice(2);
const schritt = async (name, f) => { if (nur.length && !nur.includes(name)) return; try { await f(); } catch (e) { console.log('FEHLER', name, e.message); } };
const link = (expr) => b.js(`Array.from(document.querySelectorAll('a')).filter(a => ${expr}).map(a=>a.getAttribute('href'))[0]`);
const rolle = async (text) => { await b.js(`(()=>{const s=document.querySelector('#rolle-wahl'); if(!s) return false; const o=[...s.options].find(o=>o.textContent.trim()===${JSON.stringify(text)}); if(!o) return false; s.value=o.value; s.form.submit(); return true})()`); await warte(2500); };
const feld = (label, wert) => b.js(`(()=>{const f=[...document.querySelectorAll('.rz-form-field')].find(f=>f.querySelector('label')?.textContent.trim().startsWith(${JSON.stringify(label)})); const el=f.querySelector('input'); el.focus(); el.value=${JSON.stringify(wert)}; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); el.blur(); return el.value})()`);
const scroll = (sel, txt) => b.js(`(()=>{const e=[...document.querySelectorAll(${JSON.stringify(sel)})].find(e=>e.textContent.includes(${JSON.stringify(txt)})); e.scrollIntoView({block:'start'}); window.scrollBy(0,-90); return true})()`);
const ausschnitt = async (c, datei, k = 1.5) => {
  const s = await b.cdp('Page.captureScreenshot', { format: 'png' });
  await sharp(Buffer.from(s.result.data, 'base64')).extract({ left: Math.round(c.x * k), top: Math.round(c.y * k), width: Math.round(c.w * k), height: Math.round(c.h * k) }).png().toFile(datei);
  console.log('ausschnitt', datei);
};

const VERWALTUNG = 'sonja.eickelberg', LEITUNG = 'kai.luedeking', IK = 'sylvia.bergmann', IT = 'amy.behrens', FBL = 'gisela.junkerholt';

await schritt('verwaltung', async () => {
  await b.anmelden(VERWALTUNG);
  await b.geh('', 3000); await b.shot('02_start.png');
  await b.geh('personen?q=Anna%20Becker', 3000);
  const p = await link(`/^\\/?personen\\/[0-9a-f-]{36}/.test(a.getAttribute('href')||'') && a.textContent.includes('Anna Becker')`);
  console.log('anna', p);
  await b.geh(p, 3000); await b.shot('04_person.png');
  await b.geh(p.replace(/\?.*/, '') + '/historie', 3000); await b.shot('05_historie.png');
  await b.geh('kurse', 3000); await b.shot('07_kurse.png');
  await b.geh('kurse/neu', 3000); await b.shot('09_kursneu.png');
  await b.geh('raumplan', 3500); await b.shot('10_raumplan.png');
  await b.geh('kurse?q=26H-801', 3000);
  const k = await link(`/^\\/?kurse\\/[0-9a-f-]{36}/.test(a.getAttribute('href')||'') && a.textContent.includes('Yoga')`); console.log('yoga', k);
  if (k) { await b.geh(k, 3000); await b.shot('08b_kurs_yoga.png'); }
  await b.geh('anmeldung?person=' + p.match(/personen\/([0-9a-f-]{36})/)[1], 3500); await b.shot('11_anmeldung.png');
  await b.geh('anwesenheit', 3000);
  const a = await link(`/^\\/?anwesenheit\\/[0-9a-f]{8}/.test(a.getAttribute('href')||'')`); console.log('anw', a);
  if (a) { await b.geh(a, 3500); await b.shot('13_anwesenheit_termin.png'); }
  for (const [pf, f] of [['postausgang', '16_postausgang.png'], ['vorlagen', '17_vorlagen.png'], ['protokoll', '18_protokoll.png'], ['datenschutz', '19_datenschutz.png'], ['hilfe', '23_hilfe.png']]) { await b.geh(pf, 3000); await b.shot(f); }
  await rolle('Buchhaltung'); await b.geh('finanzen', 3500); await b.shot('15_finanzen.png'); await rolle('Verwaltungsleitung');
});

await schritt('suche', async () => {
  await b.anmelden(VERWALTUNG);
  await b.geh('', 3000);
  const begriffe = { akzent: 'celik ayse', ort: 'lehrkuche', kursnr: '26h-80', frueher: 'schmidt anna', umschrift: 'mueller', datum: '12.08.1984' };
  for (const [name, q] of Object.entries(begriffe)) {
    await b.js(`(()=>{const sb=document.querySelector('.vhs-search'); for (const m of document.querySelectorAll('main > *')) if (!m.contains(sb)) m.style.visibility='hidden'; sb.style.setProperty('flex','0 0 330px','important'); sb.style.setProperty('width','330px','important'); const el=sb.querySelector('input'); el.focus(); el.value=${JSON.stringify(q)}; el.dispatchEvent(new Event('input',{bubbles:true})); return true})()`);
    await warte(2000);
    console.log('## ' + q + ' => ' + ((await b.js(`(document.querySelector('.vhs-ergebnisse')||{}).innerText`)) || '').replace(/\n+/g, ' | ').slice(0, 200));
    const c = JSON.parse(await b.js(`(()=>{const a=document.querySelector('.vhs-search').getBoundingClientRect(); const e=(document.querySelector('.vhs-ergebnisse')||document.querySelector('.vhs-search')).getBoundingClientRect(); const y=Math.max(0,a.top-6); const x=Math.min(a.left,e.left)-6; return JSON.stringify({x:x,y:y,w:Math.max(a.right,e.right)-x+1,h:e.bottom-y+8})})()`));
    await ausschnitt(c, `crops/such_${name}.png`);
  }
});

await schritt('aufgaben', async () => { await b.anmelden(LEITUNG); await b.geh('aufgaben?ansicht=alle', 3000); await b.shot('14_aufgaben.png'); await b.geh('dashboard', 3500); await b.shot('30_dashboard_leitung.png'); });

await schritt('integration', async () => {
  await b.anmelden(IK); await rolle('Integrationskurs-Team'); await b.geh('integration', 3500);
  console.log('klick', await b.js(`(()=>{const e=[...document.querySelectorAll('main div[style*=pointer], main tr, main .rz-data-row')].find(e=>e.textContent.includes('26H-401')); if(!e) return false; e.click(); return true})()`));
  await warte(3000); await b.shot('31_integration.png');
});

await schritt('betrieb', async () => { await b.anmelden(IT); await b.geh('betrieb', 3500); await b.shot('32_betrieb.png'); });

await schritt('kursneu', async () => {
  await b.anmelden(FBL);
  await b.geh('kurse/neu', 3500);
  await feld('Titel', 'Italienisch A2 – Konversation'); await warte(600);
  await feld('Mindestzahl', '6'); await feld('Höchstzahl', '14'); await feld('Entgelt', '96'); await feld('ermäßigt', '72'); await warte(600);
  await b.js(`(()=>{const el=document.querySelector('input[placeholder^="Dozierende suchen"]'); el.focus(); el.value='sch'; el.dispatchEvent(new Event('input',{bubbles:true})); el.dispatchEvent(new Event('change',{bubbles:true})); el.blur(); return true})()`);
  await warte(2200);
  console.log('dozierende', await b.js(`[...document.querySelectorAll('button.vhs-chip, .vhs-why')].map(c=>c.textContent.trim()).join(' | ')`));
  await b.js(`(()=>{const c=[...document.querySelectorAll('button.vhs-chip')].find(c=>c.textContent.includes('Klemens Schacht')); if(c) c.click(); return !!c})()`); await warte(1500);
  await b.js(`document.activeElement?.blur(); window.scrollTo(0,0)`); await warte(300);
  await b.shot('kn_dozent.png');
  await b.js(`[...document.querySelectorAll('label.vhs-chip')].find(c=>c.textContent.trim()==='Di')?.click()`); await warte(500);
  await b.js(`[...document.querySelectorAll('label.vhs-chip')].find(c=>c.textContent.trim()==='Mo')?.click()`); await warte(800);
  await feld('Beginn', '18:30'); await feld('Dauer', '90'); await feld('Anzahl Termine', '12'); await warte(1200);
  console.log('räume', await b.js(`[...document.querySelectorAll('button.vhs-chip')].map(c=>c.textContent.trim()).filter(t=>/\\d\\.\\d/.test(t)).join(' | ')`));
  await b.js(`[...document.querySelectorAll('button.vhs-chip')].find(c=>c.textContent.includes('G 1.02'))?.click()`); await warte(800);
  await b.js(`[...document.querySelectorAll('button')].find(c=>c.textContent.includes('Vorschau berechnen'))?.click()`); await warte(3500);
  await scroll('section h2', 'Termine'); await warte(500);
  await b.shot('kn_termine.png');
  // Kursart-Auswahl als Ausschnitt
  await b.groesse(1440, 1100);
  await b.geh('kurse/neu', 3500);
  await b.js(`(()=>{const f=[...document.querySelectorAll('.rz-form-field')].find(f=>f.querySelector('label')?.textContent.trim()==='Kursart'); f.scrollIntoView({block:'start'}); window.scrollBy(0,-40); f.querySelector('.rz-dropdown').click(); return true})()`); await warte(1200);
  const c = JSON.parse(await b.js(`(()=>{const p=[...document.querySelectorAll('.rz-dropdown-panel, .rz-popup')].find(p=>p.getBoundingClientRect().height>0); const ul=p.querySelector('.rz-dropdown-items-wrapper, ul'); if (ul) { ul.style.maxHeight='none'; ul.scrollTop=0; } p.style.maxHeight='none'; const f=[...document.querySelectorAll('.rz-form-field')].find(f=>f.querySelector('label')?.textContent.trim()==='Kursart'); const a=f.getBoundingClientRect(); const e=p.getBoundingClientRect(); return JSON.stringify({x:a.left-2,y:a.top-14,w:a.width+4,h:e.bottom-a.top+22})})()`));
  await warte(600);
  await ausschnitt(c, 'crops/kn_kursart.png');
  await b.groesse(1440, 900);
  // 10er-Karte: Kursdetail mit Kontingentstand
  await b.geh('kurse?q=26H-704', 3000);
  const k = await link(`/^\\/?kurse\\/[0-9a-f-]{36}/.test(a.getAttribute('href')||'')`);
  await b.geh(k, 3500);
  await b.js(`(()=>{const p=[...document.querySelectorAll('p, h2, h3')].find(p=>p.textContent.includes('Regel dieses Kurses')||p.textContent.includes('Kontingent')); if(!p) return false; p.scrollIntoView({block:'start'}); window.scrollBy(0,-100); return true})()`);
  await warte(600); await b.shot('kn_pilates_einbuchung.png');
  // Ausschnitt: acht Teilnehmendenzeilen mit Kontingentstand
  await b.js(`(()=>{const r=[...document.querySelectorAll('tr')].filter(t=>t.textContent.includes('eingebucht')); r[0].scrollIntoView({block:'center'}); return r.length})()`); await warte(600);
  const z = JSON.parse(await b.js(`(()=>{const r=[...document.querySelectorAll('tr')].filter(t=>t.textContent.includes('eingebucht')).slice(0,8); const a=r[0].getBoundingClientRect(), e=r[r.length-1].getBoundingClientRect(); return JSON.stringify({x:a.left,y:a.top,w:a.width,h:e.bottom-a.top})})()`));
  await ausschnitt(z, 'crops/kn_kontingent.png');
  const m = await sharp('crops/kn_kursart.png').metadata();
  await sharp('crops/kn_kursart.png').extract({ left: 0, top: 0, width: m.width, height: Math.min(m.height, Math.round(m.width * 430 / 437)) }).png().toFile('crops/kn_kursart_kurz.png');
});

await schritt('mobil', async () => {
  await b.groesse(390, 844, true);
  await b.anmelden('olena.kovalenko'); await b.geh('portal/teilnehmende', 3000); await b.shot('40_tn_start.png');
  await b.geh('portal/teilnehmende/buchungen', 3000); await b.shot('41_tn_buchungen.png');
  await b.geh('portal/teilnehmende/buchen', 3000); await b.shot('42_tn_buchen.png');
  await b.cdp('Network.clearBrowserCookies');
  await b.geh('k/26H-101', 3000); await b.shot('47_kursqr.png');
  await b.groesse(1440, 900);
});

await schritt('dozierende', async () => {
  // Kursleitung mit Honorarvertrag: Anmeldelink über die Verwaltung erzeugen ist nicht nötig – Testkonto, falls vorhanden
  const login = process.env.DOZ_LOGIN;
  await b.groesse(390, 844, true);
  if (login?.startsWith('http')) {
    await b.geh(login, 1500);
    await b.js(`(()=>{const f=document.querySelector('form'); f.requestSubmit(f.querySelector('button')); return true})()`); await warte(3000);
  } else await b.anmelden(login ?? 'ruben.daubner');
  await b.geh('portal/dozierende', 3000); await b.shot('43_doz_start.png');
  const t = await link(`(a.getAttribute('href')||'').includes('portal/dozierende/termin/') && !a.getAttribute('href').endsWith('.ics')`); console.log('termin', t);
  if (t) { await b.geh(t, 3000); await b.shot('46_doz_termin.png'); }
  await b.geh('portal/dozierende/honorar', 3000);
  await b.js(`(()=>{const d=[...document.querySelectorAll('details')].filter(d=>d.querySelector('summary')?.textContent.startsWith('Positionen')); if(d.length) d[Math.min(1,d.length-1)].open=true; return d.length})()`);
  await warte(500); await b.shot('45_doz_honorar.png');
  await b.groesse(1440, 900);
});

await schritt('shop', async () => { await b.groesse(1440, 900); await b.geh('shop/demo.html', 4000); await b.shot('50_shop.png'); });

b.ende();

// Handy-Zuschnitte (Seitenverhältnis des Telefonrahmens)
for (const f of ['40_tn_start', '41_tn_buchungen', '42_tn_buchen', '43_doz_start', '45_doz_honorar', '46_doz_termin', '47_kursqr']) {
  if (nur.length && !['mobil', 'dozierende'].some(n => nur.includes(n))) break;
  try {
    const m = await sharp(`shots/${f}.png`).metadata();
    await sharp(`shots/${f}.png`).extract({ left: 0, top: 0, width: m.width, height: Math.min(m.height, Math.round(m.width / 0.532)) }).png().toFile(`crops/${f}.png`);
  } catch (e) { console.log('zuschnitt', f, e.message); }
}
