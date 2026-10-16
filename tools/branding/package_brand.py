"""Build multiframe ICOs, proof sheets and a checked delivery manifest.

Run after build_brand.py and rasterize.cjs. --zip also packages the identity,
the design previews, BRAND.md and asset tooling, without creating an app build.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import struct
import xml.etree.ElementTree as ET
import zipfile
from functools import lru_cache
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "assets" / "brand"
SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)


def ico(folder: str, output: Path) -> None:
    # PNG-per-frame ICO is supported on the target Windows 10/11 versions.
    # Pack each optical render separately instead of rescaling the 256 px frame.
    payloads = [(n, (ASSETS / "icons" / folder / f"temhalo-{n}.png").read_bytes()) for n in SIZES]
    offset = 6 + 16 * len(payloads)
    records, data = [], []
    for size, payload in payloads:
        records.append(struct.pack("<BBBBHHII", size if size < 256 else 0, size if size < 256 else 0, 0, 0, 1, 32, len(payload), offset))
        data.append(payload)
        offset += len(payload)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(struct.pack("<HHH", 0, 1, len(payloads)) + b"".join(records) + b"".join(data))


@lru_cache(maxsize=32)
def font(family: str, size: int, weight: int = 400) -> ImageFont.FreeTypeFont:
    axes = {"wght": weight, **({"opsz": 14} if family == "Inter" else {})}
    source = instantiateVariableFont(TTFont(ASSETS / "fonts" / f"{family}-Variable.ttf"), axes, inplace=False)
    stream = io.BytesIO()
    source.save(stream)
    stream.seek(0)
    return ImageFont.truetype(stream, size)


def proofs() -> None:
    sheet = Image.new("RGBA", (1200, 680), "#0D1218")
    draw = ImageDraw.Draw(sheet)
    draw.text((36, 28), "Temhalo · Halo décalé", font=font("Inter", 18, 500), fill="#B4C1CF")
    full = Image.open(ASSETS / "previews" / "temhalo-logo-horizontal-color.png").convert("RGBA")
    full = full.resize((750, 228), Image.Resampling.LANCZOS)
    sheet.alpha_composite(full, (20, 76))
    glyph = Image.open(ASSETS / "icons" / "app" / "temhalo-256.png").convert("RGBA")
    sheet.alpha_composite(glyph, (888, 64))
    draw.text((36, 330), "Logo horizontal et symbole · Couleur sur Nuit", font=font("Inter", 16), fill="#96A7B9")
    for index, label in enumerate(("white", "black")):
        x = 24 + index * 588
        tile = Image.open(ASSETS / "previews" / f"temhalo-logo-horizontal-{label}.png").convert("RGBA").resize((552, 168), Image.Resampling.LANCZOS)
        sheet.alpha_composite(tile, (x, 412))
        draw.text((x + 12, 600), "Monochrome blanc" if label == "white" else "Monochrome noir", font=font("Inter", 16), fill="#B4C1CF")
    sheet.save(ASSETS / "previews" / "temhalo-logo-sheet.png", optimize=True)

    proof = Image.new("RGBA", (1100, 470), "#0D1218")
    draw = ImageDraw.Draw(proof)
    draw.text((32, 24), "Icônes Temhalo · tailles réelles et contrôle optique", font=font("Inter", 18, 500), fill="#F1F5F9")
    for size, x in [(16, 40), (32, 250), (48, 460), (256, 790)]:
        icon = Image.open(ASSETS / "icons" / "app" / f"temhalo-{size}.png").convert("RGBA")
        proof.alpha_composite(icon, (x, 86))
        draw.text((x, 366), f"{size} px · taille réelle", font=font("Inter", 14), fill="#B4C1CF")
        if size == 16:
            proof.alpha_composite(icon.resize((128, 128), Image.Resampling.NEAREST), (40, 172))
            draw.text((40, 316), "Agrandissement ×8", font=font("Inter", 12), fill="#96A7B9")
    draw.rectangle((236, 172, 398, 300), fill="#F4F7FA")
    for style, x, y in [("tray-white", 250, 210), ("tray-black", 260, 220)]:
        icon = Image.open(ASSETS / "icons" / style / "temhalo-16.png").convert("RGBA")
        if style == "tray-white":
            proof.alpha_composite(icon, (460, y))
            draw.text((486, y - 1), "Zone de notification sombre", font=font("Inter", 12), fill="#B4C1CF")
        else:
            proof.alpha_composite(icon, (x, y))
            draw.text((x + 26, y - 1), "Fond clair", font=font("Inter", 12), fill="#0D1218")
    draw.text((32, 426), "16 et 20 px : ouverture élargie et trait renforcé ; 24 px : ajustement intermédiaire.", font=font("Inter", 14), fill="#96A7B9")
    proof.save(ASSETS / "previews" / "temhalo-icon-proof.png", optimize=True)


def verify() -> dict:
    svg_count = png_count = 0
    for path in ASSETS.rglob("*.svg"):
        root = ET.parse(path).getroot()
        assert root.tag == "{http://www.w3.org/2000/svg}svg", path
        assert root.attrib.get("viewBox"), path
        for child in root.iter():
            assert child.tag.rsplit("}", 1)[-1] not in {"script", "text", "image", "foreignObject"}, path
            assert not any(key.endswith("href") for key in child.attrib), path
        svg_count += 1
    for path in ASSETS.rglob("*.png"):
        with Image.open(path) as image:
            image.verify()
        png_count += 1
    for folder in ("app", "taskbar", "tray-white", "tray-black"):
        for size in SIZES:
            with Image.open(ASSETS / "icons" / folder / f"temhalo-{size}.png") as image:
                assert image.size == (size, size)
                rgba = image.convert("RGBA")
                assert rgba.getpixel((0, 0))[3] == 0
                assert rgba.getbbox() is not None
    frames = {}
    for file in (ASSETS / "temhalo.ico", ASSETS / "icons" / "temhalo-tray-white.ico", ASSETS / "icons" / "temhalo-tray-black.ico"):
        with Image.open(file) as image:
            found = sorted(image.ico.sizes())
            assert found == [(n, n) for n in SIZES]
            for size in found:
                frame = image.ico.getimage(size)
                assert frame.size == size
            frames[str(file.relative_to(ROOT))] = found
    return {"svg_validated": svg_count, "png_validated": png_count, "ico_frames": frames, "no_external_svg_dependencies": True}


def main(create_zip: bool) -> None:
    ico("app", ASSETS / "temhalo.ico")
    ico("tray-white", ASSETS / "icons" / "temhalo-tray-white.ico")
    ico("tray-black", ASSETS / "icons" / "temhalo-tray-black.ico")
    proofs()
    report = verify()
    (ASSETS / "validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    manifest = []
    for path in sorted(ASSETS.rglob("*")):
        if path.is_file() and path.name != "manifest.json":
            content = path.read_bytes()
            manifest.append({"path": str(path.relative_to(ROOT)).replace("\\", "/"), "bytes": len(content), "sha256": hashlib.sha256(content).hexdigest()})
    (ASSETS / "manifest.json").write_text(json.dumps({"version": "1.0-review", "files": manifest}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if create_zip:
        archive_path = ROOT / "Temhalo-Identite-v1.zip"
        with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.write(ROOT / "BRAND.md", "BRAND.md")
            for directory in (ASSETS, ROOT / "design", ROOT / "tools" / "branding"):
                for path in sorted(directory.rglob("*")):
                    if path.is_file() and not any(part in {"__pycache__", "node_modules", ".venv"} for part in path.parts):
                        archive.write(path, path.relative_to(ROOT))
        report["archive"] = str(archive_path)
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", action="store_true")
    main(parser.parse_args().zip)
