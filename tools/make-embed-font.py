#!/usr/bin/env python3
"""Builds the probe face the embedded-font fixture carries (#62).

Every glyph is a solid block filling the full em - advance 1000 on a 1000-unit em - so a line
set in it is emphatically wider than the same line in any substitute a fallback would pick,
and its ink is a rectangle an instrument can measure. The name is one no machine has installed,
which is the point: only the embedded bytes can render it.

Output: tests/n8PDF.Tests/Fixtures/Fonts/n8PDFProbe.ttf (committed; regenerate only if the
probe needs to change).
"""
import os
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

upm = 1000
chars = {}
for c in range(0x20, 0x7F):
    chars[c] = f"g{c:02X}" if chr(c) not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" else chr(c)
glyph_order = [".notdef"] + [chars[c] for c in sorted(chars)]
fb = FontBuilder(upm, isTTF=True)
fb.setupGlyphOrder(glyph_order)
fb.setupCharacterMap({c: n for c, n in chars.items()})

def block(x0, y0, x1, y1):
    pen = TTGlyphPen(None)
    pen.moveTo((x0, y0)); pen.lineTo((x1, y0)); pen.lineTo((x1, y1)); pen.lineTo((x0, y1)); pen.closePath()
    return pen.glyph()

glyphs = {".notdef": block(50, 0, 950, 700)}
for c, n in chars.items():
    glyphs[n] = TTGlyphPen(None).glyph() if chr(c) == " " else block(50, 0, 950, 700)
fb.setupGlyf(glyphs)
fb.setupHorizontalMetrics({n: (1000, 50) if n != ".notdef" else (1000, 50) for n in glyph_order})
fb.setupHorizontalHeader(ascent=800, descent=-200)
fb.setupNameTable({"familyName": "n8PDF Probe", "styleName": "Regular",
                   "uniqueFontIdentifier": "n8PDFProbe-Regular",
                   "fullName": "n8PDF Probe", "psName": "n8PDFProbe-Regular", "version": "Version 1.0"})
# fsType 0 is installable embedding. Anything restrictive makes Word open a document
# carrying the font read-only, which among other things refuses a scripted export (#62).
fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, sTypoLineGap=0,
            usWinAscent=800, usWinDescent=200, achVendID="n8pd", fsType=0)
fb.setupPost()
font = fb.font
font["head"].created = font["head"].modified = 3600000000
out = os.path.join(os.path.dirname(__file__), "..", "tests", "n8PDF.Tests", "Fixtures", "Fonts", "n8PDFProbe.ttf")
font.save(os.path.normpath(out))
print("wrote", os.path.normpath(out), os.path.getsize(os.path.normpath(out)), "bytes")

# And a narrow sibling of the same family: same name, half the advance. It exists so a test can
# put an installed face and an embedded face of one name side by side and see which one wins.
fb2 = FontBuilder(upm, isTTF=True)
fb2.setupGlyphOrder(glyph_order)
fb2.setupCharacterMap({c: n for c, n in chars.items()})
glyphs2 = {".notdef": block(25, 0, 475, 700)}
for c, n in chars.items():
    glyphs2[n] = TTGlyphPen(None).glyph() if chr(c) == " " else block(25, 0, 475, 700)
fb2.setupGlyf(glyphs2)
fb2.setupHorizontalMetrics({n: (500, 25) for n in glyph_order})
fb2.setupHorizontalHeader(ascent=800, descent=-200)
fb2.setupNameTable({"familyName": "n8PDF Probe", "styleName": "Regular",
                    "uniqueFontIdentifier": "n8PDFProbe-Narrow", "fullName": "n8PDF Probe Narrow",
                    "psName": "n8PDFProbe-Narrow", "version": "Version 1.0"})
fb2.setupOS2(sTypoAscender=800, sTypoDescender=-200, sTypoLineGap=0,
             usWinAscent=800, usWinDescent=200, achVendID="n8pd", fsType=0)
fb2.setupPost()
fb2.font["head"].created = fb2.font["head"].modified = 3600000000
out2 = os.path.normpath(out).replace("n8PDFProbe.ttf", "n8PDFProbe-Narrow.ttf")
fb2.font.save(out2)
print("wrote", out2, os.path.getsize(out2), "bytes")
