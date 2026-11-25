#!/usr/bin/env python3
"""Bildschirmfotos (PNG aus tools/praesentation/app-bilder.mjs, schon ohne Seitenleiste und Kopfzeile) für die Folien aufbereiten:
leere Fläche unten und rechts abschneiden, JPEG höchstens 2400 Pixel breit; Handyansichten bleiben unbeschnitten.

Aufruf:  py -3 docs/praesentation/woche/bilder.py <ordner mit png> [name …]
"""
import sys
from pathlib import Path

from PIL import Image, ImageStat

ZIEL = Path(__file__).resolve().parent / 'bilder'


def leer(g, box):
    return ImageStat.Stat(g.crop(box)).stddev[0] <= 2


def aufbereiten(quelle: Path):
    im = Image.open(quelle).convert('RGB')
    if im.width > im.height and not quelle.stem.endswith('-monitor'):
        g = im.convert('L')
        w, h = g.size
        unten = h
        while unten > h // 3 and leer(g, (0, unten - 8, w, unten)):
            unten -= 8
        rechts = w
        while rechts > w // 2 and leer(g, (rechts - 8, 0, rechts, unten)):
            rechts -= 8
        im = im.crop((0, 0, min(w, rechts + 40), min(h, unten + 40)))
        if im.width > 2400:
            im = im.resize((2400, int(im.height * 2400 / im.width)), Image.LANCZOS)
    ZIEL.mkdir(exist_ok=True)
    im.save(ZIEL / (quelle.stem + '.jpg'), quality=86, optimize=True)


if __name__ == '__main__':
    ordner = Path(sys.argv[1])
    namen = set(sys.argv[2:])
    for f in sorted(ordner.glob('*.png')):
        if (not namen or f.stem in namen) and not f.stem.startswith('x-'):
            aufbereiten(f)
            print('ok', f.stem)
