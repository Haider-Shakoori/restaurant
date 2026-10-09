#!/usr/bin/env python3
"""Verify the *generated* Inno wizard art is readable in the dark installer.

The Windows desktop's original Glass JPG is intentionally not altered.
This validates actual PNG pixels and the required native-dark text theme,
rather than trusting a configuration-only check.
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "desktop" / "src" / "BusinessOS.Restaurant.Desktop" / "Assets"
INSTALLER = ROOT / "desktop" / "installer" / "BusinessOS.Restaurant.iss"


def srgb_luminance(rgb: tuple[int, int, int]) -> float:
    def linear(value: int) -> float:
        channel = value / 255
        return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4

    red, green, blue = (linear(channel) for channel in rgb)
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue


def main() -> None:
    source = ASSETS / "RestaurantGlassBackground.jpg"
    generated = ASSETS / "RestaurantInstallerBackground.png"
    if not source.is_file() or not generated.is_file():
        raise SystemExit("Missing source or generated restaurant installer artwork")

    with Image.open(source) as app, Image.open(generated) as installer:
        if installer.format != "PNG":
            raise SystemExit("Installer artwork must be an actual PNG")
        if installer.mode != "RGB" or installer.size != app.size:
            raise SystemExit("Installer artwork must be opaque RGB at the source size")

        # A conservative bound: combining each channel's brightest value
        # (even if those maxima occur in different pixels) cannot understate
        # the lightest background. White text must exceed WCAG AA's 4.5:1.
        extrema = installer.getextrema()
        brightest_rgb = tuple(int(pair[1]) for pair in extrema)
        contrast = 1.05 / (srgb_luminance(brightest_rgb) + 0.05)
        if contrast < 4.5:
            raise SystemExit(
                f"Installer background too bright: conservative white-text "
                f"contrast {contrast:.2f}:1 (RGB upper bounds {brightest_rgb})"
            )

    setup = INSTALLER.read_text(encoding="utf-8")
    required = (
        "WizardStyle=modern dark",
        "WizardBackColor=#101C28",
        "WizardBackImageOpacity=225",
        "WizardBackImageFile=..\\src\\BusinessOS.Restaurant.Desktop\\Assets\\RestaurantInstallerBackground.png",
        "LicenseHelpLabel.Font.Color :=",
    )
    for directive in required:
        if directive not in setup:
            raise SystemExit(f"Missing installer contrast configuration: {directive}")

    print(
        f"Restaurant installer dark background passed: conservative "
        f"white-text contrast {contrast:.2f}:1, PNG {installer.size[0]}x{installer.size[1]}"
    )


if __name__ == "__main__":
    main()
