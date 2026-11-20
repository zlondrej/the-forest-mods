"""Renders VrTweaks' meat size badges (mod/VrTweaks/Icons/Badge{L,S}.png): a rounded, filled square with the letter
cut out, with a chalky grain like the game's HUD icons. White; the plugin puts them on the game's meat icon.

    tools/pyenv/Scripts/python scripts/meat-badges.py [--preview MEAT_ICON.png PREVIEW.png]

Needs Pillow and the Arial Bold font (arialbd.ttf, found by name on Windows).
"""
import os
import random
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ICON = 152            # the game's HUD icon size
BADGE = 0.24          # badge size, of the icon (MeatIcons.BadgeSize)
SIZE = round(ICON * BADGE)
SS = 8                # supersampling for the shape
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "mod", "VrTweaks", "Icons")


def shape(letter):
    big = SIZE * SS
    m = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((0, 0, big - 1, big - 1), radius=int(big * 0.24), fill=255)
    font = ImageFont.truetype("arialbd.ttf", int(big * 0.76))
    d.text((big / 2, big / 2 + big * 0.02), letter, font=font, fill=0, anchor="mm")  # cut out
    return m.resize((SIZE, SIZE), Image.LANCZOS)


def grain(seed):
    """The game's chalk: mostly opaque, softly speckled with see-through spots (alpha 0.55..1)."""
    rnd = random.Random(seed)
    fine = Image.new("L", (SIZE, SIZE))
    fine.putdata([rnd.randrange(256) for _ in range(SIZE * SIZE)])
    fine = fine.filter(ImageFilter.GaussianBlur(0.6))
    return fine.point(lambda v: int(255 * max(0.55, min(1.0, 0.55 + (v - 50) / 90.0))))


def badge(letter, seed):
    alpha = ImageChops.multiply(shape(letter), grain(seed))
    out = Image.new("RGBA", (SIZE, SIZE), (251, 251, 251, 0))
    out.putalpha(alpha)
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    badges = {"L": badge("L", 1), "S": badge("S", 2)}
    for letter, im in badges.items():
        im.save(os.path.join(OUT, f"Badge{letter}.png"))
    if len(sys.argv) == 4 and sys.argv[1] == "--preview":
        meat = Image.open(sys.argv[2]).convert("RGBA")
        sheet = Image.new("RGBA", (2 * (ICON + 20) + 2 * 74, ICON), (40, 40, 40, 255))
        x = 0
        icons = []
        for letter in "LS":
            icon = Image.new("RGBA", (ICON, ICON))
            s = round(ICON * 1.047)  # MeatIcons.MeatSize (the picture has margins), centred at MeatCentre
            icon.alpha_composite(meat.resize((s, s), Image.LANCZOS), (round(ICON * 0.489 - s / 2), round(ICON * 0.620 - s / 2)))
            icon.alpha_composite(badges[letter], (round(ICON * 0.805 - SIZE / 2), round(ICON * 0.218 - SIZE / 2)))
            icons.append(icon)
        for icon in icons:
            sheet.alpha_composite(icon, (x, 0))
            x += ICON + 20
        for icon in icons:
            sheet.alpha_composite(icon.resize((56, 56), Image.LANCZOS), (x, (ICON - 56) // 2))
            x += 74
        sheet.save(sys.argv[3])


if __name__ == "__main__":
    main()
