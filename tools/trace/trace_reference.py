#!/usr/bin/env python3
"""
Vectorise a flat-colour Capri reference picture into the layered shapes the avatar rig animates.

    python3 -I tools/trace/trace_reference.py <reference.png|webp> src/Capricon.Core/Avatar/capri_trace.json

The picture is split by palette into pink (hair + skin), black (bodysuit, face), green (mask,
panels) and white (mouth, hair specks). Pastel glitch blocks, ghost shadows and the purple rim are
dropped: the app regenerates those procedurally so they can animate. The happy eyes and the
grin are exported separately so the app can swap expressions.

Tuned for the 1523x2000 reference used for the default model; another picture in the same
flat style works if the landmark boxes below are adjusted.
"""
import json
import sys

import cv2
import numpy as np

SCALE = 0.24                 # reference px -> canvas units
OFFSET = (0.0, 0.0)          # cut edges of the picture coincide with the window edges
CANVAS = (366, 480)
EPS = 1.3                    # contour simplification, reference px

PALETTE = {
    "pink": [(253, 40, 121), (249, 74, 143)],
    "black": [(3, 1, 2)],
    "green": [(8, 246, 121)],
    "white": [(254, 254, 254)],
    "rim": [(166, 59, 174)],
    "other": [(253, 206, 211), (210, 206, 252), (251, 162, 174), (170, 170, 252),
              (89, 252, 252), (254, 253, 92), (171, 253, 173), (247, 136, 248)],
}

# Landmark boxes in reference pixels (x0, y0, x1, y1).
FACE_BOX = (830, 540, 1090, 840)


def classify(rgb):
    names, cols = [], []
    for k, v in PALETTE.items():
        for c in v:
            names.append(k)
            cols.append(c)
    cols = np.array(cols, np.int32)
    best = np.full(rgb.shape[:2], 1 << 30, np.int64)
    idx = np.zeros(rgb.shape[:2], np.int32)
    px = rgb.astype(np.int32)
    for i, c in enumerate(cols):
        d = ((px - c) ** 2).sum(-1)
        better = d < best
        best[better] = d[better]
        idx[better] = i
    return np.array(names)[idx]


def inside(box, x, y, w, h):
    x0, y0, x1, y1 = box
    return x >= x0 and y >= y0 and x + w <= x1 and y + h <= y1


def to_canvas(pts):
    out = []
    for x, y in pts:
        out += [round(x * SCALE + OFFSET[0], 2), round(y * SCALE + OFFSET[1], 2)]
    return out


def rings(mask, min_hole=0, min_area=40):
    """Outer contours with their holes, simplified, in canvas coordinates."""
    contours, hier = cv2.findContours(mask, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
    shapes = []
    if hier is None:
        return shapes
    hier = hier[0]
    for i, c in enumerate(contours):
        if hier[i][3] != -1 or cv2.contourArea(c) < min_area:
            continue
        shape = [to_canvas(cv2.approxPolyDP(c, EPS, True)[:, 0, :])]
        child = hier[i][2]
        while child != -1:
            hole = contours[child]
            if cv2.contourArea(hole) >= min_hole:
                shape.append(to_canvas(cv2.approxPolyDP(hole, EPS, True)[:, 0, :]))
            child = hier[child][0]
        shapes.append(shape)
    return shapes


def fill_small_holes(mask, max_area):
    inv = cv2.bitwise_not(mask)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(inv, 8)
    out = mask.copy()
    for i in range(1, n):
        x, y, w, h, a = stats[i]
        touches = x == 0 or y == 0 or x + w >= mask.shape[1] or y + h >= mask.shape[0]
        if a <= max_area and not touches:
            out[lab == i] = 255
    return out


def main(src, dst):
    bgr = cv2.imread(src)
    if bgr is None:
        sys.exit(f"cannot read {src}")
    cls = classify(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
    k3 = np.ones((3, 3), np.uint8)

    def mask(name):
        m = (cls == name).astype(np.uint8) * 255
        return cv2.morphologyEx(m, cv2.MORPH_OPEN, k3)

    pink, black, green, white = mask("pink"), mask("black"), mask("green"), mask("white")

    # --- face: eyes (small black bits on the mask) and the grin (white inside the face)
    eyes = np.zeros_like(black)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(black, 8)
    for i in range(1, n):
        x, y, w, h, a = stats[i]
        if a < 4000 and inside(FACE_BOX, x, y, w, h):
            eyes[lab == i] = 255
            black[lab == i] = 0
    mouth = np.zeros_like(white)
    n, lab, stats, cent = cv2.connectedComponentsWithStats(white, 8)
    specks = []
    for i in range(1, n):
        x, y, w, h, a = stats[i]
        if inside(FACE_BOX, x, y, w, h) and a > 800:
            mouth[lab == i] = 255
        elif a <= 700:
            cx, cy = cent[i]
            # Only specks that sit on the pink mane/skin.
            ring = pink[max(0, y - 3):y + h + 3, max(0, x - 3):x + w + 3]
            if ring.mean() > 120:
                specks.append([round(cx * SCALE + OFFSET[0], 2), round(cy * SCALE + OFFSET[1], 2),
                               round(max(0.35, (a / np.pi) ** 0.5 * SCALE), 2)])

    # Swap-able features leave no holes behind: the face is solid black, the mask solid green.
    black = cv2.bitwise_or(black, cv2.dilate(mouth, k3, iterations=3))
    green = fill_small_holes(green, 20000)
    # The eyes reach past the mask edge, so their slots are not closed holes: patch them
    # wherever the mask surrounds them.
    closed = cv2.morphologyEx(green, cv2.MORPH_CLOSE, np.ones((25, 25), np.uint8))
    green = cv2.bitwise_or(green, cv2.bitwise_and(cv2.dilate(eyes, k3, iterations=2), closed))

    # Specks become holes we do not want; the rim/antialias seams get covered by a slight grow.
    pink = fill_small_holes(pink, 900)
    grow = np.ones((5, 5), np.uint8)
    pink, black, green = (cv2.dilate(m, grow) for m in (pink, black, green))

    def box_of(m):
        ys, xs = np.nonzero(m)
        return [round(xs.min() * SCALE + OFFSET[0], 2), round(ys.min() * SCALE + OFFSET[1], 2),
                round(xs.max() * SCALE + OFFSET[0], 2), round(ys.max() * SCALE + OFFSET[1], 2)]

    data = {
        "source": "traced from the reference picture by tools/trace/trace_reference.py",
        "width": CANVAS[0],
        "height": CANVAS[1],
        "layers": [
            {"name": "pink", "shapes": rings(pink, min_hole=600)},
            {"name": "black", "shapes": rings(black, min_hole=60)},
            {"name": "green", "shapes": rings(green, min_hole=60)},
        ],
        "eyes": rings(cv2.dilate(eyes, k3), min_area=10),
        "mouth": rings(mouth, min_area=10),
        "eyesBox": box_of(eyes),
        "mouthBox": box_of(mouth),
        "specks": specks,
    }
    with open(dst, "w") as f:
        json.dump(data, f, separators=(",", ":"))
    total = sum(len(r) // 2 for l in data["layers"] for s in l["shapes"] for r in s)
    print(f"layers: {[(l['name'], len(l['shapes'])) for l in data['layers']]}, points: {total}, "
          f"specks: {len(specks)}, eyes {data['eyesBox']}, mouth {data['mouthBox']}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
