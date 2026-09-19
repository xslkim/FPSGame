#!/usr/bin/env python3
"""Convert Unity cubemap skyboxes to equirectangular panoramas for Godot
PanoramaSkyMaterial.

Usage:
  # Unity single-image cubemap, 6 frames in a horizontal strip (+X,-X,+Y,-Y,+Z,-Z)
  cubemap_to_panorama.py strip <in.png> <out.png> [width]

  # Unity 6-sided skybox: six face files in +X,-X,+Y,-Y,+Z,-Z order
  cubemap_to_panorama.py faces <px> <nx> <py> <ny> <pz> <nz> <out.png> [width]
"""
import sys

import numpy as np
from PIL import Image

# Face axis -> (face_index in [px,nx,py,ny,pz,nz], u axis, v axis, sign)
# Standard cubemap sampling: for each output direction d, pick the dominant
# axis, then compute (s, t) in [-1, 1] on that face.


def load_faces_strip(path: str):
    im = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    h, w = im.shape[:2]
    assert w == 6 * h, f"strip must be 6x1, got {w}x{h}"
    return [im[:, i * h:(i + 1) * h, :] for i in range(6)]


def load_faces_files(paths):
    return [np.asarray(Image.open(p).convert("RGB"), dtype=np.float32) / 255.0
            for p in paths]


def cube_to_equirect(faces, out_w: int):
    face_size = faces[0].shape[0]
    out_h = out_w // 2
    # Equirect pixel centers -> direction.
    u = (np.arange(out_w) + 0.5) / out_w  # 0..1
    v = (np.arange(out_h) + 0.5) / out_h
    theta = (u - 0.5) * 2.0 * np.pi  # longitude
    phi = v * np.pi  # latitude, 0 at top
    sx = np.sin(phi)[:, None] * np.sin(theta)[None, :]
    sy = np.cos(phi)[:, None] * np.ones_like(theta)[None, :]
    sz = np.sin(phi)[:, None] * np.cos(theta)[None, :]

    ax, ay, az = np.abs(sx), np.abs(sy), np.abs(sz)
    out = np.zeros((out_h, out_w, 3), dtype=np.float32)

    # face selector: 0=+X 1=-X 2=+Y 3=-Y 4=+Z 5=-Z
    sel = np.where((ax >= ay) & (ax >= az) & (sx > 0), 0,
          np.where((ax >= ay) & (ax >= az), 1,
          np.where((ay >= az) & (sy > 0), 2,
          np.where(ay >= az, 3,
          np.where(sz > 0, 4, 5)))))

    for face in range(6):
        m = sel == face
        if not m.any():
            continue
        if face == 0:    # +X
            s, t, d = -sz[m] / ax[m], -sy[m] / ax[m], ax[m]
        elif face == 1:  # -X
            s, t, d = sz[m] / ax[m], -sy[m] / ax[m], ax[m]
        elif face == 2:  # +Y
            s, t, d = sx[m] / ay[m], sz[m] / ay[m], ay[m]
        elif face == 3:  # -Y
            s, t, d = sx[m] / ay[m], -sz[m] / ay[m], ay[m]
        elif face == 4:  # +Z
            s, t, d = sx[m] / az[m], -sy[m] / az[m], az[m]
        else:            # -Z
            s, t, d = -sx[m] / az[m], -sy[m] / az[m], az[m]
        px = np.clip(((s + 1.0) * 0.5 * (face_size - 1)).astype(np.int32), 0, face_size - 1)
        py = np.clip(((t + 1.0) * 0.5 * (face_size - 1)).astype(np.int32), 0, face_size - 1)
        out[m] = faces[face][py, px]
    return out


def main() -> int:
    args = sys.argv[1:]
    mode = args[0]
    if mode == "strip":
        faces = load_faces_strip(args[1])
        out_path = args[2]
        out_w = int(args[3]) if len(args) > 3 else 2048
    else:
        faces = load_faces_files(args[1:7])
        out_path = args[7]
        out_w = int(args[8]) if len(args) > 8 else 2048
    out = cube_to_equirect(faces, out_w)
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(out_path)
    print("saved", out_path, Image.open(out_path).size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
