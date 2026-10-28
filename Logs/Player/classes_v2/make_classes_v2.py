"""네 클래스 플레이어 시안 v2 생성기 (Logs/Player/classes_v2/).

공통 규칙
- 캔버스 20x20, 몸통 가운데 픽셀 (col 9, row 10) - 지금 그림과 같아 meta(PPU·피벗)를 그대로 쓴다.
- 몸통: (x-c)^2 + (y-c)^2 <= r^2 인 원. 8방향 대칭이고, 행 폭이 끝에서 2-1-0-1칸씩 늘어 45도 직선(마름모 변)이 생기지 않는다.
- 외곽선 #2F2F2E: 원 안이면서 상하좌우 이웃 중 하나가 원 밖인 칸.
- 그늘 2겹: 외곽선 안쪽을 가운데에서의 거리로 나눈다(테가 원 모양으로 돈다) - 바깥 테 x0.71, 안쪽 테 x0.863, 가운데 바탕색.
- 손: 안쪽 3x3 + 외곽선, 바깥(오른쪽) 줄은 손 색 x0.863. 몸 오른쪽 위·아래 대각선 테두리에 반쯤 걸쳐 붙인다.
"""
import os
import struct
import sys
import zlib

W = H = 20
CX, CY = 9, 10
OUTLINE = (0x2F, 0x2F, 0x2E)
DARK, LIGHT = 0.71, 0.863


def mul(c, k):
    return tuple(max(0, min(255, round(v * k))) for v in c)


def hexc(c):
    return "#%02X%02X%02X" % c


# 지름별 원 반지름. d/2 그대로면 11은 행 폭이 5,7,9,11(매 줄 1칸씩 = 45도 직선)이라 마름모처럼 보인다.
# 11은 5.35로 줄여 3,7,9,9,11(끝에서 2-1-0-1칸씩), 13은 6.5 그대로 5,9,11,11,13(같은 꼴).
RADIUS = {11: 5.35, 13: 6.5}


def body_mask(d):
    r = RADIUS[d]
    c = (d - 1) / 2.0
    cells = set()
    for j in range(d):
        for i in range(d):
            if (i - c) ** 2 + (j - c) ** 2 <= r * r:
                cells.add((CX - (d - 1) // 2 + i, CY - (d - 1) // 2 + j))
    return cells


def outline_of(cells):
    return {(x, y) for (x, y) in cells
            if any((x + dx, y + dy) not in cells for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))}


def rings(d):
    """칸마다 0=외곽선, 1=바깥 그늘, 2=안쪽 그늘, 3=가운데."""
    cells = body_mask(d)
    edge = outline_of(cells)
    r = RADIUS[d]
    out = {}
    for (x, y) in cells:
        if (x, y) in edge:
            out[(x, y)] = 0
            continue
        dist = ((x - CX) ** 2 + (y - CY) ** 2) ** 0.5
        # 외곽선 바로 안쪽 1겹(상하좌우로 외곽선에 닿는 칸)은 항상 바깥 그늘 - 테가 끊기지 않게.
        touches_edge = any((x + dx, y + dy) in edge for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
        if touches_edge or dist > r - 2.0:
            out[(x, y)] = 1
        elif dist > r - 3.0:
            out[(x, y)] = 2
        else:
            out[(x, y)] = 3
    # 안쪽 그늘도 끊기지 않게: 가운데 칸이 바깥 그늘에 상하좌우로 닿으면 안쪽 그늘로.
    for (x, y), k in list(out.items()):
        if k == 3 and any(out.get((x + dx, y + dy)) == 1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            out[(x, y)] = 2
    return out


def put_hand(img, hx, hy, color):
    """손: (hx, hy)가 안쪽 3x3의 가운데. 외곽선을 먼저 두르고 안을 채운다."""
    for y in range(hy - 2, hy + 3):
        for x in range(hx - 2, hx + 3):
            if abs(x - hx) == 2 and abs(y - hy) == 2:
                continue  # 모서리는 비운다(둥근 손)
            img[(x, y)] = OUTLINE
    for y in range(hy - 1, hy + 2):
        for x in range(hx - 1, hx + 2):
            img[(x, y)] = mul(color, LIGHT) if x == hx + 1 else color


def shaded_body(d, color_at):
    """color_at(x, y, ring) -> 그 칸 색. None이면 바탕색에 링 그늘을 곱한다. ring 0은 외곽선."""
    img = {}
    for (x, y), k in rings(d).items():
        if k == 0:
            img[(x, y)] = OUTLINE
        else:
            img[(x, y)] = color_at(x, y, k)
    return img


def ring_shade(base):
    return lambda x, y, k: mul(base, {1: DARK, 2: LIGHT, 3: 1.0}[k])


def hand_offset(d):
    # 스카우트 시안 B(지름 13): 손 가운데 = 몸 가운데 + (6, ±5). 몸이 작아지면 같은 각도로 테두리에 걸치게 당긴다.
    return {13: (6, 5), 11: (5, 4)}[d]


# ---------------- 클래스별 ----------------

def scout():
    d = 13
    white = (0xFF, 0xFF, 0xFF)
    img = shaded_body(d, ring_shade(white))
    dx, dy = hand_offset(d)
    put_hand(img, CX + dx, CY - dy, white)
    put_hand(img, CX + dx, CY + dy, white)
    return img


TRAP_MID = (0x6B, 0x4E, 0x3B)      # 지금 트래퍼 갈색 = 안쪽 그늘 색이 되도록
TRAP_CENTER = mul(TRAP_MID, 1 / LIGHT)  # 가운데는 한 단계 밝게(스카우트의 흰 가운데처럼)
TRAP_HAND = (0xD9, 0xBC, 0xA1)


def trapper():
    d = 11
    img = shaded_body(d, ring_shade(TRAP_CENTER))
    dx, dy = hand_offset(d)
    put_hand(img, CX + dx, CY - dy, TRAP_HAND)
    put_hand(img, CX + dx, CY + dy, TRAP_HAND)
    return img


CAMO_BASE = (0x7B, 0x88, 0x5F)
# 어두운 무늬는 지금 #5E6B48이 그늘 진 바탕(#6A7552)과 밝기가 거의 같아 뭉개지므로 한 단계 어둡게 내렸다.
CAMO_DARK = (0x4A, 0x56, 0x38)
CAMO_SAND = (0xA3, 0x9B, 0x77)
# 큰 무늬 3개(몸 가운데 기준 좌표, 반지름, 색): 왼쪽 위 큰 어두운 무늬 1, 오른쪽 아래·왼쪽 아래 모래 무늬 2.
# 무늬는 그늘을 곱하지 않은 평평한 덩어리라 색 수가 늘지 않는다.
# 바깥 그늘 테는 무늬 없이 한 색으로 둘러 윤곽이 깔끔하고, 바탕(연두)만 스카우트처럼 안쪽 테 x0.863·가운데 x1로 볼록하게.
CAMO_BLOBS = [((-1.4, -1.6), 1.9, CAMO_DARK), ((1.8, 1.2), 1.25, CAMO_SAND), ((-1.9, 2.1), 0.9, CAMO_SAND)]


def camo_color(x, y, k):
    if k == 1:
        return mul(CAMO_BASE, DARK)
    px, py = x - CX, y - CY
    for (bx, by), r, c in CAMO_BLOBS:
        if (px - bx) ** 2 + (py - by) ** 2 <= r * r:
            return c
    return mul(CAMO_BASE, LIGHT if k == 2 else 1.0)


def gunner():
    d = 11
    img = shaded_body(d, camo_color)
    dx, dy = hand_offset(d)
    white = (0xFF, 0xFF, 0xFF)
    put_hand(img, CX + dx, CY - dy, white)
    put_hand(img, CX + dx, CY + dy, white)
    return img


GUN = (0x4A, 0x4E, 0x44)


def gunner_armed():
    d = 11
    img = shaded_body(d, camo_color)
    # 총: 몸 가운데 줄(row 10)로 오른쪽 끝(col 19)까지. 총신 col 13~18, 위아래 외곽선, 끝 col 19 외곽선 - 지금 그림과 같은 자리.
    for x in range(13, 19):
        img[(x, CY - 1)] = OUTLINE
        img[(x, CY + 1)] = OUTLINE
        img[(x, CY)] = GUN
    img[(19, CY)] = OUTLINE
    # 손 3x3 두 개가 총신 위아래를 쥔다(손 외곽선이 총 외곽선과 겹침).
    white = (0xFF, 0xFF, 0xFF)
    put_hand(img, 14, CY - 3, white)
    put_hand(img, 14, CY + 3, white)
    return img


# ---------------- PNG ----------------

def write_png(path, img):
    raw = bytearray()
    for y in range(H):
        raw.append(0)
        for x in range(W):
            c = img.get((x, y))
            raw += bytes((c[0], c[1], c[2], 255)) if c else bytes((0, 0, 0, 0))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def ascii_art(img):
    colors = {}
    letters = "abcdefghijklmnopqrstuvwxyz"
    lines = []
    for y in range(H):
        s = ""
        for x in range(W):
            c = img.get((x, y))
            if c is None:
                s += "."
            else:
                if c not in colors:
                    colors[c] = letters[len(colors)]
                s += colors[c]
        lines.append(s)
    legend = " ".join(f"{v}={hexc(k)}" for k, v in colors.items())
    return lines, legend, len(colors)


def write_preview(path, imgs, scale=12, gap=2):
    """확인용: 시안들을 회색 바탕에 확대해 가로로 늘어놓는다(실제 렌더 아님)."""
    pw = W * scale
    tw = len(imgs) * (pw + gap * scale) + gap * scale
    th = H * scale + 2 * gap * scale
    canvas = {}
    for i, img in enumerate(imgs):
        ox = gap * scale + i * (pw + gap * scale)
        for (x, y), c in img.items():
            for yy in range(scale):
                for xx in range(scale):
                    canvas[(ox + x * scale + xx, gap * scale + y * scale + yy)] = c
    raw = bytearray()
    bg = (0x6E, 0x73, 0x3A)
    for y in range(th):
        raw.append(0)
        for x in range(tw):
            c = canvas.get((x, y), bg)
            raw += bytes((c[0], c[1], c[2], 255))
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", tw, th, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def row_widths(d):
    cells = body_mask(d)
    ys = sorted({y for _, y in cells})
    return [sum(1 for (_, yy) in cells if yy == y) for y in ys]


if __name__ == "__main__":
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    made = []
    for name, fn in (("player_base_v2", scout), ("player_trapper_v2", trapper), ("player_gunner_v2", gunner), ("player_gunner_armed_v2", gunner_armed)):
        img = fn()
        made.append(img)
        write_png(os.path.join(out_dir, name + ".png"), img)
        lines, legend, n = ascii_art(img)
        print(f"== {name} ({n}색)")
        for l in lines:
            print("  " + l)
        print("  " + legend)
    for d in (11, 13):
        print(f"지름 {d} 행 폭: {row_widths(d)}")
    if len(sys.argv) > 2:
        write_preview(sys.argv[2], made)
