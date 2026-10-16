"""Recolors the purple Files Dev app icons in place to the blue Files Turbo icons.

The Files icons use hues from about 240 (back of the folder) to 295 (front fold). They are mapped to a blue range
so the front stays lighter and closer to cyan and the back becomes a deeper royal blue, which keeps the shading of
the original artwork. Gray and transparent pixels, and the high contrast variants, are left alone.

Usage: python .github/scripts/Recolor-AppIcons.py [folder]   (defaults to src/Files.App/Assets/AppTiles/Dev)
Run it only once on the original purple icons, because running it on blue icons shifts them again.
"""
import colorsys
import pathlib
import sys

from PIL import Image

SOURCE_FRONT, SOURCE_BACK = 295.0, 240.0  # hues in the Files Dev icons
TARGET_FRONT, TARGET_BACK = 199.0, 216.0  # sky blue front, deeper blue back
SATURATION_BOOST = 1.4
VALUE_GAMMA = 0.92  # blue reads darker than purple at the same value, so lift it a little


def map_hue(hue):
    t = (SOURCE_FRONT - hue) / (SOURCE_FRONT - SOURCE_BACK)
    return TARGET_FRONT + (TARGET_BACK - TARGET_FRONT) * min(max(t, -0.2), 1.1)


def build_table():
    # Looked up by half-degree hue so the large splash images only do the mapping math once per hue
    return {h: map_hue(h / 2) for h in range(0, 720)}


TABLE = build_table()


def recolor(image):
    rgba = image.convert('RGBA')
    pixels = rgba.load()
    width, height = rgba.size
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            degrees = h * 360
            if s < 0.06 or not 200 <= degrees <= 330:
                continue
            new_h = TABLE[int(degrees * 2) % 720] / 360
            nr, ng, nb = colorsys.hsv_to_rgb(new_h, min(s * SATURATION_BOOST, 1.0), v ** VALUE_GAMMA)
            pixels[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return rgba


def main():
    folder = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else 'src/Files.App/Assets/AppTiles/Dev')
    for png in sorted(folder.glob('*.png')):
        original = Image.open(png).convert('RGBA')
        recolored = recolor(original)
        if recolored.tobytes() != original.tobytes():  # the white badge logos have no color to change
            recolored.save(png, optimize=True)
            print(png.name)

    ico = folder / 'Logo.ico'
    frames = []
    with Image.open(ico) as source:
        for size in sorted(source.info['sizes'], reverse=True):
            source.size = size
            frames.append(recolor(source.copy()))
    frames[0].save(ico, format='ICO', sizes=[f.size for f in frames], append_images=frames[1:])
    print(ico.name)


main()
