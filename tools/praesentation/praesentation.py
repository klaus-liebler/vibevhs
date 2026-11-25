#!/usr/bin/env python3
"""Baut eine VibeVHS-Präsentation als PowerPoint-Datei (.pptx) aus docs/praesentation/<name>/folien.json (nach dem Vorbild von SchoolCraft).

Aufruf aus dem Repo-Wurzelverzeichnis:  py -3 tools/praesentation/praesentation.py woche
Ergebnis: docs/praesentation/<name>/<name>.pptx (16:9, bearbeitbar, Sprechernotizen aus „notiz“).
Farben aus der Oberfläche (wwwroot/app.css: --vhs-accent …), Schrift Calibri. Braucht python-pptx und Pillow.

Folienarten (nach Feldern): h1 (Titel), kapitel (Tagestrenner), bild (Szene mit Bildschirmfoto), karten, aussage (Schluss).
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageFont
from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.util import Emu, Pt

WURZEL = Path(__file__).resolve().parent.parent.parent
PX = 914400 / 144  # 1920 × 1080 px auf 13,333 × 7,5 Zoll
SCHRIFT = 'Calibri'


def farbe(h): return RGBColor.from_string(h)


# Farben der VibeVHS-Oberfläche
PETROL, PETROL_TIEF, HAUCH, SONNE = '0D6870', '0A3D4A', 'D6ECEE', 'FF8A66'  # Sonne = Orange der Bildmarke
TINTE, NEBEL, GRAU, LINIE = '172629', 'F3F5F5', '57696D', 'D5DFDF'
HINTERGRUND = {'hell': NEBEL, 'hauch': HAUCH, 'dunkel': PETROL, 'tief': PETROL_TIEF}


class Stil:
    def __init__(self, hg):
        self.dunkel = hg in ('dunkel', 'tief')
        self.grund = HINTERGRUND.get(hg, NEBEL)
        self.titel = NEBEL if self.dunkel else PETROL
        self.text = 'D3E7E9' if self.dunkel else GRAU
        self.stark = NEBEL if self.dunkel else TINTE
        self.marke = SONNE if self.dunkel else PETROL
        self.fuss = 'A9CBCE' if self.dunkel else '6B7D81'
        self.karte = '17545B' if self.dunkel else ('FFFFFF' if hg != 'hauch' else NEBEL)
        self.feld = '17545B' if self.dunkel else ('FFFFFF' if hg == 'hauch' else HAUCH)


def e(px): return Emu(int(px * PX))


def rechteck(folie, x, y, b, h, fuell, rund=True, linie=None, name=None):
    form = folie.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE if rund else MSO_SHAPE.RECTANGLE, e(x), e(y), e(b), e(h))
    form.fill.solid()
    form.fill.fore_color.rgb = farbe(fuell)
    if linie:
        form.line.color.rgb = farbe(linie)
        form.line.width = Pt(0.75)
    else:
        form.line.fill.background()
    if rund:
        form.adjustments[0] = min(0.5, 20 / max(1, min(b, h)))
    form.shadow.inherit = False
    if name:
        form.name = name
    return form


def text(folie, x, y, b, h, inhalt, groesse, farbe_, fett=False, abstand=1.15, name=None, mitte=False, sperrung=None):
    """inhalt: str oder Liste von Läufen {text, fett, gelb}."""
    feld = folie.shapes.add_textbox(e(x), e(y), e(b), e(h))
    if name:
        feld.name = name
    tf = feld.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = MSO_ANCHOR.TOP
    laeufe = [{'text': inhalt}] if isinstance(inhalt, str) else inhalt
    p = tf.paragraphs[0]
    p.line_spacing = abstand
    if mitte:
        p.alignment = PP_ALIGN.CENTER
    for lauf in laeufe:
        r = p.add_run()
        r.text = lauf['text']
        r.font.name = SCHRIFT
        r.font.size = Pt(groesse)
        r.font.bold = fett or lauf.get('fett', False)
        r.font.color.rgb = farbe(SONNE if lauf.get('gelb') else (lauf.get('farbe') or farbe_))
        if sperrung:
            r.font._element.set('spc', str(sperrung))
    return feld


_MASS = {}
_SCHRIFTDATEIEN = ['C:/Windows/Fonts/calibrib.ttf', '/usr/share/fonts/truetype/crosextra/Carlito-Bold.ttf', '/Library/Fonts/Calibri Bold.ttf']


def _mass(pt):
    if pt not in _MASS:
        _MASS[pt] = None
        for datei in _SCHRIFTDATEIEN:
            if Path(datei).exists():
                _MASS[pt] = ImageFont.truetype(datei, int(pt * 2))
                break
    return _MASS[pt]


def zeilen(t, breite_px, pt):
    schrift = _mass(pt)
    breite = (lambda w: schrift.getlength(w)) if schrift else (lambda w: len(w) * pt)
    n, zeile = 1, ''
    for wort in t.split():
        probe = (zeile + ' ' + wort).strip()
        if zeile and breite(probe) > breite_px:
            n, zeile = n + 1, wort
        else:
            zeile = probe
    return n


def hoehe(t, breite_px, pt, abstand=1.15): return zeilen(t, breite_px, pt) * pt * 2 * abstand * 1.08


def kopf(folie, f, s, x=128, b=1664, h2pt=36):
    y = 128
    if f.get('eyebrow'):
        hh = hoehe(f['eyebrow'].upper(), b * 0.6, 12)
        text(folie, x, y, b, hh, f['eyebrow'].upper(), 12, s.marke, fett=True, sperrung=300, name='Dachzeile')
        y += hh + 24
    if f.get('h2'):
        hh = hoehe(f['h2'], b, h2pt, 1.0)
        text(folie, x, y, b, hh, f['h2'], h2pt, s.titel, fett=True, abstand=1.0, name='Titel')
        y += hh + 8
    return y


def fuss(folie, f, s, nr):
    if f.get('fuss'):
        text(folie, 128, 1000, 1300, 30, f['fuss'], 11, s.fuss, name='Fußzeile')
    text(folie, 1392, 1000, 400, 30, f.get('fuss_rechts') or str(nr), 11, s.fuss, name='Seitenzahl').text_frame.paragraphs[0].alignment = PP_ALIGN.RIGHT


def bild(folie, pfad, x, y, b, max_h, rahmen):
    with Image.open(pfad) as im:
        w, h = im.size
    hb = b * h / w
    if hb > max_h:
        hb, b = max_h, max_h * w / h
    if rahmen:
        rechteck(folie, x - 1, y - 1, b + 2, hb + 2, LINIE, rund=False, name='Bildrahmen')
    folie.shapes.add_picture(str(pfad), e(x), e(y), e(b), e(hb))


def pillenzeile(folie, namen, x, y, s, breite):
    """Bereiche der Anwendung als Pillen, umbrechend."""
    xx = x
    for name in namen:
        mass = _mass(12)
        b = (mass.getlength(name) if mass else len(name) * 13) + 36
        if xx + b > x + breite:
            xx, y = x, y + 48
        form = rechteck(folie, xx, y, b, 38, s.feld, name=f'Bereich {name}')
        form.adjustments[0] = 0.5
        text(folie, xx + 18, y + 9, b - 30, 24, name, 12, s.marke, fett=True)
        xx += b + 10
    return y + 48


def szene(folie, f, s, ordner):
    """Eine Szene: links Zeit, Person, Titel, Text, Bereiche, Nutzen; rechts ein echtes Bildschirmfoto (Verwaltung oder Handy)."""
    links = 500
    y = kopf(folie, f, s, b=links, h2pt=26)
    y += 12
    if f.get('text'):
        hh = hoehe(f['text'], links, 14)
        text(folie, 128, y, links, hh, f['text'], 14, s.text, name='Text')
        y += hh + 28
    if f.get('bereiche'):
        y = pillenzeile(folie, f['bereiche'], 128, y, s, links) + 12
    g = f.get('gewinn')
    if g:
        hh = hoehe(g['satz'], links - 56, 16, 1.1)
        gy = max(y + 8, 930 - hh - 60)
        rechteck(folie, 128, gy, links, hh + 72, s.feld, name='Nutzen')
        text(folie, 156, gy + 16, links - 56, 24, g['marke'].upper(), 10, s.marke, fett=True, sperrung=200)
        text(folie, 156, gy + 42, links - 56, hh, g['satz'], 16, s.stark, fett=True, abstand=1.1)
    bilder = [f['bild']] + ([f['bild2']] if f.get('bild2') else [])
    x0 = 128 + links + 64
    breite = 1792 - x0
    with Image.open(ordner / bilder[0]) as im:
        hoch = im.height > im.width
    if hoch:
        hb = 820
        bb = []
        for b in bilder:
            with Image.open(ordner / b) as im:
                bb.append(hb * im.width / im.height)
        gesamt = sum(bb) + 64 * (len(bb) - 1)
        x = x0 + (breite - gesamt) / 2
        for b, w in zip(bilder, bb):
            rechteck(folie, x - 14, 114, w + 28, hb + 28, TINTE, name='Gerät')
            folie.shapes.add_picture(str(ordner / b), e(x), e(128), e(w), e(hb))
            x += w + 64
    else:
        bild(folie, ordner / bilder[0], x0, 128, breite, 820, True)


def kapitel(folie, f, s):
    y = 200
    text(folie, 128, y, 1600, 30, f['eyebrow'].upper(), 14, s.marke, fett=True, sperrung=300)
    y += 50
    hh = hoehe(f['kapitel'], 1600, 60, 0.95)
    text(folie, 128, y, 1600, hh, f['kapitel'], 60, s.titel, fett=True, abstand=0.95, name='Titel')
    y += hh + 30
    if f.get('lead'):
        hh = hoehe(f['lead'], 1500, 20)
        text(folie, 128, y, 1500, hh, f['lead'], 20, s.text)
        y += hh + 48
    szenen = f.get('szenen', [])
    halb = (len(szenen) + 1) // 2
    for i, z in enumerate(szenen):
        sp = 128 + (i // halb) * 820
        text(folie, sp, y + (i % halb) * 40, 800, 36, '›  ' + z, 16, s.stark)


def karten(folie, f, s):
    y = kopf(folie, f, s) + 48
    k = f['karten']
    gap = 32
    b = (1664 - gap * (len(k) - 1)) / len(k)
    ch = 0
    for kk in k:
        h = 40 + (30 if kk.get('marke') else 0) + hoehe(kk.get('titel') or '', b - 64, 20, 1.05) + 14 + hoehe(kk.get('text') or '', b - 64, 14) + 40
        ch = max(ch, h)
    for i, kk in enumerate(k):
        x = 128 + i * (b + gap)
        rechteck(folie, x, y, b, ch, s.karte, linie=None if s.dunkel else 'E3EAEA', name=f'Karte {i + 1}')
        yy = y + 36
        if kk.get('marke'):
            text(folie, x + 32, yy, b - 64, 24, kk['marke'], 11, s.marke, fett=True)
            yy += 30
        if kk.get('titel'):
            hh = hoehe(kk['titel'], b - 64, 20, 1.05)
            text(folie, x + 32, yy, b - 64, hh, kk['titel'], 20, s.stark, fett=True, abstand=1.05)
            yy += hh + 14
        if kk.get('text'):
            text(folie, x + 32, yy, b - 64, hoehe(kk['text'], b - 64, 14), kk['text'], 14, s.text)
    if f.get('lead'):
        text(folie, 128, y + ch + 40, 1500, 80, f['lead'], 16, s.text)


def pillen(folie, liste, x, y, s):
    for p in liste:
        mass = _mass(12)
        b = (mass.getlength(p) if mass else len(p) * 12) + 48
        if x + b > 1792:
            x, y = 128, y + 64
        form = rechteck(folie, x, y, b, 48, SONNE if p == liste[-1] else s.feld, name='Pille')
        form.adjustments[0] = 0.5
        tf = form.text_frame
        tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
        r = tf.paragraphs[0].add_run()
        r.text, r.font.name, r.font.size, r.font.bold = p, SCHRIFT, Pt(12), True
        r.font.color.rgb = farbe(TINTE if p == liste[-1] else (NEBEL if s.dunkel else PETROL))
        tf.paragraphs[0].alignment = PP_ALIGN.CENTER
        x += b + 16


def folie_bauen(praes, f, nr, ordner):
    folie = praes.slides.add_slide(praes.slide_layouts[6])
    s = Stil(f.get('hintergrund', 'hell'))
    hg = folie.background.fill
    hg.solid()
    hg.fore_color.rgb = farbe(s.grund)
    if f.get('kapitel'):
        kapitel(folie, f, s)
    elif f.get('bild'):
        szene(folie, f, s, ordner)
    elif f.get('h1'):
        if f.get('logo'):
            folie.shapes.add_picture(str(ordner / f['logo']), e(128), e(128), height=e(110))
        if f.get('logo2'):
            with Image.open(ordner / f['logo2']) as im:
                b = 110 * im.width / im.height
            folie.shapes.add_picture(str(ordner / f['logo2']), e(1792 - b), e(128), height=e(110))
        text(folie, 128, 560, 1600, 30, f['eyebrow'].upper(), 12, s.marke, fett=True, sperrung=300)
        text(folie, 128, 610, 1600, 240, f['h1'], 54, s.titel, fett=True, abstand=0.95, name='Titel')
        if f.get('lead'):
            text(folie, 128, 860, 1500, 80, f['lead'], 18, s.text)
    elif f.get('aussage'):
        oben = 300
        text(folie, 128, oben, 1600, 30, f['eyebrow'].upper(), 12, s.marke, fett=True, sperrung=300)
        inhalt = f['aussage']
        hh = hoehe(''.join(l['text'] for l in inhalt), 1600, 40, 1.05)
        text(folie, 128, oben + 48, 1600, hh, inhalt, 40, s.titel, fett=True, abstand=1.05, name='Aussage')
        if f.get('pillen'):
            pillen(folie, f['pillen'], 128, oben + 48 + hh + 48, s)
    elif f.get('karten'):
        karten(folie, f, s)
    fuss(folie, f, s, nr)
    if f.get('notiz'):
        folie.notes_slide.notes_text_frame.text = f['notiz']


def bauen(name):
    ordner = WURZEL / 'docs/praesentation' / name
    daten = json.loads((ordner / 'folien.json').read_text(encoding='utf-8'))
    praes = Presentation()
    praes.slide_width, praes.slide_height = e(1920), e(1080)
    praes.core_properties.title = daten['titel']
    praes.core_properties.author = 'VibeVHS'
    for nr, f in enumerate(daten['folien'], 1):
        folie_bauen(praes, f, nr, ordner)
    ziel = ordner / f'{name}.pptx'
    praes.save(ziel)
    print(f'{ziel.relative_to(WURZEL)}: {len(daten["folien"])} Folien')


if __name__ == '__main__':
    for n in sys.argv[1:] or ['woche']:
        bauen(n)
