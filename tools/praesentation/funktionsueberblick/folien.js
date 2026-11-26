const pptxgen = require('pptxgenjs');
const sharp = require('sharp');
const path = require('path');
const { themaAnwenden } = require('./thema');

// Aufruf: npm run folien  (vorher npm run symbole und npm run aufnahme, siehe README.md)
const HIER = __dirname;
const REPO = path.resolve(HIER, '..', '..', '..');
const LOGO = path.join(REPO, 'docs', 'praesentation', 'logo', 'vibevhs-icon.png');

const THEME = {
  name: 'VibeVHS',
  headFontFace: 'Segoe UI Semibold',
  bodyFontFace: 'Segoe UI',
  colors: {
    dk1: '0A3D4A', lt1: 'FFFFFF', dk2: '172629', lt2: 'F2F7F7',
    accent1: '0E7C86', accent2: 'FF7A59', accent3: '6FDCCF', accent4: '57696D', accent5: 'D6ECEE', accent6: 'FFE3DA',
    hlink: '0E7C86', folHlink: '57696D',
  },
};
const HEX = THEME.colors;
const OUT = process.argv[2] || path.join(REPO, 'docs', 'praesentation', 'VibeVHS-Praesentation.pptx');
const S = (f) => path.join(HIER, 'shots', f);
const CR = (f) => path.join(HIER, 'crops', f);
const I = (n) => path.join(HIER, 'symbole', n + '.png');

(async () => {
  const pres = new pptxgen();
  pres.layout = 'LAYOUT_WIDE'; // 13.333 x 7.5
  pres.title = 'VibeVHS – Funktionsüberblick';
  pres.author = 'Klaus Liebler';
  pres.company = 'vhs Emsdetten Greven Saerbeck';
  pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
  const C = pres.SchemeColor;

  // ---------- Layouts ----------
  pres.defineSlideMaster({
    title: 'TITEL_DUNKEL',
    background: { color: C.text1 },
    objects: [],
  });
  pres.defineSlideMaster({
    title: 'INHALT',
    background: { color: C.background2 },
    margin: [0.5, 0.6, 0.6, 0.6],
    objects: [
      { image: { path: LOGO, x: 0.6, y: 6.93, w: 0.3, h: 0.3 } },
      { text: { text: 'VibeVHS · Funktionsüberblick · Stand November 2026', options: { x: 1.0, y: 6.93, w: 7, h: 0.3, fontSize: 10, color: C.accent4, margin: 0, valign: 'middle' } } },
      { placeholder: { options: { name: 'kicker', type: 'body', x: 0.6, y: 0.32, w: 9, h: 0.3, fontSize: 11, bold: true, color: C.accent2, charSpacing: 2, margin: 0, align: 'left' }, text: '' } },
      { placeholder: { options: { name: 'title', type: 'title', x: 0.6, y: 0.62, w: 12.1, h: 0.75, fontSize: 32, color: C.text1, fontFace: THEME.headFontFace, margin: 0, valign: 'top', align: 'left' }, text: '' } },
    ],
    slideNumber: { x: 12.2, y: 6.93, w: 0.55, h: 0.3, fontSize: 10, color: C.accent4, align: 'right' },
  });

  // ---------- Bausteine ----------
  const dims = {};
  const groesse = async (p) => (dims[p] ??= await sharp(p).metadata());

  // Screenshot im Browserrahmen; liefert das Bildrechteck für Markierungen
  async function browser(slide, file, x, y, w, name) {
    const m = await groesse(file);
    const bar = 0.28;
    const ih = (w - 0.08) * m.height / m.width;
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, {
      x, y, w, h: bar + ih + 0.04, rectRadius: 0.1, fill: { color: C.text1 }, line: { color: C.text1, width: 0.5 },
      shadow: { type: 'outer', color: '0A3D4A', blur: 14, offset: 4, angle: 90, opacity: 0.28 }, objectName: `${name}-rahmen`,
    });
    [0, 1, 2].forEach((i) => slide.addShape(pres.shapes.OVAL, {
      x: x + 0.14 + i * 0.16, y: y + 0.09, w: 0.1, h: 0.1, fill: { color: [C.accent2, C.accent3, C.background1][i] }, line: { type: 'none' }, objectName: `${name}-punkt${i}`,
    }));
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, {
      x: x + 0.62, y: y + 0.06, w: Math.min(3.2, w * 0.4), h: 0.16, rectRadius: 0.08, fill: { color: '1E5662' }, line: { type: 'none' }, objectName: `${name}-adresse`,
    });
    const r = { x: x + 0.04, y: y + bar, w: w - 0.08, h: ih };
    slide.addImage({ path: file, ...r, altText: name, objectName: name });
    return r;
  }

  // Smartphone-Rahmen
  function phone(slide, file, x, y, w, name) {
    const sw = w - 0.16;
    const sh = sw / 0.532;
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, {
      x, y, w, h: sh + 0.42, rectRadius: 0.28, fill: { color: C.text1 }, line: { color: C.text1, width: 0.5 },
      shadow: { type: 'outer', color: '0A3D4A', blur: 14, offset: 4, angle: 90, opacity: 0.3 }, objectName: `${name}-geraet`,
    });
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: x + w / 2 - 0.3, y: y + 0.08, w: 0.6, h: 0.08, rectRadius: 0.04, fill: { color: '1E5662' }, line: { type: 'none' }, objectName: `${name}-lautsprecher` });
    const r = { x: x + 0.08, y: y + 0.24, w: sw, h: sh };
    slide.addImage({ path: file, ...r, altText: name, objectName: name });
    return r;
  }

  // Korall-Nummer auf dem Screenshot
  function marker(slide, r, fx, fy, n) {
    const d = 0.38;
    slide.addShape(pres.shapes.OVAL, {
      x: r.x + fx * r.w - d / 2, y: r.y + fy * r.h - d / 2, w: d, h: d, fill: { color: C.accent2 }, line: { color: C.background1, width: 2 },
      shadow: { type: 'outer', color: '000000', blur: 6, offset: 2, angle: 90, opacity: 0.35 }, objectName: `marker-${n}`,
    });
    slide.addText(String(n), { x: r.x + fx * r.w - d / 2, y: r.y + fy * r.h - d / 2, w: d, h: d, fontSize: 13, bold: true, color: C.background1, align: 'center', valign: 'middle', margin: 0, isTextBox: true, objectName: `marker-${n}-zahl` });
  }

  // Nummerierte Punkte links
  function punkte(slide, items, x, y, w, { abstand = 1.15, nummern = true } = {}) {
    items.forEach((it, i) => {
      const yy = y + i * abstand;
      if (nummern) {
        slide.addShape(pres.shapes.OVAL, { x, y: yy + 0.02, w: 0.38, h: 0.38, fill: { color: C.accent2 }, line: { type: 'none' }, objectName: `punkt-${i + 1}` });
        slide.addText(String(i + 1), { x, y: yy + 0.02, w: 0.38, h: 0.38, fontSize: 13, bold: true, color: C.background1, align: 'center', valign: 'middle', margin: 0, isTextBox: true, objectName: `punkt-${i + 1}-zahl` });
      }
      const tx = nummern ? x + 0.55 : x;
      slide.addText([
        { text: it[0], options: { fontSize: 16, bold: true, color: C.text1, breakLine: true } },
        { text: it[1], options: { fontSize: 14, color: C.text2 } },
      ], { x: tx, y: yy, w: w - (tx - x), h: abstand - 0.1, valign: 'top', margin: 0, paraSpaceAfter: 3, isTextBox: true, objectName: `punkt-${i + 1}-text` });
    });
  }

  // Karte mit Icon im Kreis
  function karte(slide, x, y, w, h, icon, titel, text, { dunkel = false } = {}) {
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, {
      x, y, w, h, rectRadius: 0.15, fill: { color: dunkel ? '12505E' : C.background1 }, line: { type: 'none' },
      shadow: dunkel ? undefined : { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.12 }, objectName: `karte-${titel}`,
    });
    slide.addShape(pres.shapes.OVAL, { x: x + 0.3, y: y + 0.3, w: 0.7, h: 0.7, fill: { color: dunkel ? C.accent2 : C.accent1 }, line: { type: 'none' }, objectName: `karte-${titel}-kreis` });
    slide.addImage({ path: I(icon), x: x + 0.45, y: y + 0.45, w: 0.4, h: 0.4, altText: titel, objectName: `karte-${titel}-icon` });
    slide.addText([
      { text: titel, options: { fontSize: 17, bold: true, color: dunkel ? C.background1 : C.text1, breakLine: true } },
      { text, options: { fontSize: 14, color: dunkel ? 'D6ECEE' : C.text2 } },
    ], { x: x + 0.3, y: y + 1.2, w: w - 0.6, h: h - 1.4, valign: 'top', margin: 0, paraSpaceAfter: 4, isTextBox: true, objectName: `karte-${titel}-text` });
  }

  // Bild in heller Karte, eingepasst (für Ausschnitte ohne Browserrahmen)
  async function bildKarte(slide, file, x, y, w, h, titel, eingabe, name) {
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.12, fill: { color: C.background1 }, line: { type: 'none' },
      shadow: { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.14 }, objectName: `${name}-karte` });
    slide.addText([
      { text: titel + '  ', options: { bold: true, color: C.text1 } },
      { text: eingabe, options: { color: C.accent2, fontFace: 'Consolas', bold: true } },
    ], { x: x + 0.2, y: y + 0.12, w: w - 0.4, h: 0.4, fontSize: 14, margin: 0, valign: 'middle', isTextBox: true, objectName: `${name}-titel` });
    const m = await groesse(file);
    const maxW = w - 0.4, maxH = h - 0.75;
    let iw = maxW, ih = iw * m.height / m.width;
    if (ih > maxH) { ih = maxH; iw = ih * m.width / m.height; }
    slide.addImage({ path: file, x: x + (w - iw) / 2, y: y + 0.6, w: iw, h: ih, altText: `${titel}: ${eingabe}`, objectName: name });
  }

  // Kompakte Karte: Icon links neben dem Titel, Text darunter
  function karteKompakt(slide, x, y, w, h, icon, titel, text, { schrift = 14 } = {}) {
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.12, fill: { color: C.background1 }, line: { type: 'none' },
      shadow: { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.12 }, objectName: `kk-${titel}` });
    slide.addShape(pres.shapes.OVAL, { x: x + 0.2, y: y + 0.2, w: 0.52, h: 0.52, fill: { color: C.accent1 }, line: { type: 'none' }, objectName: `kk-${titel}-kreis` });
    slide.addImage({ path: I(icon), x: x + 0.31, y: y + 0.31, w: 0.3, h: 0.3, altText: titel, objectName: `kk-${titel}-icon` });
    slide.addText(titel, { x: x + 0.85, y: y + 0.2, w: w - 1.0, h: 0.52, fontSize: 16, bold: true, color: C.text1, valign: 'middle', margin: 0, isTextBox: true, objectName: `kk-${titel}-titel` });
    slide.addText(text, { x: x + 0.2, y: y + 0.85, w: w - 0.4, h: h - 0.95, fontSize: schrift, color: C.text2, valign: 'top', margin: 0, isTextBox: true, objectName: `kk-${titel}-text` });
  }

  // Spalte mit Icon, Titel und Liste
  function spalte(s, x, icon, titel, zeilen, dunkel) {
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: 1.7, w: 3.85, h: 4.85, rectRadius: 0.15, fill: { color: dunkel ? C.text1 : C.background1 }, line: { type: 'none' }, shadow: { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.12 }, objectName: `spalte-${titel}` });
    s.addShape(pres.shapes.OVAL, { x: x + 0.3, y: 2.0, w: 0.7, h: 0.7, fill: { color: dunkel ? C.accent2 : C.accent1 }, line: { type: 'none' }, objectName: `spalte-${titel}-kreis` });
    s.addImage({ path: I(icon), x: x + 0.45, y: 2.15, w: 0.4, h: 0.4, altText: titel, objectName: `spalte-${titel}-icon` });
    s.addText(titel, { x: x + 1.15, y: 2.0, w: 2.5, h: 0.7, fontSize: 18, bold: true, color: dunkel ? C.background1 : C.text1, valign: 'middle', margin: 0, isTextBox: true, objectName: `spalte-${titel}-titel` });
    s.addText(zeilen.map((z, i) => ({ text: z, options: { bullet: { indent: 14 }, breakLine: i < zeilen.length - 1 } })), {
      x: x + 0.3, y: 2.95, w: 3.3, h: 3.4, fontSize: 14, color: dunkel ? 'D6ECEE' : C.text2, valign: 'top', margin: 0, paraSpaceAfter: 8, isTextBox: true, objectName: `spalte-${titel}-liste` });
  }

  function inhalt(kicker, titel, abschnitt) {
    const s = pres.addSlide({ masterName: 'INHALT', sectionTitle: abschnitt });
    s.addText(kicker.toUpperCase(), { placeholder: 'kicker' });
    s.addText(titel, { placeholder: 'title' });
    return s;
  }

  // Wortmarke (Text, damit sie skaliert und in der Schrift des Themas bleibt)
  function wortmarke(slide, x, y, groesse, farbe) {
    slide.addText([
      { text: 'Vibe', options: { color: farbe } },
      { text: 'VHS', options: { color: C.accent2 } },
    ], { x, y, w: groesse * 0.075, h: groesse / 60, fontSize: groesse, fontFace: 'Segoe UI', bold: true, margin: 0, valign: 'middle', isTextBox: true, objectName: 'wortmarke' });
  }

  // ===================== Folien =====================
  // 1 Titel
  pres.addSection({ title: 'Einstieg' });
  {
    const s = pres.addSlide({ masterName: 'TITEL_DUNKEL', sectionTitle: 'Einstieg' });
    s.addShape(pres.shapes.OVAL, { x: 8.6, y: -2.6, w: 7.5, h: 7.5, fill: { color: '0E7C86', transparency: 70 }, line: { type: 'none' }, objectName: 'deko-kreis' });
    await browser(s, S('02_start.png'), 6.9, 1.55, 7.6, 'titel-screenshot');
    s.addImage({ path: LOGO, x: 0.75, y: 1.3, w: 1.25, h: 1.25, altText: 'VibeVHS-Logo', objectName: 'logo' });
    wortmarke(s, 0.75, 2.85, 66, C.background1);
    s.addText('Volkshochschule, einfach verwaltet', { x: 0.75, y: 4.0, w: 6, h: 0.5, fontSize: 22, color: 'BFE3E0', margin: 0, isTextBox: true, objectName: 'untertitel' });
    s.addText('Funktionsüberblick aus Sicht von Verwaltung, Kursleitungen und Teilnehmenden', { x: 0.75, y: 4.55, w: 5.8, h: 0.75, fontSize: 16, color: C.background1, margin: 0, isTextBox: true, objectName: 'zweck' });
    s.addText([
      { text: 'Pilot: vhs Emsdetten Greven Saerbeck', options: { breakLine: true } },
      { text: 'Stand: November 2026' },
    ], { x: 0.75, y: 5.9, w: 5.5, h: 0.7, fontSize: 13, color: '9CC9C6', margin: 0, isTextBox: true, objectName: 'stand' });
    s.addNotes('Begrüßung. VibeVHS ist eine webbasierte Verwaltung für Volkshochschulen, entwickelt mit der vhs Emsdetten Greven Saerbeck als Pilot. In den nächsten 30 Minuten zeige ich, was die Software heute aus Sicht der Nutzerinnen und Nutzer kann – Verwaltung, Kursleitungen und Teilnehmende. Alle Screenshots stammen aus dem aktuellen Stand mit fiktiven Testdaten.');
  }

  pres.addSection({ title: 'Überblick' });
  // 2 Ziele
  {
    const s = inhalt('Warum VibeVHS', 'Acht Versprechen an alle, die damit arbeiten', 'Überblick');
    const w = 2.85, g = 0.23, h = 1.92;
    const v = [
      ['touch', 'Einfach', 'Klare Seiten, Folgeschritte nach jeder Aktion und ein eingebauter Tutor mit geführten Touren.'],
      ['fertig', 'Mitdenkend', 'Prüft beim Planen selbst: Raumkonflikte, Ferien, Lehrbefähigung, Mindestteilnehmerzahl.'],
      ['auto', 'Entlastend', 'Rechnungen, Lastschrift, Mahnungen, Honorare, Erinnerungen, Nachrücken: alles automatisch.'],
      ['weiter', 'Verlässlich', 'Nichts bleibt liegen: Offenes wird zur Aufgabe – mit Vertretung und Eskalation.'],
      ['hub', 'Gut vernetzt', 'Angebunden an Webseite (TYPO3), BAMF-Abrechnung und XRechnung, dazu Excel-Exporte.'],
      ['schutz', 'Sicher', 'Datenschutz und Datensicherung eingebaut, GoBD-konforme Belege.'],
      ['tn', 'Offen für alle', 'Webshop, Portale für Teilnehmende und Kursleitungen, Check-in per QR – auch am Handy.'],
      ['sprache', 'Verständlich', 'Jede Mail und jeder Brief mit Übersetzung – in der Sprache der Empfänger.'],
    ];
    v.forEach(([icon, titel, text], i) => karteKompakt(s, 0.6 + (i % 4) * (w + g), 1.6 + Math.floor(i / 4) * (h + 0.2), w, h, icon, titel, text));
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 0.6, y: 5.85, w: 12.13, h: 0.75, rectRadius: 0.15, fill: { color: C.accent5 }, line: { type: 'none' }, objectName: 'grundsatz' });
    s.addText([{ text: 'Grundsatz: ', options: { bold: true, color: C.accent1 } }, { text: 'Die Routine erledigt VibeVHS – die Entscheidungen treffen Menschen.', options: { color: C.text1 } }], { x: 0.95, y: 5.85, w: 11.5, h: 0.75, fontSize: 18, valign: 'middle', margin: 0, isTextBox: true, objectName: 'grundsatz-text' });
    s.addNotes('Acht Versprechen ziehen sich durch die ganze Software. Einfach: klare Seiten, nach jeder Aktion die sinnvollen nächsten Schritte, und ein eingebauter Tutor führt mit Touren durch die Abläufe. Mitdenkend: Schon beim Anlegen eines Kurses prüft das System Raumkonflikte, Ferien und die Lehrbefähigung der Kursleitung; vor Kursbeginn prüft es die Mindestteilnehmerzahl. Entlastend: Wiederkehrende Arbeit wie Rechnungslauf, Lastschrift, Mahnvorschläge, Honorarentwürfe, Erinnerungen und das Nachrücken von der Warteliste läuft von selbst. Verlässlich: Was jemand entscheiden muss, landet als Aufgabe im richtigen Korb – mit Vertretung und Eskalation, wenn es liegen bleibt. Gut vernetzt: Der Webshop hängt an der vhs-Webseite (TYPO3), Integrationskurse werden mit dem BAMF abgerechnet, Rechnungen gehen als XRechnung hinaus, Listen lassen sich nach Excel exportieren. Sicher: Datenschutz ist Teil des Fachmodells, jede Nacht läuft eine Sicherung mit Wiederherstellungstest, Belege sind GoBD-konform. Offen für alle: Teilnehmende buchen im Webshop, Kursleitungen und Teilnehmende haben eigene Portale, der Check-in läuft per QR-Code am Handy. Verständlich: Jede Mail und jeder Brief kommt mit Übersetzungsmöglichkeit. Der Grundsatz dahinter: Die Software erledigt die Routine, Entscheidungen bleiben bei Menschen – etwa ob ein unterbelegter Kurs abgesagt wird.');
  }

  // 3 Für wen
  {
    const s = inhalt('Drei Zugänge', 'Eine Software, drei Perspektiven', 'Überblick');
    const w = 3.75, g = 0.3, y = 1.75, h = 2.6;
    karte(s, 0.6, y, w, h, 'verwaltung', 'Verwaltung', 'Acht Rollen von Leitung bis Buchhaltung – Rechte und Aufgaben folgen der aktiven Rolle.');
    karte(s, 0.6 + w + g, y, w, h, 'dozent', 'Kursleitungen', 'Dozierendenportal: Termine, Anwesenheit, Unterlagen und Honorar – auch am Smartphone.');
    karte(s, 0.6 + 2 * (w + g), y, w, h, 'tn', 'Teilnehmende', '„Meine vhs“ und Webshop: buchen, umbuchen, Unterlagen und Rechnungen abrufen.');
    s.addText('Rollen in der Verwaltung', { x: 0.6, y: 4.65, w: 6, h: 0.35, fontSize: 14, bold: true, color: C.text1, margin: 0, isTextBox: true, objectName: 'rollen-titel' });
    const rollen = ['Leitung', 'Verwaltungsleitung', 'Fachbereichsleitung', 'Teilnehmerservice', 'Buchhaltung', 'Integrationskurs-Team', 'Systemverwaltung', 'Prüfung'];
    let x = 0.6, yy = 5.1;
    rollen.forEach((r) => {
      const bw = 0.32 + r.length * 0.105;
      if (x + bw > 12.75) { x = 0.6; yy += 0.55; }
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: yy, w: bw, h: 0.42, rectRadius: 0.21, fill: { color: C.accent5 }, line: { type: 'none' }, objectName: `rolle-${r}` });
      s.addText(r, { x, y: yy, w: bw, h: 0.42, fontSize: 14, color: C.accent1, bold: true, align: 'center', valign: 'middle', margin: 0, isTextBox: true, objectName: `rolle-${r}-text` });
      x += bw + 0.15;
    });
    s.addNotes('Es gibt drei Zugänge. Die Verwaltung arbeitet in der Hauptanwendung; wer mehrere Rollen hat, wechselt die aktive Rolle oben rechts – Menü, Aufgabenkörbe und Rechte passen sich an. Kursleitungen haben ein eigenes, mobil nutzbares Portal. Teilnehmende buchen über den Webshop auf der vhs-Website und verwalten ihre Buchungen in „Meine vhs“. Die Anmeldung läuft im Testsystem über eine Testanmeldung; im Betrieb über die kommunale Benutzerverwaltung bzw. das Servicekonto.');
  }

  // 4 Agenda
  {
    const s = inhalt('Agenda', 'Ein Rundgang in fünf Stationen', 'Überblick');
    const st = [
      ['Arbeitsalltag', 'Startseite, Suche, Aufgaben, Personen'],
      ['Kurse & Teilnahme', 'Kurs anlegen, Raumplan, Anmeldung, Anwesenheit'],
      ['Geld & Förderung', 'Finanzen, Integrationskurse, Post'],
      ['Portale & Webshop', 'Teilnehmende, Kursleitungen, Online-Buchung'],
      ['Steuerung & Betrieb', 'Dashboard, Datenschutz, Hilfe'],
    ];
    const w = 2.3, g = 0.15;
    st.forEach(([t, d], i) => {
      const x = 0.6 + i * (w + g);
      s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: 1.9, w, h: 3.9, rectRadius: 0.15, fill: { color: i === 0 ? C.text1 : C.background1 }, line: { type: 'none' }, shadow: { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.12 }, objectName: `station-${i + 1}` });
      s.addText(String(i + 1).padStart(2, '0'), { x: x + 0.3, y: 2.15, w: 1.6, h: 1.0, fontSize: 48, bold: true, color: C.accent2, margin: 0, isTextBox: true, objectName: `station-${i + 1}-nr` });
      s.addText([
        { text: t, options: { fontSize: 18, bold: true, color: i === 0 ? C.background1 : C.text1, breakLine: true } },
        { text: d, options: { fontSize: 14, color: i === 0 ? 'D6ECEE' : C.text2 } },
      ], { x: x + 0.3, y: 3.4, w: w - 0.5, h: 2.2, valign: 'top', margin: 0, paraSpaceAfter: 6, isTextBox: true, objectName: `station-${i + 1}-text` });
    });
    s.addNotes('Der Rundgang folgt dem Weg durch einen typischen Tag: zuerst der Arbeitsalltag in der Verwaltung, dann Kurse und Teilnahme, danach Geld und Förderung, die Portale nach außen und zum Schluss Steuerung, Datenschutz und Betrieb. Am Ende ein kurzer Blick auf den Stand und die nächsten Schritte.');
  }

  // ---------- 1 Arbeitsalltag ----------
  pres.addSection({ title: 'Arbeitsalltag' });
  {
    const s = inhalt('1 · Arbeitsalltag', 'Die Startseite sagt, was heute ansteht', 'Arbeitsalltag');
    punkte(s, [
      ['Was steht an?', 'Eigene Aufgaben, Gruppenkorb und was Kolleg:innen gerade bearbeiten.'],
      ['Heute automatisch erledigt', 'Was die Routinen über Nacht getan haben – gleichartige Schritte gebündelt.'],
      ['Prüfung nötig', 'Vorschläge wie Lastschriftlauf oder Mahnungen warten auf Freigabe.'],
      ['Kennzahlen', 'Personen, Postausgang, Zustellungen auf einen Blick.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('02_start.png'), 5.0, 1.55, 7.75, 'startseite');
    marker(s, r, 0.277, 0.235, 1); marker(s, r, 0.403, 0.44, 2); marker(s, r, 0.873, 0.503, 3); marker(s, r, 0.537, 0.883, 4);
    s.addNotes('Nach der Anmeldung landet man auf der Startseite. Oben: die eigenen Aufgaben, der Korb der aktiven Rolle und was Kolleginnen gerade übernommen haben. Darunter „Heute automatisch erledigt“: Das System hat z. B. Belege festgeschrieben, Mahnvorschläge erzeugt und einen Lastschriftlauf vorbereitet. Alles, was eine Entscheidung braucht, ist als „Prüfung nötig“ markiert – nichts geht ohne Freigabe raus. Unten Kennzahlen, und über die Schnellsuche oben findet man Personen, Kurse und Orte.');
  }
  {
    const s = inhalt('1 · Arbeitsalltag', 'Die Alles-Suche verzeiht Schreibweisen', 'Arbeitsalltag');
    // Mauerwerk: drei Spalten, alle Ausschnitte im selben Maßstab
    const spalten = [
      [['akzent', 'Akzente, Reihenfolge', 'celik ayse'], ['ort', 'Räume', 'lehrkuche']],
      [['kursnr', 'Kursnummer', '26h-80'], ['frueher', 'Frühere Namen', 'schmidt anna']],
      [['umschrift', 'Umschrift', 'mueller'], ['datum', 'Geburtsdatum, E-Mail', '12.08.1984']],
    ];
    const w = 3.9, g = 0.215;
    // Alle Ausschnitte im selben Maßstab; so klein, dass auch die höchste Spalte über der Fußzeile endet (1,6 … 6,8 Zoll).
    let iw = 3.1;
    for (const eintraege of spalten) {
      let hoehe = 0;
      for (const [f] of eintraege) { const m = await groesse(CR(`such_${f}.png`)); hoehe += m.height / m.width; }
      iw = Math.min(iw, (6.8 - 1.6 - eintraege.length * 1.0 + 0.2) / hoehe);
    }
    for (const [c, eintraege] of spalten.entries()) {
      let y = 1.6;
      for (const [f, t, q] of eintraege) {
        const m = await groesse(CR(`such_${f}.png`));
        const ih = iw * m.height / m.width;
        await bildKarte(s, CR(`such_${f}.png`), 0.6 + c * (w + g), y, w, ih + 0.8, t, q, `suche-${f}`);
        y += ih + 0.8 + 0.2;
      }
    }
    s.addNotes('Oben auf jeder Seite sitzt die Alles-Suche: ein Feld für Personen, Kurse und Orte, Treffer erscheinen schon beim Tippen, gruppiert nach Art. Sie ist tolerant gegenüber Schreibweisen: Groß- und Kleinschreibung, ä, ae oder a, Akzente wie ş und Ç, Bindestriche und die Reihenfolge der Wörter spielen keine Rolle. „mueller“ findet Müller und Muller, „celik ayse“ findet Ayşe Çelik. Weil die Suche in allen Namensversionen sucht, findet „schmidt anna“ auch Anna Becker – ihr früherer Name. Kursnummern lassen sich anfangen („26h-80“ zeigt alle Bewegungskurse 26H-80x), Räume wie „lehrkuche“ werden ohne Umlaut gefunden, und auch Geburtsdatum oder E-Mail-Adresse führen zur Person. Echte Tippfehler wie „beker“ findet die Suche dagegen nicht; ähnliche Namen erkennt die Dublettenprüfung.');
  }
  {
    const s = inhalt('1 · Arbeitsalltag', 'Aufgabenkörbe mit Fristen und Eskalation', 'Arbeitsalltag');
    punkte(s, [
      ['Körbe je Rolle und Fachbereich', 'Meine, Gruppenkorb, Kolleg:innen, alle offenen – wer zuständig ist, sieht es.'],
      ['Fristen und Eskalation', 'Überfällige Aufgaben steigen automatisch zur Leitung auf.'],
      ['Übernehmen, zuweisen, erledigen', 'Mit einem Klick, Vertretung bei Abwesenheit inklusive.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    const r = await browser(s, S('14_aufgaben.png'), 5.0, 1.55, 7.75, 'aufgaben');
    marker(s, r, 0.787, 0.228, 1); marker(s, r, 0.169, 0.332, 2); marker(s, r, 0.169, 0.421, 3);
    s.addNotes('Aufgaben entstehen meist automatisch – etwa wenn ein Integrationskurs gemeldet werden muss oder eine Lehrfreigabe fehlt. Sie landen im Korb der zuständigen Rolle und des Fachbereichs. Jede Aufgabe hat eine Frist; ist sie überschritten, eskaliert das System an die Leitung. Übernehmen, zuweisen und erledigen geht direkt aus der Liste, Abwesenheiten und Vertretungen sind hinterlegt. Jeder Schritt landet im Protokoll.');
  }
  {
    const s = inhalt('1 · Arbeitsalltag', 'Personen: eine Akte für alle Rollen', 'Arbeitsalltag');
    punkte(s, [
      ['Eine Person, viele Rollen', 'Teilnehmende, Dozierende, Mitarbeitende – ohne doppelte Datensätze.'],
      ['Alles im Zusammenhang', 'Stammdaten, Rollen, Qualifikationen, Einwilligungen, Vollmachten, Kurse.'],
      ['Ändern mit Datum und Grund', 'Jede Angabe hat ihre eigene Historie – ein Klick auf die Uhr.'],
      ['Aktionen direkt zur Hand', 'Datenhistorie, Testmail, Bescheinigung drucken – oben rechts.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('04_person.png'), 5.0, 1.55, 7.75, 'personenakte');
    marker(s, r, 0.413, 0.205, 1); marker(s, r, 0.25, 0.704, 2); marker(s, r, 0.927, 0.40, 3); marker(s, r, 0.642, 0.207, 4);
    s.addNotes('Herzstück ist die Personenakte. Eine Person kann gleichzeitig Teilnehmerin, Kursleiterin und Mitarbeiterin sein – es gibt nur einen Datensatz. Hier am Beispiel Anna Becker, die als Dozentin geführt wird. Neben jeder Angabe sitzt ein Uhrsymbol: Dahinter liegt die Historie genau dieser Angabe. Rollen können hinzugefügt und zeitlich beendet werden. Über die Personenliste findet man außerdem die Dublettenprüfung: Mögliche Doppelungen werden vorgeschlagen und nur nach Prüfung zusammengeführt.');
  }
  {
    const s = inhalt('1 · Arbeitsalltag', 'Nichts geht verloren: die Datenhistorie', 'Arbeitsalltag');
    punkte(s, [
      ['Zeitreise per Stichtag', 'Wie war der Stand am …, und was war dazu am … bekannt?'],
      ['Änderung ab Datum', 'Heirat, Umzug, neue Beschäftigung: gilt ab einem Tag, die alte Fassung bleibt.'],
      ['Korrektur mit Grund', 'Tippfehler werden korrigiert, nicht überschrieben – sichtbar in der Zeitleiste.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    const r = await browser(s, S('05_historie.png'), 5.0, 1.55, 7.75, 'historie');
    marker(s, r, 0.754, 0.257, 1); marker(s, r, 0.447, 0.943, 2); marker(s, r, 0.83, 0.90, 3);
    s.addNotes('Die Datenhistorie zeigt, warum das wichtig ist. Anna Becker hieß früher Schmidt. Die Namensänderung ist als „Änderung ab Datum“ erfasst – Briefe von damals werden beim Nachdruck mit dem damals gültigen Namen erzeugt. Mit dem Stichtag kann man fragen: Wie war der Stand an Tag X, und was wussten wir an Tag Y? Echte Fehler werden als Korrektur mit Grund erfasst. Gelöscht oder überschrieben wird nichts – das ist die Grundlage für Nachvollziehbarkeit und Prüfungen.');
  }

  // ---------- 2 Kurse & Teilnahme ----------
  pres.addSection({ title: 'Kurse & Teilnahme' });
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Das Programm: von der Liste zum neuen Kurs', 'Kurse & Teilnahme');
    punkte(s, [
      ['Semesterliste mit Filtern', 'Fachbereich, Status, Ort – Filter bleiben beim Zurückkehren erhalten.'],
      ['Kurs per Schnellweg', 'Titel, Fachbereich, Termine, Ort, Entgelt – Konflikte zeigt die Seite sofort.'],
      ['Kurs-QR-Bogen', 'QR-Codes für Aushang und Programmheft, mit vhs-Logo.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    await browser(s, S('07_kurse.png'), 4.95, 1.5, 6.6, 'kursliste');
    await browser(s, S('09_kursneu.png'), 7.85, 3.35, 4.9, 'kurs-neu');
    s.addNotes('Die Kursliste zeigt das Semester mit Belegung, Status und Ort. Filter stehen in der Adresse, so landet man nach dem Öffnen eines Kurses wieder in genau derselben gefilterten Liste. Neue Kurse legt man über den Schnellweg an: Stammdaten, Terminreihe, Ort und Entgelt. Ferien und Feiertage werden bei der Terminreihe berücksichtigt, Raumkonflikte sofort angezeigt. Für Aushänge gibt es einen QR-Bogen je Semester.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Kurs anlegen: die passende Kursleitung', 'Kurse & Teilnahme');
    punkte(s, [
      ['Anforderung aus Kursart und Titel', '„Italienisch A2“ verlangt die Lehrfreigabe Italienisch.'],
      ['Geeignet heißt wählbar', 'Wer die Freigabe hat, ist direkt auswählbar.'],
      ['Fehlt etwas, steht dran was', '⚠ mit Grund; Ausnahme nur durch die Fachbereichsleitung, mit Begründung.'],
      ['Geprüft an jedem Kurstag', 'Läuft eine Qualifikation ab, fällt es vor dem Termin auf.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, CR('kn_dozent.png'), 5.0, 1.55, 7.75, 'kurs-anlegen-dozent');
    marker(s, r, 0.223, 0.128, 1); marker(s, r, 0.40, 0.31, 2); marker(s, r, 0.808, 0.42, 3); marker(s, r, 0.971, 0.469, 4);
    s.addNotes('Beim Anlegen eines Kurses prüft VibeVHS die Kursleitung. Die Kursart legt fest, welche Qualifikationen nötig sind; bei Sprachkursen ergibt sich die Sprache aus dem Titel – „Italienisch A2 – Konversation“ verlangt die Lehrfreigabe Italienisch, also die Eignungsprüfung der vhs. Bei der Suche nach „sch“ ist Klemens Schacht direkt wählbar, weil er die Freigabe hat. Alle anderen sind mit einem Warnzeichen markiert; der Tooltip nennt, was fehlt. Eine Ausnahme ist möglich, aber nur durch Fachbereichsleitung oder Leitung und nur mit Begründung, die protokolliert wird. Geprüft wird an jedem einzelnen Veranstaltungstag – läuft eine Qualifikation mitten im Kurs ab, fällt das auf.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Terminautomatik mit Ferien- und Feiertagsprüfung', 'Kurse & Teilnahme');
    punkte(s, [
      ['Serie statt Einzeltermine', 'Wochentage, Beginn, Dauer – Umfang als Anzahl, Enddatum oder unbefristet.'],
      ['Ferien und Feiertage', 'Schulferien NRW pausieren (Vorgabe der Kursart), Feiertage immer frei.'],
      ['Passende Räume', 'Nur Räume, die Kursart und Größe erfüllen, sind wählbar.'],
      ['Vorschau vor dem Speichern', 'Kursnummer, alle Termine, ausgelassene Tage mit Grund.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, CR('kn_termine.png'), 5.0, 1.55, 7.75, 'kurs-anlegen-termine');
    marker(s, r, 0.115, 0.065, 1); marker(s, r, 0.575, 0.387, 2); marker(s, r, 0.537, 0.622, 3); marker(s, r, 0.261, 0.822, 4);
    s.addNotes('Termine entstehen aus einer Serie: Wochentage, Beginn, Dauer und Umfang – eine feste Anzahl, ein Enddatum oder unbefristet. Unbefristete Kurse wie „Pilates fortlaufend“ werden rollierend fortgeschrieben. Schulferien NRW werden auf Wunsch ausgelassen – die Kursart gibt das vor –, gesetzliche Feiertage immer. Zur Auswahl stehen nur Räume, die zur Nutzungsart passen und groß genug sind. Die Vorschau zeigt vor dem Speichern die vergebene Kursnummer und alle Termine: hier zwölf Montage ab 1. Februar 2027; der 22. März fällt in die Osterferien und der 29. März ist Ostermontag – beide werden automatisch übersprungen, die Serie läuft entsprechend länger. Konflikte stehen ebenfalls in der Vorschau.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Buchungssystematik: mit der Kursart gewählt', 'Kurse & Teilnahme');
    const w = 2.88, g = 0.2, y = 1.6, h = 1.75;
    karteKompakt(s, 0.6, y, w, h, 'oeffentlich', 'Öffentlich', 'Tresen und online, Warteliste, Storno bis 7 Tage vorher.');
    karteKompakt(s, 0.6 + (w + g), y, w, h, 'karte', '10er-Karte', '10 Termine in 6 Monaten; Termine einzeln einbuchen.');
    karteKompakt(s, 0.6 + 2 * (w + g), y, w, h, 'firma', 'Firmenkurs', 'Auftraggeber bucht, nicht öffentlich, ohne Warteliste.');
    karteKompakt(s, 0.6 + 3 * (w + g), y, w, h, 'integration', 'Integrationskurs', 'Nur das IK-Team bucht; Check-in mit Bestätigung.');
    s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: 0.6, y: 3.6, w: 3.1, h: 3.1, rectRadius: 0.12, fill: { color: C.background1 }, line: { type: 'none' }, shadow: { type: 'outer', color: '0A3D4A', blur: 10, offset: 2, angle: 90, opacity: 0.14 }, objectName: 'kursart-karte' });
    s.addImage({ path: CR('kn_kursart_kurz.png'), x: 0.6 + (3.1 - 2.86 * 437 / 430) / 2, y: 3.72, w: 2.86 * 437 / 430, h: 2.86, altText: 'Auswahl der Kursart beim Anlegen', objectName: 'kursart-auswahl' });
    await browser(s, CR('kn_kontingent.png'), 3.95, 3.6, 8.78, 'kontingent-stand');
    s.addNotes('Wie Kundinnen und Kunden buchen, wählt man beim Anlegen über die Kursart. Sie bringt eine Vorlage mit: Öffentliche Kurse buchen Teilnehmende am Tresen oder online, mit Warteliste und Stornofrist von sieben Tagen. Fortlaufende Kurse laufen als 10er-Karte: zehn Termine innerhalb von sechs Monaten, in die man sich einzeln einbucht – bis 24 Stunden vorher, Nichterscheinen verbraucht die Einheit. Rechts unten sieht man den Stand je Person: besucht, verfallen, eingebucht, übrig. Firmenkurse bucht der Auftraggeber, sie sind nicht öffentlich sichtbar. Integrationskurse bucht nur das Integrationskurs-Team, der Selbst-Check-in braucht die Bestätigung der Kursleitung. Die Kursart steuert außerdem Nutzungsart, Raumwahl und die Ferienregel.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Kursdetail: alles zu einer Veranstaltung', 'Kurse & Teilnahme');
    punkte(s, [
      ['Kursdaten auf einen Blick', 'Team, Termine, Entgelt mit Ermäßigung, Buchungswege, Check-in.'],
      ['Termine gezielt ändern', 'Nur diesen, alle folgenden oder einen zusätzlichen Termin – mit Benachrichtigung.'],
      ['Veröffentlichen und QR', 'Kurs in den Webshop stellen, QR-Code für die Schnellanmeldung.'],
      ['Absagen mit Begründung', 'Bestätigung, Grund und Nachricht an alle Teilnehmenden.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('08b_kurs_yoga.png'), 5.0, 1.55, 7.75, 'kursdetail');
    marker(s, r, 0.277, 0.299, 1); marker(s, r, 0.732, 0.287, 2); marker(s, r, 0.754, 0.16, 3); marker(s, r, 0.944, 0.157, 4);
    s.addNotes('Im Kursdetail – hier „Yoga am Abend“ – sieht man Team, Terminreihe, Entgelt mit Ermäßigung und die erlaubten Buchungswege. Termine lassen sich einzeln, ab einem Datum oder als Zusatztermin ändern; Betroffene werden automatisch informiert. Mit „Veröffentlichen“ erscheint der Kurs im Webshop. Eine Absage wird nur mit Bestätigung und Grund ausgeführt und benachrichtigt alle Teilnehmenden.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Raumplan und Orte', 'Kurse & Teilnahme');
    punkte(s, [
      ['Wochenansicht je Raum', 'Belegung über alle Standorte – Emsdetten, Greven, Saerbeck und online.'],
      ['Konflikte sofort sichtbar', 'Doppelbelegungen werden rot markiert, mit Sprung zum Kurs.'],
      ['Räume mit Merkmalen', 'Barrierefrei, Plätze, Ausstattung – Kurse fordern „mind. n“ an.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    await browser(s, S('10_raumplan.png'), 5.0, 1.55, 7.75, 'raumplan');
    s.addNotes('Der Raumplan zeigt die Woche je Raum über alle Standorte. Überschneidungen erscheinen rot mit direktem Link zum betroffenen Kurs. Unter „Orte“ sind Gebäude und Räume mit Merkmalen gepflegt – barrierefrei, Anzahl Plätze, Ausstattung. Kurse können Raumbedingungen wie „mindestens 12 Plätze“ stellen, die Raumwahl prüft das.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Anmeldung am Tresen in drei Schritten', 'Kurse & Teilnahme');
    punkte(s, [
      ['Person – Kurs – Zahlung', 'Der Ablauf führt durch drei Schritte, nichts wird vergessen.'],
      ['Buchungsprüfung sofort', 'Was möglich ist und warum nicht: voll, Warteliste, Überschneidung.'],
      ['Freie Plätze im Blick', 'Belegung und Status je Kurs, Lastschrift oder Rechnung direkt wählbar.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    const r = await browser(s, S('11_anmeldung.png'), 5.0, 1.55, 7.75, 'tresen');
    marker(s, r, 0.429, 0.228, 1); marker(s, r, 0.56, 0.375, 2); marker(s, r, 0.852, 0.447, 3);
    s.addNotes('Kommt jemand an den Tresen, führt die Anmeldung durch drei Schritte: Person, Kurs, Zahlung. Schon bei der Kursauswahl prüft das System, ob die Buchung möglich ist – und sagt, warum nicht: ausgebucht, nur Warteliste, Überschneidung, fehlende Voraussetzung. Ist ein Kurs voll, landet die Person auf der Warteliste und rückt automatisch nach, wenn ein Platz frei wird. Bezahlt wird per Lastschrift mit Mandat oder per Rechnung.');
  }
  {
    const s = inhalt('2 · Kurse & Teilnahme', 'Anwesenheit und Check-in per QR-Code', 'Kurse & Teilnahme');
    punkte(s, [
      ['Tagesliste', 'Alle Veranstaltungen des Tages mit Ort und Kursleitung.'],
      ['Check-in per QR', 'Raum-QR am Beamer oder Aushang; Teilnehmende checken selbst ein.'],
      ['Ausweise scannen', 'Teilnehmendenausweise per Kamera – ideal für Integrationskurse.'],
      ['Liste abschließen', 'Danach zählt die Anwesenheit für Bescheinigung und Abrechnung.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('13_anwesenheit_termin.png'), 5.0, 1.55, 7.75, 'anwesenheit');
    marker(s, r, 0.288, 0.15, 1); marker(s, r, 0.797, 0.322, 2); marker(s, r, 0.77, 0.155, 3); marker(s, r, 0.93, 0.155, 4);
    s.addNotes('Für jede Veranstaltung gibt es eine Anwesenheitsliste. Teilnehmende checken selbst ein: Die Kursleitung zeigt einen Raum-QR-Code am Beamer, wer ihn scannt, ist eingetragen. Der Code gilt nur für diesen Termin und Ort, von 30 Minuten vor bis 30 Minuten nach Beginn. Alternativ scannt die Kursleitung die Ausweise per Kamera. Nach dem Abschließen fließt die Anwesenheit in Bescheinigungen und – bei Integrationskursen – in die BAMF-Abrechnung ein.');
  }

  // ---------- 3 Geld & Förderung ----------
  pres.addSection({ title: 'Geld & Förderung' });
  {
    const s = inhalt('3 · Geld & Förderung', 'Finanzen nach GoBD – vorbereitet, nicht nebenher', 'Geld & Förderung');
    punkte(s, [
      ['Alles an einem Ort', 'Belege, Lastschrift, Kontoauszüge, Mahnwesen, Honorare, Export.'],
      ['Kennzahlen', 'Offene Forderungen, Überfälliges, offene Honorare, festgeschriebene Belege.'],
      ['Was ist zu tun?', 'Vorschläge der Routinen prüfen und freigeben – mit einem Klick zum Vorgang.'],
      ['Routinen auf Knopfdruck', 'Täglich 6:30 Uhr automatisch, bei Bedarf sofort.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('15_finanzen.png'), 5.0, 1.55, 7.75, 'finanzen');
    marker(s, r, 0.792, 0.228, 1); marker(s, r, 0.326, 0.331, 2); marker(s, r, 0.299, 0.433, 3); marker(s, r, 0.846, 0.765, 4);
    s.addNotes('Die Finanzen sind GoBD-konform: Belege werden lückenlos nummeriert und festgeschrieben, korrigiert wird nur per Storno. Rechnungen, Gutschriften und Firmenrechnungen inklusive XRechnung entstehen automatisch aus den Buchungen. Der SEPA-Lastschriftlauf wird vorgeschlagen und nach Freigabe als Datei erzeugt; Kontoauszüge werden eingelesen und Zahlungen zugeordnet, sichere Treffer automatisch. Mahnvorschläge und Honorarabrechnungen für Kursleitungen laufen ebenso. Für die Kämmerei gibt es den Export an die Finanzsoftware.');
  }
  {
    const s = inhalt('3 · Geld & Förderung', 'Integrationskurse mit BAMF-Anbindung', 'Geld & Förderung');
    punkte(s, [
      ['Status je Kurs', 'Einrichten, melden, gemeldet – was fehlt, steht dran.'],
      ['Kursbeginn melden', 'Meldung an das BAMF direkt aus VibeVHS (heute simuliert).'],
      ['Gesperrt heißt erklärt', 'Ausweis nicht geprüft? Der Kursstart nennt die Person und den Grund.'],
      ['UE je Abschnitt', 'Geplante, belegte und gehaltene Unterrichtseinheiten je Modul.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('31_integration.png'), 5.0, 1.55, 7.75, 'integration');
    marker(s, r, 0.494, 0.383, 1); marker(s, r, 0.862, 0.27, 2); marker(s, r, 0.635, 0.423, 3); marker(s, r, 0.732, 0.511, 4);
    s.addNotes('Integrationskurse haben eigene Regeln. Das Team sieht je Kurs den Stand gegenüber dem BAMF. Der Kursbeginn wird erst freigegeben, wenn alle Ausweise gegen die Berechtigung geprüft sind – ohne Kopie, das Lichtbild dient nur dem Sichtvergleich. Hier sieht man den Grund direkt: zwei Ausweise sind noch nicht geprüft. Die Unterrichtseinheiten werden je Abschnitt gezählt und bilden die Grundlage für Kostenerstattung, Kostenbeitrag und Fahrtkosten. Die BAMF-Schnittstelle ist derzeit noch ein Mock.');
  }
  {
    const s = inhalt('3 · Geld & Förderung', 'Post und Nachrichten – mehrsprachig', 'Geld & Förderung');
    punkte(s, [
      ['Postausgang', 'Jede Mail und jeder Brief sichtbar, mit Zustellstatus und Anhang.'],
      ['Vorlagen mit Platzhaltern', 'Bestätigungen, Absagen, Mahnungen – zentral gepflegt.'],
      ['Übersetzung eingebaut', 'Jede Mail mit Übersetzungslink, jeder Brief mit QR „Translate this letter“.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    await browser(s, S('16_postausgang.png'), 4.95, 1.5, 6.6, 'postausgang');
    await browser(s, S('17_vorlagen.png'), 7.85, 3.35, 4.9, 'vorlagen');
    s.addNotes('Alles, was VibeVHS verschickt, läuft über den Postausgang – nachvollziehbar mit Zeitpunkt, Kanal und Status; fehlgeschlagene Zustellungen erscheinen auf der Startseite. Die Texte kommen aus Vorlagen mit Platzhaltern. Für viele Teilnehmende ist Deutsch nicht die Erstsprache: Jede Mail enthält einen Übersetzungslink, jeder Brief einen QR-Code, der das Schreiben in der eigenen Sprache anzeigt. Briefe werden mit Prüfsumme im Dokumentarchiv abgelegt.');
  }

  // ---------- 4 Portale & Webshop ----------
  pres.addSection({ title: 'Portale & Webshop' });
  {
    const s = inhalt('4 · Portale & Webshop', '„Meine vhs“ – das Portal für Teilnehmende', 'Portale & Webshop');
    punkte(s, [
      ['Nächste Termine', 'Mit Raum, Wegbeschreibung, Kalendereintrag und Check-in.'],
      ['Buchungen verwalten', 'Abmelden und umbuchen nach den Stornoregeln.'],
      ['Für andere buchen', 'Mit Vollmacht, z. B. Eltern für ihre Kinder.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    phone(s, CR('40_tn_start.png'), 5.0, 1.55, 2.45, 'tn-termine');
    phone(s, CR('41_tn_buchungen.png'), 7.65, 1.55, 2.45, 'tn-buchungen');
    phone(s, CR('42_tn_buchen.png'), 10.3, 1.55, 2.45, 'tn-buchen');
    s.addNotes('Teilnehmende melden sich ohne Passwort an: Sie bekommen einen Anmeldelink per Mail, später über das Servicekonto der Kommune. In „Meine vhs“ sehen sie ihre nächsten Termine mit Raum und Weg – Änderungen sind farbig markiert –, können in den eigenen Kalender übernehmen und einchecken. Buchungen lassen sich nach den Stornoregeln abmelden oder umbuchen. Mit Vollmacht bucht man auch für andere, etwa für das eigene Kind. Unterlagen und Rechnungen liegen ebenfalls hier.');
  }
  {
    const s = inhalt('4 · Portale & Webshop', 'Das Dozierendenportal – für unterwegs gemacht', 'Portale & Webshop');
    punkte(s, [
      ['Meine Termine', 'Raum, Teilnehmendenzahl, Wegbeschreibung, Kalender-Abo.'],
      ['Anwesenheit am Smartphone', 'Ausweise scannen, Liste abschließen, Nachricht an die Gruppe.'],
      ['Honorar nachvollziehbar', 'Jede Position mit Rechenweg; nach Freigabe die Gutschrift als PDF.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    phone(s, CR('43_doz_start.png'), 5.0, 1.55, 2.45, 'doz-termine');
    phone(s, CR('46_doz_termin.png'), 7.65, 1.55, 2.45, 'doz-anwesenheit');
    phone(s, CR('45_doz_honorar.png'), 10.3, 1.55, 2.45, 'doz-honorar');
    s.addNotes('Kursleitungen sind meist freiberuflich und unterwegs. Das Dozierendenportal zeigt die nächsten Termine mit Raum und Teilnehmendenzahl und lässt sich als Kalender abonnieren. Direkt im Kurs erfassen sie die Anwesenheit, scannen Ausweise und schreiben der Gruppe eine Nachricht. Gezeigt wird Till Rother, eine freiberufliche Kursleitung. Rechts seine Honoraransicht: der aktuelle Entwurf, den die Buchhaltung noch prüft; nach der Freigabe gibt es die Gutschrift als PDF. Jede Position zeigt den Rechenweg aus dem Honorarvertrag – Basis je Termin plus Unterrichtseinheiten mal Satz plus anwesende Teilnehmende mal Satz, dazu Fahrtkosten je Kilometer. Stimmt etwas nicht, meldet die Kursleitung direkt hier einen Einwand an die Buchhaltung. Festangestellte Kursleitungen erhalten kein Honorar und sehen hier entsprechend nichts. Anliegen der Kursleitungen landen in der Verwaltung unter „Anliegen Kursleitungen“.');
  }
  {
    const s = inhalt('4 · Portale & Webshop', 'Webshop und Schnellanmeldung per QR', 'Portale & Webshop');
    punkte(s, [
      ['Web-Komponente', 'Ein Skript-Tag bindet das Kursangebot in jede Website ein, auch TYPO3.'],
      ['Live-Belegung', 'Freie Plätze, Ermäßigung, Barrierefreiheit direkt im Angebot.'],
      ['QR im Programmheft', 'Scannen, Kurs sehen, in den Warenkorb – fertig.'],
    ], 0.6, 1.65, 3.9, { abstand: 1.35 });
    await browser(s, S('50_shop.png'), 4.75, 1.55, 6.3, 'webshop');
    phone(s, CR('47_kursqr.png'), 10.35, 2.05, 2.4, 'kurs-qr');
    s.addNotes('Online buchen Teilnehmende über den Webshop. Er ist eine Web-Komponente: Ein einziges Skript-Tag bindet ihn in die bestehende vhs-Website ein, auch in TYPO3. Das Angebot zeigt freie Plätze, Ermäßigungen und Barrierefreiheit in Echtzeit, Plätze werden während des Bezahlens reserviert. Wer im Programmheft oder auf einem Plakat den QR-Code scannt, landet direkt auf der Kursseite und kann sofort buchen.');
  }

  // ---------- 5 Steuerung & Betrieb ----------
  pres.addSection({ title: 'Steuerung & Betrieb' });
  {
    const s = inhalt('5 · Steuerung & Betrieb', 'Dashboard und Auswertungen für die Leitung', 'Steuerung & Betrieb');
    punkte(s, [
      ['Kennzahlen je Semester', 'Auslastung, Umsatz, Ausfallquote, offene Aufgaben.'],
      ['Auslastung je Fachbereich', 'Wo läuft es, wo braucht es Werbung?'],
      ['Umsatz im Verlauf', 'Monatsweise, Stornos und Gutschriften abgezogen.'],
      ['Frühwarnung', 'Kurse unter Mindestteilnehmerzahl rechtzeitig erkennen.'],
    ], 0.6, 1.65, 4.1);
    const r = await browser(s, S('30_dashboard_leitung.png'), 5.0, 1.55, 7.75, 'dashboard');
    marker(s, r, 0.808, 0.151, 1); marker(s, r, 0.353, 0.381, 2); marker(s, r, 0.608, 0.381, 3); marker(s, r, 0.895, 0.381, 4);
    s.addNotes('Für die Leitung bündelt das Dashboard die wichtigsten Zahlen des Semesters: Auslastung, Umsatz, Ausfallquote und offene Aufgaben. Die Auslastung je Fachbereich zeigt, wo nachgesteuert werden muss. Kurse unter der Mindestteilnehmerzahl erscheinen frühzeitig – rechtzeitig für Werbung oder eine geordnete Absage. Unter „Auswertungen“ gibt es Listen und Statistiken, z. B. die VHS-Statistik des DIE, jeweils mit Export.');
  }
  {
    const s = inhalt('5 · Steuerung & Betrieb', 'Datenschutz und Protokoll – eingebaut', 'Steuerung & Betrieb');
    punkte(s, [
      ['Sperren, Anonymisieren, Löschen', 'Löschläufe nur mit zwei Freigaben durch verschiedene Personen.'],
      ['Lückenloses Protokoll', 'Wer hat wann was getan – Menschen wie Routinen.'],
      ['Personen geschützt', 'Pseudonyme in Logs; Exporte und Zugriffe auf Gesperrte werden protokolliert.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    await browser(s, S('19_datenschutz.png'), 4.95, 1.5, 6.6, 'datenschutz');
    await browser(s, S('18_protokoll.png'), 7.85, 3.35, 4.9, 'protokoll');
    s.addNotes('Datenschutz ist kein Zusatzmodul. Personen können gesperrt, archiviert und anonymisiert werden; Löschläufe brauchen zwei Freigaben durch verschiedene Personen, und es wird nie etwas gelöscht, worauf andere Daten verweisen. Das Protokoll hält jeden Schritt fest – von Menschen und von automatischen Routinen. In technischen Logs stehen keine Personendaten, sondern Pseudonyme. Exporte und Zugriffe auf gesperrte Personen werden eigens protokolliert.');
  }
  {
    const s = inhalt('5 · Steuerung & Betrieb', 'Hilfe, Schulung und Betrieb', 'Steuerung & Betrieb');
    punkte(s, [
      ['Geführte Touren', 'Schritt für Schritt durch echte Abläufe – für Abnahme und Schulung.'],
      ['Dokumentation mit Kommentaren', 'Rückmeldungen direkt in der App sammeln.'],
      ['Betrieb im Blick', 'Sicherungen, Sicherheitsprüfungen, Routinen und Importe.'],
    ], 0.6, 1.65, 4.1, { abstand: 1.35 });
    await browser(s, S('23_hilfe.png'), 4.95, 1.5, 6.6, 'hilfe');
    await browser(s, S('32_betrieb.png'), 7.85, 3.35, 4.9, 'betrieb');
    s.addNotes('Zum Einarbeiten gibt es geführte Touren: Sie führen Schritt für Schritt durch echte Abläufe – Person anlegen, Kurs anlegen, Tresenanmeldung, Lastschriftlauf und mehr. Dieselben Touren dienen als Abnahme-Drehbuch. In der Dokumentation können alle Kommentare hinterlassen, die direkt in die Weiterentwicklung fließen. Die Systemverwaltung sieht unter „Betrieb“ Sicherungen, Sicherheitseinstellungen und den Stand der Routinen; Datenübernahmen laufen über den Import.');
  }
  {
    const s = inhalt('Leitlinien', 'Was man in jeder Ecke wiederfindet', 'Steuerung & Betrieb');
    const items = [
      ['gesperrt', 'Gesperrt heißt erklärt', 'Ein grauer Knopf sagt immer, warum – und was zuerst zu tun ist.'],
      ['weiter', 'Nächste Schritte', 'Nach jeder Aktion schlägt die Seite den sinnvollen Folgeschritt vor.'],
      ['zurueck', 'Zurück zur Liste', 'Filter bleiben erhalten, die zuletzt geöffnete Zeile ist markiert.'],
      ['sprache', 'Mehrsprachig', 'Übersetzungslink in Mails, QR-Code auf jedem Brief.'],
    ];
    const w = 5.95, h = 2.25;
    items.forEach(([ic, t, d], i) => karte(s, 0.6 + (i % 2) * (w + 0.25), 1.7 + Math.floor(i / 2) * (h + 0.3), w, h, ic, t, d));
    s.addNotes('Vier Muster ziehen sich durch die ganze Oberfläche und machen sie verlässlich: Gesperrte Aktionen erklären sich selbst. Nach jeder Aktion werden die nächsten Schritte angeboten. Aus jeder Detailseite geht es zurück in genau die gefilterte Liste, aus der man kam. Und alles, was an Kundinnen und Kunden geht, ist übersetzbar.');
  }

  // ---------- Abschluss ----------
  pres.addSection({ title: 'Ausblick' });
  {
    const s = inhalt('Stand und Ausblick', 'Wo wir stehen – und was als Nächstes kommt', 'Ausblick');
    spalte(s, 0.6, 'fertig', 'Fertig (I0–I10)', ['Personen, Programm, Anmeldung, Durchführung', 'Aufgaben, Finanzen/GoBD, Integrationskurse', 'Webshop, beide Portale', 'Hilfe, Touren, Dokumentation'], false);
    spalte(s, 4.74, 'deploy', 'Als Nächstes', ['Testsystem im Hochschulnetz in Betrieb nehmen', 'Abnahme mit der vhs EGS anhand der Touren', 'Vor dem Produktivbetrieb: Datenbank-Migrationen'], true);
    spalte(s, 8.88, 'klaeren', 'Offen zu klären', ['Barzahlung am Tresen', 'BAMF-Erstattungs- und Kostenbeitragssätze', 'Sachkonten für den Export an die Kämmerei', 'Echter Virenscanner und Übersetzer (heute simuliert)'], false);
    s.addNotes('Zum Stand: Die geplanten Iterationen sind umgesetzt, von den Personen bis zu den Portalen, dazu Hilfe und Touren. Als Nächstes geht das Testsystem auf dem Hochschulserver in Betrieb; dann folgt die Abnahme mit der vhs entlang der Touren. Vor einem Produktivbetrieb werden Datenbank-Migrationen eingeführt. Offen sind einige fachliche Klärungen: Barzahlung, die aktuellen BAMF-Sätze, die Sachkonten für die Kämmerei sowie echte Anbindungen für Virenprüfung und Übersetzung, die heute noch simuliert sind.');
  }
  {
    const s = pres.addSlide({ masterName: 'TITEL_DUNKEL', sectionTitle: 'Ausblick' });
    s.addShape(pres.shapes.OVAL, { x: -2.5, y: 3.2, w: 7, h: 7, fill: { color: '0E7C86', transparency: 70 }, line: { type: 'none' }, objectName: 'deko-kreis' });
    s.addImage({ path: LOGO, x: 5.92, y: 1.15, w: 1.5, h: 1.5, altText: 'VibeVHS-Logo', objectName: 'logo' });
    s.addText('Vielen Dank!', { x: 1.5, y: 2.95, w: 10.33, h: 1.0, fontSize: 48, bold: true, color: C.background1, align: 'center', margin: 0, isTextBox: true, objectName: 'dank' });
    s.addText('Fragen, Ideen, Rückmeldungen – gern jetzt oder direkt in der App unter Dokumentation.', { x: 1.5, y: 4.05, w: 10.33, h: 0.6, fontSize: 18, color: 'BFE3E0', align: 'center', margin: 0, isTextBox: true, objectName: 'fragen' });
    s.addText([
      { text: 'Vibe', options: { color: C.background1, bold: true } },
      { text: 'VHS', options: { color: C.accent2, bold: true } },
      { text: '  ·  Volkshochschule, einfach verwaltet', options: { color: '9CC9C6' } },
    ], { x: 1.5, y: 5.6, w: 10.33, h: 0.5, fontSize: 16, align: 'center', margin: 0, isTextBox: true, objectName: 'fuss' });
    s.addNotes('Danke für die Aufmerksamkeit. Gern beantworte ich Fragen oder zeige einzelne Abläufe live im Testsystem. Rückmeldungen können auch direkt in der App über die Dokumentation als Kommentar hinterlassen werden.');
  }

  await pres.writeFile({ fileName: OUT });
  await themaAnwenden(OUT, THEME);
  console.log('geschrieben', OUT);
})();
