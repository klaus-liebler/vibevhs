// Symbole (Material Icons aus react-icons) als PNG in drei Farben: weiß, Petrol (_p), Koralle (_c) → symbole/
const sharp = require('sharp');
const fs = require('fs');
const path = require('path');
const React = require('react');
const RDS = require('react-dom/server');
const md = require('react-icons/md');

// Name der Datei → Material-Icon
const SYMBOLE = {
  touch: 'MdTouchApp', auto: 'MdAutorenew', schutz: 'MdVerifiedUser', hub: 'MdHub', verwaltung: 'MdBadge', dozent: 'MdSchool',
  tn: 'MdPerson', gesperrt: 'MdLock', weiter: 'MdArrowForward', historie: 'MdHistory', sprache: 'MdTranslate', zurueck: 'MdKeyboardReturn',
  hilfe: 'MdHelpOutline', fertig: 'MdCheckCircle', deploy: 'MdCloudUpload', klaeren: 'MdQuestionAnswer', db: 'MdStorage', euro: 'MdEuro',
  kurs: 'MdEvent', gruppe: 'MdGroups', insights: 'MdInsights', shop: 'MdShoppingCart', netz: 'MdPublic', gebaeude: 'MdDomain',
  flagge: 'MdFlag', suche: 'MdSearch', karte: 'MdConfirmationNumber', firma: 'MdBusiness', integration: 'MdAssistantPhoto',
  oeffentlich: 'MdPublic',
};

(async () => {
  const ziel = path.join(__dirname, 'symbole');
  fs.mkdirSync(ziel, { recursive: true });
  for (const [n, k] of Object.entries(SYMBOLE)) {
    if (!md[k]) throw new Error('Unbekanntes Symbol ' + k);
    for (const [suf, farbe] of [['', '#FFFFFF'], ['_p', '#0E7C86'], ['_c', '#FF7A59']]) {
      const svg = RDS.renderToStaticMarkup(React.createElement(md[k], { color: farbe, size: 256 }));
      await sharp(Buffer.from(svg)).resize(256, 256).png().toFile(path.join(ziel, `${n}${suf}.png`));
    }
  }
  console.log(Object.keys(SYMBOLE).length, 'Symbole in', ziel);
})();
