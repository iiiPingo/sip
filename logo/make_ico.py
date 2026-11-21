"""Writes sip.ico from a logo option (default B): tiles at 16..256 (32-bit DIB below 256, PNG at 256)."""
import io, struct, sys
from logos import tile, OPTIONS

opt = {o['key']: o for o in OPTIONS}[sys.argv[2] if len(sys.argv) > 2 else 'B']
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
entries = []
for s in sizes:
    im = tile(s, opt['a'], opt['b'], opt['glyph']).convert('RGBA')
    if s >= 256:
        b = io.BytesIO(); im.save(b, 'PNG'); data = b.getvalue()
    else:
        px = im.tobytes('raw', 'BGRA')
        rows = [px[y * s * 4:(y + 1) * s * 4] for y in range(s)][::-1]
        mask_row = ((s + 31) // 32) * 4
        hdr = struct.pack('<IiiHHIIiiII', 40, s, s * 2, 1, 32, 0, s * s * 4 + mask_row * s, 0, 0, 0, 0)
        data = hdr + b''.join(rows) + b'\x00' * (mask_row * s)
    entries.append((s, data))
out = io.BytesIO()
out.write(struct.pack('<HHH', 0, 1, len(entries)))
off = 6 + 16 * len(entries)
for s, d in entries:
    out.write(struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(d), off)); off += len(d)
for s, d in entries:
    out.write(d)
open(sys.argv[1], 'wb').write(out.getvalue())
print('wrote', sys.argv[1], len(out.getvalue()), 'bytes')
