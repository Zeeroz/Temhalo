"""Generate Temhalo's vector identity and rendering inputs. No application code.

Run with Python 3.12+, Pillow, fonttools and brotli; then run rasterize.cjs.
Original font files and their OFL notices are fetched from a pinned Google Fonts
commit on the first run only. SVG wordmarks contain paths, not font references.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
from urllib.request import Request, urlopen

from fontTools import subset
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.roundingPen import RoundingPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "brand"
COMMIT = "1d0e46c7933245c8180fe83e47917961c913bbdb"
FONT_FILES = {
    "SpaceGrotesk": ("spacegrotesk", "SpaceGrotesk%5Bwght%5D.ttf"),
    "Inter": ("inter", "Inter%5Bopsz%2Cwght%5D.ttf"),
    "JetBrainsMono": ("jetbrainsmono", "JetBrainsMono%5Bwght%5D.ttf"),
}
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
HALO, ECHO, NIGHT, FROST = "#7CE8CD", "#B9B2FF", "#0D1218", "#F1F5F9"


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def download(url: str, destination: Path) -> None:
    if destination.exists():
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    with urlopen(Request(url, headers={"User-Agent": "Temhalo-Brand/1.0"}), timeout=45) as reply:
        destination.write_bytes(reply.read())


def fonts() -> dict:
    provenance = {"repository": "https://github.com/google/fonts", "commit": COMMIT, "fonts": []}
    for family, (directory, filename) in FONT_FILES.items():
        base = f"https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl/{directory}/"
        ttf = OUT / "fonts" / f"{family}-Variable.ttf"
        license_file = OUT / "fonts" / "licenses" / f"{family}-OFL.txt"
        download(base + filename, ttf)
        download(base + "OFL.txt", license_file)
        provenance["fonts"].append({
            "family": family, "source": base + filename, "license": base + "OFL.txt",
            "sha256": hashlib.sha256(ttf.read_bytes()).hexdigest(),
        })
        # Subset for the offline design preview only. Original complete fonts remain
        # untouched; retain all names and the original copyright / OFL metadata.
        weights = (500, 600) if family == "SpaceGrotesk" else (400, 500, 600) if family == "Inter" else (400, 500)
        for weight in weights:
            axes = {"wght": weight, **({"opsz": 14} if family == "Inter" else {})}
            font = instantiateVariableFont(TTFont(ttf), axes, inplace=False)
            options = subset.Options()
            options.name_IDs = ["*"]
            options.name_legacy = True
            options.name_languages = ["*"]
            options.layout_features = ["*"]
            sub = subset.Subsetter(options=options)
            sub.populate(unicodes=list(range(32, 384)) + list(range(0x2000, 0x2070)) + [0x2192, 0x2212, 0x25A1, 0x2713])
            sub.subset(font)
            font.flavor = "woff2"
            font.save(OUT / "fonts" / f"{family}-{weight}-Preview.woff2")
    write(OUT / "fonts" / "sources.json", json.dumps(provenance, ensure_ascii=False, indent=2) + "\n")
    return provenance


def point(cx: float, radius: float, angle: float) -> tuple[float, float]:
    a = math.radians(angle)
    return cx + radius * math.cos(a), 64 + radius * math.sin(a)


def symbol(first: str = HALO, second: str = ECHO, size: int = 128) -> str:
    # Tiny assets receive a wider aperture and a thicker line; a straight resize
    # of the master would close the gaps between the two round-ended strokes.
    if size <= 20:
        radius, cx, shift, opening, segment, stroke = 39, 56, 16, 47, 24, 14
    elif size <= 24:
        radius, cx, shift, opening, segment, stroke = 40, 56.5, 15, 44, 27, 13
    else:
        radius, cx, shift, opening, segment, stroke = 40, 57, 14, 42, 29, 12
    ax, ay = point(cx, radius, -opening)
    bx, by = point(cx, radius, opening)
    dx, dy = point(cx + shift, radius, -segment)
    ex, ey = point(cx + shift, radius, segment)
    return (
        f'<g fill="none" stroke-width="{stroke}" stroke-linecap="round">'
        f'<path stroke="{first}" d="M{ax:.3f} {ay:.3f}A{radius} {radius} 0 1 0 {bx:.3f} {by:.3f}"/>'
        f'<path stroke="{second}" d="M{dx:.3f} {dy:.3f}A{radius} {radius} 0 0 1 {ex:.3f} {ey:.3f}"/>'
        '</g>'
    )


def svg(content: str, width: int, height: int, title: str, viewbox: str | None = None) -> str:
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
        f'viewBox="{viewbox or f"0 0 {width} {height}"}" role="img" aria-labelledby="title">'
        f'<title id="title">{title}</title>{content}</svg>\n'
    )


def wordmark() -> tuple[str, int]:
    source = TTFont(OUT / "fonts" / "SpaceGrotesk-Variable.ttf")
    static = instantiateVariableFont(source, {"wght": 600}, inplace=False)
    memory = io.BytesIO()
    static.save(memory)
    memory.seek(0)
    measure = ImageFont.truetype(memory, 800)
    scale = 80 / static["head"].unitsPerEm
    glyphs, cmap = static.getGlyphSet(), static.getBestCmap()
    y_min, y_max = 1e9, -1e9
    for letter in "Temhalo":
        bounds = BoundsPen(glyphs)
        glyphs[cmap[ord(letter)]].draw(bounds)
        y_min, y_max = min(y_min, bounds.bounds[1]), max(y_max, bounds.bounds[3])
    baseline = 76.32 + (y_min + y_max) * scale / 2
    pen = SVGPathPen(glyphs)
    rounded = RoundingPen(pen, roundFunc=lambda n: round(n, 3))
    for index, letter in enumerate("Temhalo"):
        # Pillow's kerning-aware advances preserve the font's T/e pair. A small
        # optical tracking adjustment makes the wordmark compact without editing
        # or redistributing a modified typeface.
        position = (measure.getlength("Temhalo"[:index + 1]) - measure.getlength(letter)) / 10 - index * 0.6
        glyphs[cmap[ord(letter)]].draw(TransformPen(rounded, (scale, 0, 0, -scale, 152 + position, baseline)))
    width = math.ceil(152 + measure.getlength("Temhalo") / 10 - 6 * 0.6 + 24)
    return pen.getCommands(), width


def generate() -> None:
    fonts()
    letter_paths, logo_width = wordmark()
    vectors = OUT / "svg"
    tasks = []

    def render_input(path: Path, content: str, width: int, height: int) -> None:
        write(path, content)
        tasks.append({"svg": str(path.relative_to(ROOT)).replace("\\", "/"), "png": str(path.with_suffix(".png").relative_to(ROOT)).replace("\\", "/"), "width": width, "height": height})

    for label, first, second, text in [
        ("color", HALO, ECHO, FROST), ("white", "#FFFFFF", "#FFFFFF", "#FFFFFF"),
        ("black", "#000000", "#000000", "#000000"),
    ]:
        write(vectors / f"temhalo-symbol-{label}.svg", svg(symbol(first, second), 128, 128, "Temhalo — Halo décalé"))
        horizontal = f'<g transform="translate(20 20) scale(.88)">{symbol(first, second)}</g><path fill="{text}" d="{letter_paths}"/>'
        logo = svg(horizontal, logo_width, 152, "Temhalo")
        write(vectors / f"temhalo-logo-horizontal-{label}.svg", logo)
        render_input(OUT / "previews" / f"temhalo-logo-horizontal-{label}.svg", svg(f'<rect width="100%" height="100%" fill="{NIGHT if label != "black" else "#F4F7FA"}"/>{horizontal}', logo_width, 152, "Temhalo"), logo_width * 2, 304)

    for size in SIZES:
        for directory, color1, color2 in [("app", HALO, ECHO), ("tray-white", "#FFFFFF", "#FFFFFF"), ("tray-black", "#000000", "#000000")]:
            icon = svg(symbol(color1, color2, size), size, size, "Temhalo", "0 0 128 128")
            render_input(OUT / "icons" / directory / f"temhalo-{size}.svg", icon, size, size)
        # Explicit PNGs for the taskbar. Same optical master as the app icon.
        render_input(OUT / "icons" / "taskbar" / f"temhalo-{size}.svg", svg(symbol(size=size), size, size, "Temhalo", "0 0 128 128"), size, size)

    # Optional Windows packaging artwork; no manifest or installer is created.
    # Plain square assets keep the surroundings transparent and let Windows own
    # the platform tile / icon treatment.
    for scale in (100, 125, 150, 200, 400):
        for name, base_size in [("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)]:
            size = math.ceil(base_size * scale / 100)
            render_input(OUT / "windows" / f"{name}.scale-{scale}.svg", svg(symbol(), size, size, "Temhalo", "0 0 128 128"), size, size)
        sw, sh = 620 * scale // 100, 300 * scale // 100
        draw_width = 360 * scale / 100
        ratio = draw_width / logo_width
        horizontal = f'<g transform="translate(20 20) scale(.88)">{symbol()}</g><path fill="{FROST}" d="{letter_paths}"/>'
        splash = f'<g transform="translate({(sw - draw_width) / 2:.3f} {(sh - 152 * ratio) / 2:.3f}) scale({ratio:.6f})">{horizontal}</g>'
        render_input(OUT / "windows" / f"SplashScreen.scale-{scale}.svg", svg(splash, sw, sh, "Temhalo — démarrage sur fond Nuit"), sw, sh)

    render_input(OUT / "splash" / "temhalo-splash-dark.svg", svg(f'<rect width="1240" height="600" fill="{NIGHT}"/><g transform="translate({(1240-logo_width*1.5)/2:.3f} 186) scale(1.5)"><g transform="translate(20 20) scale(.88)">{symbol()}</g><path fill="{FROST}" d="{letter_paths}"/></g>', 1240, 600, "Temhalo — écran de démarrage"), 1240, 600)
    write(OUT / "render-jobs.json", json.dumps(tasks, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"vector_wordmark": [logo_width, 152], "raster_jobs": len(tasks), "font_commit": COMMIT}))


if __name__ == "__main__":
    argparse.ArgumentParser(description=__doc__).parse_args()
    generate()
