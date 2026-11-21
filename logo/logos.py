"""Sip logo concepts: family squircle tiles + ring-style tray icons, supersampled."""
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops, ImageFont

SS = 8

def hexc(h):
    h = h.lstrip('#'); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))

def gradient(size, a, b):
    a, b = np.array(hexc(a), float), np.array(hexc(b), float)
    y, x = np.mgrid[0:size, 0:size]
    t = ((x + y) / (2.0 * (size - 1)))[..., None]
    rgb = a * (1 - t) + b * t
    alpha = np.full((size, size, 1), 255.0)
    return Image.fromarray(np.concatenate([rgb, alpha], 2).astype(np.uint8), 'RGBA')

def disc(d, cx, cy, r, fill=255):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)

def thick_line(d, p0, p1, w, fill=255):
    d.line([p0, p1], fill=fill, width=int(w)); disc(d, *p0, w / 2, fill); disc(d, *p1, w / 2, fill)

def arc_band(d, c, r_out, r_in, a0, a1, fill=255, caps=True):
    """filled ring segment from a0 to a1 degrees (0 = right, clockwise in screen coords)"""
    pts = []
    N = 160
    for i in range(N + 1):
        t = math.radians(a0 + (a1 - a0) * i / N); pts.append((c + math.cos(t) * r_out, c + math.sin(t) * r_out))
    for i in range(N, -1, -1):
        t = math.radians(a0 + (a1 - a0) * i / N); pts.append((c + math.cos(t) * r_in, c + math.sin(t) * r_in))
    d.polygon(pts, fill=fill)
    if caps:
        rm, w = (r_out + r_in) / 2, (r_out - r_in) / 2
        for a in (a0, a1):
            t = math.radians(a); disc(d, c + math.cos(t) * rm, c + math.sin(t) * rm, w, fill)

# ---------------------------------------------------------------- glyphs (n = tile px)
def glyph_straw(n, small=False):
    """tumbler with a bent straw"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    P = lambda u, v: (u * n, v * n)
    # cup: tapered, rounded bottom
    top, bot = 0.36, 0.78
    d.polygon([P(0.29, top), P(0.71, top), P(0.665, bot - 0.04), P(0.335, bot - 0.04)], fill=255)
    d.rounded_rectangle([*P(0.335, bot - 0.10), *P(0.665, bot)], radius=0.05 * n, fill=255)
    # liquid line gap
    if not small:
        d.rectangle([*P(0.27, 0.445), *P(0.73, 0.475)], fill=0)
    # straw: from inside cup up, then bent to the right
    w = (0.075 if small else 0.06) * n
    thick_line(d, P(0.47, 0.40), P(0.55, 0.20), w)
    thick_line(d, P(0.55, 0.20), P(0.68, 0.20), w)
    return m

def drop_poly(cx, top, h):
    w = h * 0.72; pts = []
    for i in range(96):
        t = 2 * math.pi * i / 96
        x = math.sin(t) * math.sin(t / 2); y = -math.cos(t)
        pts.append((cx + x * w * 0.62, top + (y + 1) / 2 * h))
    return pts

def glyph_wave(n, small=False):
    """drop + two sound waves"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    d.polygon(drop_poly(0.37 * n, 0.24 * n, 0.52 * n), fill=255)
    cx, cy = 0.37 * n, 0.58 * n
    w = (0.07 if small else 0.055) * n
    arcs = [(0.25, ), (0.37, )] if not small else [(0.29, )]
    for (r,) in arcs:
        arc_band(d, 0, 0, 0, 0, 0) if False else None
        # arcs centred on the drop, opening to the right
        R = r * n
        pts_o, pts_i = [], []
        img = Image.new('L', (n, n), 0); dd = ImageDraw.Draw(img)
        dd.ellipse([cx - R - w / 2, cy - R - w / 2, cx + R + w / 2, cy + R + w / 2], fill=255)
        dd.ellipse([cx - R + w / 2, cy - R + w / 2, cx + R - w / 2, cy + R - w / 2], fill=0)
        wedge = Image.new('L', (n, n), 0); dw = ImageDraw.Draw(wedge)
        dw.pieslice([cx - 2 * n, cy - 2 * n, cx + 2 * n, cy + 2 * n], -42, 42, fill=255)
        band = ImageChops.multiply(img, wedge)
        # round caps
        for a in (-42, 42):
            t = math.radians(a); disc(ImageDraw.Draw(band), cx + math.cos(t) * R, cy + math.sin(t) * R, w / 2)
        m = ImageChops.lighter(m, band)
    return m

def glyph_ring(n, small=False):
    """top-down glass = level ring (3/4) with a straw leaving through the gap"""
    m = Image.new('L', (n, n), 0); d = ImageDraw.Draw(m)
    c = n / 2
    ro, ri = 0.30 * n, (0.20 if small else 0.225) * n
    arc_band(d, c, ro, ri, -20, 250)            # gap at top-right
    w = (0.08 if small else 0.06) * n
    thick_line(d, (c, c), (c + 0.27 * n, c - 0.27 * n), w)
    disc(d, c, c, (0.075 if small else 0.06) * n)
    return m

def tile(size, a, b, glyph):
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=0.24 * n, fill=255)
    out.paste(gradient(n, a, b), (0, 0), mask)
    gm = glyph(n, size < 40)
    out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 255)), (0, 0), gm)
    return out.resize((size, size), Image.LANCZOS)

def tray(size, accent, level=0.65, muted=False):
    """ring-style tray icon: dim track + accent level arc from 12 o'clock; muted = grey ring + slash"""
    n = size * SS
    out = Image.new('RGBA', (n, n), (0, 0, 0, 0))
    c = n / 2; ro = 0.47 * n; ri = ro - 0.19 * n
    track = Image.new('L', (n, n), 0); arc_band(ImageDraw.Draw(track), c, ro, ri, 0, 360, caps=False)
    out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 70 if not muted else 60)), (0, 0), track)
    if muted:
        g = Image.new('L', (n, n), 0); dg = ImageDraw.Draw(g)
        thick_line(dg, (c - 0.26 * n, c + 0.26 * n), (c + 0.26 * n, c - 0.26 * n), 0.15 * n)
        out.paste(Image.new('RGBA', (n, n), (200, 200, 200, 255)), (0, 0), g)
    else:
        lv = Image.new('L', (n, n), 0)
        arc_band(ImageDraw.Draw(lv), c, ro, ri, -90, -90 + 360 * level)
        out.paste(Image.new('RGBA', (n, n), hexc(accent) + (255,)), (0, 0), lv)
        dot = Image.new('L', (n, n), 0); disc(ImageDraw.Draw(dot), c, c, 0.1 * n)
        out.paste(Image.new('RGBA', (n, n), (255, 255, 255, 255)), (0, 0), dot)
    return out.resize((size, size), Image.LANCZOS)

OPTIONS = [
    dict(key='A', name='Straw', sub='tumbler + bent straw, mint soda', a='#0A5C55', b='#3FE0B5', accent='#3DDBB0', glyph=glyph_straw),
    dict(key='B', name='Ripple', sub='drop + sound waves, blue raspberry', a='#1B2E9E', b='#3FB8FF', accent='#4CA8FF', glyph=glyph_wave),
    dict(key='C', name='Rim', sub='glass from above = level ring + straw, ube', a='#4A2390', b='#C38BFF', accent='#B07CFF', glyph=glyph_ring),
]

def font(sz, bold=False):
    p = '/usr/share/fonts/opentype/inter/Inter-SemiBold.otf' if bold else '/usr/share/fonts/opentype/inter/Inter-Regular.otf'
    try: return ImageFont.truetype(p, sz)
    except Exception: return ImageFont.load_default()

def sheet(path):
    W, colw, H = 1260, 420, 620
    img = Image.new('RGBA', (W, H), (24, 24, 24, 255)); d = ImageDraw.Draw(img)
    for i, o in enumerate(OPTIONS):
        x0 = i * colw
        if i: d.line([x0, 30, x0, H - 30], fill=(48, 48, 48), width=1)
        img.alpha_composite(tile(200, o['a'], o['b'], o['glyph']), (x0 + 110, 40))
        d.text((x0 + 210, 262), f"{o['key']}  ·  {o['name']}", font=font(26, True), fill=(242, 242, 242), anchor='mm')
        d.text((x0 + 210, 294), o['sub'], font=font(15), fill=(160, 160, 160), anchor='mm')
        xx = x0 + 70
        for s in (64, 32, 24, 16):
            img.alpha_composite(tile(s, o['a'], o['b'], o['glyph']), (xx, 330 + (64 - s) // 2)); xx += s + 26
        d.text((xx + 6, 362), 'exe icon sizes', font=font(13), fill=(112, 112, 112), anchor='lm')
        strip = (x0 + 40, 420, x0 + colw - 40, 470)
        d.rounded_rectangle(strip, radius=8, fill=(32, 32, 32))
        xx = strip[0] + 18
        for s, lv in ((16, 0.65), (20, 0.65), (24, 0.3)):
            img.alpha_composite(tray(s, o['accent'], lv), (xx, 445 - s // 2)); xx += s + 18
        img.alpha_composite(tray(20, o['accent'], muted=True), (xx + 6, 435))
        d.text((xx + 36, 445), 'tray · muted', font=font(13), fill=(112, 112, 112), anchor='lm')
        z = tray(16, o['accent'], 0.65).resize((64, 64), Image.NEAREST)
        img.alpha_composite(z, (x0 + 60, 490))
        d.text((x0 + 60, 562), 'tray 16 px, zoomed', font=font(12), fill=(112, 112, 112))
        # accent sample: slider
        bx = x0 + 160
        d.text((bx, 500), 'Speakers', font=font(14), fill=(242, 242, 242), anchor='lm')
        d.text((bx + 200, 500), '42', font=font(14), fill=(160, 160, 160), anchor='rm')
        d.rounded_rectangle([bx, 518, bx + 200, 522], radius=2, fill=(70, 70, 70))
        d.rounded_rectangle([bx, 518, bx + 84, 522], radius=2, fill=hexc(o['accent']))
        disc(d, bx + 84, 520, 8, (69, 69, 69)); disc(d, bx + 84, 520, 4, hexc(o['accent']))
        d.text((bx, 552), o['accent'], font=font(13), fill=(160, 160, 160), anchor='lm')
    img.save(path)

if __name__ == '__main__':
    sheet(sys.argv[1] if len(sys.argv) > 1 else 'sip_logo_options.png')
