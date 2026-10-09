#!/usr/bin/env python3
"""Generate BusinessOS Restaurant production launcher icons.

The mark intentionally stays text-free so it remains legible at Windows taskbar
and mobile launcher sizes. It uses the approved BusinessOS Restaurant visual
direction: near-black field, warm gold ring, and a cloche/service mark.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter


BG = (17, 18, 20, 255)
GOLD = (212, 175, 55, 255)
GOLD_DARK = (151, 116, 31, 255)
CREAM = (247, 241, 226, 255)


def make_icon(size: int) -> Image.Image:
    scale = 4
    s = size * scale
    im = Image.new("RGBA", (s, s), BG)
    d = ImageDraw.Draw(im)

    pad = int(s * 0.07)
    radius = int(s * 0.22)
    d.rounded_rectangle(
        (pad, pad, s - pad, s - pad),
        radius=radius,
        fill=BG,
        outline=GOLD_DARK,
        width=max(2, int(s * 0.018)),
    )

    cx = s // 2
    # Cloche dome.
    dome_left = int(s * 0.24)
    dome_top = int(s * 0.31)
    dome_right = int(s * 0.76)
    dome_bottom = int(s * 0.60)
    d.pieslice(
        (dome_left, dome_top, dome_right, dome_bottom + int(s * 0.18)),
        start=180,
        end=360,
        fill=GOLD,
    )
    # Knock the lower half away so the dome has a crisp restaurant silhouette.
    d.rectangle(
        (dome_left - 2, int(s * 0.50), dome_right + 2, int(s * 0.69)),
        fill=BG,
    )
    # Dome outline / rim.
    rim_y = int(s * 0.57)
    rim_w = max(3, int(s * 0.032))
    d.rounded_rectangle(
        (int(s * 0.20), rim_y, int(s * 0.80), rim_y + rim_w),
        radius=rim_w // 2,
        fill=GOLD,
    )
    # Handle.
    handle_r = int(s * 0.035)
    d.ellipse(
        (cx - handle_r, int(s * 0.265), cx + handle_r, int(s * 0.265) + handle_r * 2),
        fill=CREAM,
        outline=GOLD,
        width=max(2, int(s * 0.012)),
    )

    # BusinessOS-style lower monogram: two service strokes forming a subtle B.
    stem_x = int(s * 0.39)
    top_y = int(s * 0.66)
    bottom_y = int(s * 0.80)
    stroke = max(3, int(s * 0.022))
    d.line((stem_x, top_y, stem_x, bottom_y), fill=CREAM, width=stroke)
    d.arc(
        (stem_x - int(s * 0.012), top_y, int(s * 0.62), int(s * 0.735)),
        start=-90,
        end=90,
        fill=CREAM,
        width=stroke,
    )
    d.arc(
        (stem_x - int(s * 0.012), int(s * 0.715), int(s * 0.64), bottom_y),
        start=-90,
        end=90,
        fill=CREAM,
        width=stroke,
    )

    if size != s:
        im = im.resize((size, size), Image.Resampling.LANCZOS)
    return im


def save_desktop(root: Path) -> None:
    target = root / "desktop" / "src" / "BusinessOS.Restaurant.Desktop" / "Assets"
    target.mkdir(parents=True, exist_ok=True)
    icon = make_icon(256)
    icon.save(
        target / "BusinessOS.Restaurant.ico",
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    icon.save(target / "BusinessOS.Restaurant.png", format="PNG")

    # Keep the original JPG unchanged for the WPF Glass theme. Inno Setup
    # needs a separate, high-contrast dark PNG because its labels and controls
    # are displayed OVER this full-window artwork. This transformation runs
    # on every CI/release build, so the installed wizard never reuses the
    # bright desktop image by accident.
    background_source = target / "RestaurantGlassBackground.jpg"
    installer_background = target / "RestaurantInstallerBackground.png"
    if not background_source.exists():
        raise FileNotFoundError(f"Missing Restaurant background: {background_source}")

    with Image.open(background_source) as background:
        background.load()
        artwork = background.convert("RGB")
        artwork = artwork.filter(ImageFilter.GaussianBlur(radius=3))
        artwork = ImageEnhance.Color(artwork).enhance(0.78)
        artwork = ImageEnhance.Brightness(artwork).enhance(0.65)
        dark_veil = Image.new("RGB", artwork.size, (12, 22, 32))
        artwork = Image.blend(artwork, dark_veil, alpha=0.42)
        artwork.save(
            installer_background,
            format="PNG",
            optimize=True,
        )



def save_android(mobile: Path) -> None:
    sizes = {
        "mipmap-mdpi": 48,
        "mipmap-hdpi": 72,
        "mipmap-xhdpi": 96,
        "mipmap-xxhdpi": 144,
        "mipmap-xxxhdpi": 192,
    }
    res = mobile / "android" / "app" / "src" / "main" / "res"
    for folder, size in sizes.items():
        target = res / folder
        target.mkdir(parents=True, exist_ok=True)
        make_icon(size).save(target / "ic_launcher.png", format="PNG")


def save_ios(mobile: Path) -> None:
    appicon = mobile / "ios" / "Runner" / "Assets.xcassets" / "AppIcon.appiconset"
    contents = appicon / "Contents.json"
    if not contents.exists():
        raise FileNotFoundError(f"Missing iOS AppIcon catalog: {contents}")

    data = json.loads(contents.read_text(encoding="utf-8"))
    for entry in data.get("images", []):
        filename = entry.get("filename")
        size_text = entry.get("size")
        scale_text = entry.get("scale")
        if not filename or not size_text or not scale_text:
            continue
        points = float(size_text.split("x", 1)[0])
        scale = int(scale_text.rstrip("x"))
        pixels = int(round(points * scale))
        make_icon(pixels).save(appicon / filename, format="PNG")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", default=".")
    parser.add_argument("--desktop", action="store_true")
    parser.add_argument("--mobile", action="store_true", help="Generate both Android and iOS icons.")
    parser.add_argument("--android", action="store_true")
    parser.add_argument("--ios", action="store_true")
    args = parser.parse_args()

    root = Path(args.repo_root).resolve()
    if not any((args.desktop, args.mobile, args.android, args.ios)):
        args.desktop = args.mobile = True

    if args.desktop:
        save_desktop(root)

    mobile = root / "mobile"
    if args.mobile or args.android:
        save_android(mobile)
    if args.mobile or args.ios:
        save_ios(mobile)


if __name__ == "__main__":
    main()
