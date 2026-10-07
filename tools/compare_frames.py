"""Compare a Godot capture with a Unity reference at the same frame and viewport.

Usage:
  python tools/compare_frames.py unity.png godot.png --reference-crop 0,60,1920,1080
  python tools/compare_frames.py unity.png godot.png --out temp/visual_review
"""

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageEnhance, ImageOps, ImageStat


def crop_arg(value: str) -> tuple[int, int, int, int]:
    parts = tuple(int(part) for part in value.split(","))
    if len(parts) != 4 or parts[2] <= 0 or parts[3] <= 0:
        raise argparse.ArgumentTypeError("crop must be x,y,width,height")
    return parts


def load(path: Path, crop: tuple[int, int, int, int] | None) -> Image.Image:
    image = Image.open(path).convert("RGB")
    if crop:
        x, y, width, height = crop
        if x < 0 or y < 0 or x + width > image.width or y + height > image.height:
            raise ValueError(f"crop {crop} lies outside {path} ({image.size})")
        image = image.crop((x, y, x + width, y + height))
    return image


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path, help="Unity reference image")
    parser.add_argument("current", type=Path, help="Godot image")
    parser.add_argument("--reference-crop", type=crop_arg)
    parser.add_argument("--current-crop", type=crop_arg)
    parser.add_argument("--out", type=Path, default=Path("temp/visual_review"))
    args = parser.parse_args()

    reference = load(args.reference, args.reference_crop)
    current = load(args.current, args.current_crop)
    if reference.size != current.size:
        raise SystemExit(
            f"Image sizes differ: Unity {reference.size}, Godot {current.size}. "
            "Capture the same viewport size or pass crop coordinates."
        )
    args.out.mkdir(parents=True, exist_ok=True)
    reference.save(args.out / "reference.png")
    current.save(args.out / "current.png")
    Image.blend(reference, current, 0.5).save(args.out / "overlay.png")
    diff = ImageChops.difference(reference, current)
    gray = diff.convert("L")
    ImageOps.colorize(ImageEnhance.Contrast(gray).enhance(2.0), black="#080b20", white="#ff4a32").save(
        args.out / "heatmap.png"
    )
    stats = ImageStat.Stat(diff)
    histogram = gray.histogram()
    changed = sum(histogram[25:]) / (reference.width * reference.height)
    summary = {
        "reference": str(args.reference.resolve()),
        "current": str(args.current.resolve()),
        "size": list(reference.size),
        "mean_absolute_rgb_error": round(sum(stats.mean) / 3, 3),
        "fraction_pixels_changed_over_24": round(changed, 5),
        "note": "Pixel difference diagnoses framing, lighting and layout; it is not a perceptual quality score.",
    }
    (args.out / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
