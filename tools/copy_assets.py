#!/usr/bin/env python3
"""Copy FBX + texture assets listed in a Unity export JSON manifest into the
Godot project, preserving subdirectory structure relative to a Unity
asset-pack root.

Usage:
  copy_assets.py <json> [--map <unity_prefix>=<godot_res_rel_dir>]...

Default map (school hallway):
  Assets/AssetTools/SceneRes/Assets_School_Hallway/ = assets/models/school_hallway
"""
import json
import shutil
import sys
from pathlib import Path

UNITY_PROJECT = Path("G:/test/FPSGame")
GODOT_ASSETS = Path("G:/FPSGame/game")

DEFAULT_MAP = {
    "Assets/AssetTools/SceneRes/Assets_School_Hallway/": "assets/models/school_hallway",
}


def main() -> int:
    args = sys.argv[1:]
    json_path = Path(args[0]) if args else Path(
        "G:/FPSGame/game/data/env_export/school_day.json")
    mapping = dict(DEFAULT_MAP)
    if "--map" in args:
        mapping = {}
        for i, a in enumerate(args):
            if a == "--map" and i + 1 < len(args):
                src, dst = args[i + 1].split("=", 1)
                if not src.endswith("/"):
                    src += "/"
                mapping[src] = dst
    data = json.loads(json_path.read_text(encoding="utf-8"))
    assets = list(data.get("fbxAssets", [])) + list(data.get("textureAssets", []))
    # Mesh sources referenced by nodes may include non-FBX model files (.obj),
    # which the Unity exporter does not list in fbxAssets.
    for n in data.get("nodes", []):
        for key in ("fbx", "colliderFbx"):
            p = n.get(key) or ""
            if p and p not in assets:
                assets.append(p)
    copied, missing, skipped = 0, [], []
    for rel in assets:
        dst = None
        for src_prefix, dst_dir in mapping.items():
            if rel.startswith(src_prefix):
                dst = GODOT_ASSETS / dst_dir / rel[len(src_prefix):]
                break
        if dst is None:
            skipped.append(rel)
            continue
        src = UNITY_PROJECT / rel
        if not src.exists():
            missing.append(rel)
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        copied += 1
    print(f"copied={copied} missing={len(missing)} skipped={len(skipped)}")
    for m in missing:
        print("MISSING:", m)
    for s in skipped[:20]:
        print("SKIPPED(no map):", s)
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
