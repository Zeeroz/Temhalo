"""Fill the literal design template with the generated symbol and offline fonts.

Writes a standalone, self-contained HTML preview and, when --inline is provided,
the same fragment for Codex's conversation preview. Does not start a server.
"""
from __future__ import annotations

import argparse
import base64
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main(inline: Path | None) -> None:
    fonts = ROOT / "assets" / "brand" / "fonts"
    faces = []
    for family, css_name, weights in [
        ("SpaceGrotesk", "Temhalo Space", (500, 600)),
        ("Inter", "Temhalo Inter", (400, 500, 600)),
        ("JetBrainsMono", "Temhalo Mono", (400, 500)),
    ]:
        for weight in weights:
            encoded = base64.b64encode((fonts / f"{family}-{weight}-Preview.woff2").read_bytes()).decode("ascii")
            faces.append(f'@font-face{{font-family:"{css_name}";src:url(data:font/woff2;base64,{encoded}) format("woff2");font-weight:{weight};font-style:normal;font-display:swap;}}')
    fragment = (ROOT / "design" / "dashboard.template.html").read_text(encoding="utf-8")
    mark = (ROOT / "assets" / "brand" / "svg" / "temhalo-symbol-color.svg").read_text(encoding="utf-8").strip()
    fragment = fragment.replace("/*@@FONT_FACES@@*/", "\n".join(faces)).replace("@@SYMBOL@@", mark)
    assert "@@" not in fragment
    assert len(fragment.encode("utf-8")) < 1_000_000
    target = ROOT / "design" / "Temhalo-dashboard.html"
    document = ('<!doctype html>\n<html lang="fr"><head><meta charset="utf-8">'
        '<meta name="viewport" content="width=device-width,initial-scale=1">'
        '<title>Temhalo — Maquette du tableau de bord</title>'
        '<style>html,body{margin:0;background:#0D1218;color-scheme:dark}body{padding:0}button{cursor:pointer}</style>'
        '</head><body>\n' + fragment + '\n</body></html>\n')
    target.write_text(document, encoding="utf-8", newline="\n")
    if inline:
        inline.parent.mkdir(parents=True, exist_ok=True)
        inline.write_text(fragment, encoding="utf-8", newline="\n")
    print(f"{target} ({len(document.encode('utf-8'))} bytes, offline fonts)")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inline", type=Path)
    main(parser.parse_args().inline)
