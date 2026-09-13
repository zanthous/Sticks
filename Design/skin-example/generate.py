"""Regenerate the small example .osk using only Python's standard library.

All artwork is original, drawn from these shapes. Coordinates below are logical
skin pixels; PNGs are rendered at 2x for crisp edges. See ../skinning.md.
"""

import math
from pathlib import Path
import struct
import zipfile
import zlib


ROOT = Path(__file__).resolve().parent
SCALE = 2


def chunk(kind, payload):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))


def save(name, width, height, shape, colour=(255, 255, 255), opacity=1):
    """Rasterise a signed-distance shape, with analytic edge antialiasing."""
    rows = bytearray()
    for py in range(height * SCALE):
        rows.append(0)
        y = (py + 0.5) / SCALE - height / 2
        for px in range(width * SCALE):
            x = (px + 0.5) / SCALE - width / 2
            alpha = round(255 * opacity * max(0, min(1, 0.5 - shape(x, y) * SCALE)))
            rows.extend((*colour, alpha) if alpha else (0, 0, 0, 0))
    data = b"\x89PNG\r\n\x1a\n"
    data += chunk(b"IHDR", struct.pack(">IIBBBBB", width * SCALE, height * SCALE, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(rows, 9))
    data += chunk(b"IEND", b"")
    path = ROOT / f"{name}@2x.png"
    path.write_bytes(data)
    return path


def ring(radius, thickness):
    return lambda x, y: abs(math.hypot(x, y) - radius) - thickness / 2


def polygon(points):
    def distance(x, y):
        inside = False
        nearest = math.inf
        for a, b in zip(points, points[1:] + points[:1]):
            dx, dy = b[0] - a[0], b[1] - a[1]
            t = max(0, min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy)))
            nearest = min(nearest, math.hypot(x - a[0] - t * dx, y - a[1] - t * dy))
            if (a[1] > y) != (b[1] > y) and x < a[0] + (y - a[1]) * dx / dy:
                inside = not inside
        return -nearest if inside else nearest
    return distance


def main():
    files = [
        save("sticks-playfield", 640, 640, ring(230, 1.5), opacity=0.75),
        save("sticks-playfield-background", 640, 640,
             lambda x, y: math.hypot(x, y) - 229, colour=(12, 18, 28), opacity=0.35),
        save("sticks-cursor-left", 28, 28, ring(10, 3), colour=(51, 190, 234)),
        save("sticks-cursor-right", 28, 28, ring(10, 3), colour=(255, 103, 139)),
        save("sticks-cursortrail-left", 18, 18,
             lambda x, y: math.hypot(x, y) - 5, colour=(51, 190, 234), opacity=0.55),
        save("sticks-cursortrail-right", 18, 18,
             lambda x, y: math.hypot(x, y) - 5, colour=(255, 103, 139), opacity=0.55),
        save("sticks-note-centre", 22, 22, polygon([(-5, 0), (0, -8), (5, 0), (0, 8)])),
        save("sticks-slider-head", 22, 22, polygon([(-7, -8), (-1, -8), (8, 0), (-1, 8), (-7, 8), (2, 0)])),
        save("sticks-slider-reversal", 22, 22, polygon([(-8, -8), (-2, -8), (8, 0), (-2, 8), (-8, 8), (1, 0)])),
        save("sticks-double-note", 36, 40,
             lambda x, y: abs(polygon([(-15, 0), (0, -17), (15, 0), (0, 17)])(x, y)) - 1.5),
        save("sticks-judgement", 10, 10, lambda x, y: math.hypot(x, y) - 5),
        save("sticks-click", 465, 465, ring(230, 5)),
    ]
    ini = ROOT / "skin.ini"
    ini.write_text("""[General]
Name: Sticks Minimal
Author: Sticks
Version: 2.7

[Colours]
SticksLeft: 51,190,234
SticksRight: 255,103,139
SticksOverlap: 192,92,255
""", encoding="utf-8")
    files.append(ini)
    archive = ROOT / "Sticks Minimal.osk"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as output:
        for path in files:
            info = zipfile.ZipInfo(path.name, date_time=(2026, 9, 14, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(info, path.read_bytes())
    print(f"Generated {len(files) - 1} assets and {archive.name}")


if __name__ == "__main__":
    main()
