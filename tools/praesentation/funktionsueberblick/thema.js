// Schreibt Name und Farbschema des Themas in eine von pptxgenjs erzeugte .pptx (pptxgenjs selbst kann das nicht).
// Ohne das lösen die Schemafarben (pres.SchemeColor) in PowerPoint zur Office-Standardpalette auf.
const fs = require('fs');

// jszip kommt mit pptxgenjs mit
const JSZip = require(require.resolve('jszip', { paths: [require.resolve('pptxgenjs')] }));

const FARBEN = ['dk1', 'lt1', 'dk2', 'lt2', 'accent1', 'accent2', 'accent3', 'accent4', 'accent5', 'accent6', 'hlink', 'folHlink'];
const xmlText = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

async function themaAnwenden(datei, thema) {
  const zip = await JSZip.loadAsync(fs.readFileSync(datei));
  const teil = 'ppt/theme/theme1.xml';
  const xml = await zip.file(teil).async('string');
  for (const k of FARBEN) if (!/^[0-9A-Fa-f]{6}$/.test(thema.colors[k] ?? '')) throw new Error(`Farbe ${k} fehlt oder ist kein Hexwert`);
  const name = xmlText(thema.name);
  const schema = `<a:clrScheme name="${name}">` + FARBEN.map((k) => `<a:${k}><a:srgbClr val="${thema.colors[k].toUpperCase()}"/></a:${k}>`).join('') + '</a:clrScheme>';
  const neu = xml.replace(/<a:theme\b([^>]*?)\bname="[^"]*"/, `<a:theme$1name="${name}"`).replace(/<a:clrScheme\b[\s\S]*?<\/a:clrScheme>/, schema);
  if (!neu.includes(schema)) throw new Error('Kein <a:clrScheme> im Thema gefunden – nichts geändert.');
  zip.file(teil, neu);
  fs.writeFileSync(datei, await zip.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE' }));
}

module.exports = { themaAnwenden };
