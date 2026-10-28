"""트래퍼 포획망 휘두르기 시안 (Logs/Trapper/net_swing/).

좌표: 캔버스 36x48 텍셀, 1텍셀 = 1/16칸. 원점(플레이어 가운데 = 포획망 판정 원점)은 왼쪽 아래에서 (2, 24) 텍셀 - 스프라이트 피벗으로 쓴다.
+x = 플레이어가 바라보는 방향(LookDirection). 판정: 원점에서 2칸(32텍셀) 안, 바라보는 방향에서 ±45도 안.
그림의 모든 칸은 칸 가운데가 원점에서 32텍셀 안, ±45.5도 안에 있도록 그린다(바깥 끝 = 판정 경계).
"""
import math
import os
import struct
import sys
import zlib

W, H = 36, 48
OX, OY = 2.0, 24.0
R = 32.0          # 판정 거리 2칸 = 32텍셀
HALF = 45.0       # 판정 각도 90도의 절반

OUTLINE = (0x2F, 0x2F, 0x2E)
ROPE = (0xD2, 0xBC, 0x9F)      # 밝은 밧줄 - 트래퍼 손 #D9BCA1과 같은 계열, 채도 조금 낮게
ROPE_MID = (0xA8, 0x91, 0x7A)  # 그물코
ROPE_DARK = (0x7D, 0x6A, 0x58) # 뭉친 그물 그늘
WOOD = (0x6B, 0x4E, 0x3B)      # 자루 - 트래퍼 몸 갈색
TRAIL = (0xE3, 0xDA, 0xCB)     # 휘두른 자국(밝음)
TRAIL_MID = (0xB9, 0xAD, 0x99) # 휘두른 자국(안쪽)
WEIGHT = (0x8E, 0x8B, 0x84)    # 그물 가장자리 추


def polar(px, py):
    dx, dy = px + 0.5 - OX, py + 0.5 - OY
    return math.hypot(dx, dy), math.degrees(math.atan2(dy, dx))


def at(r, deg):
    a = math.radians(deg)
    return OX + r * math.cos(a), OY + r * math.sin(a)


class Canvas:
    def __init__(self):
        self.px = {}
        self.outlined = set()   # 외곽선을 두를 칸

    def set(self, x, y, c, outline=True, over=True):
        if not (0 <= x < W and 0 <= y < H):
            return
        if not over and (x, y) in self.px:
            return
        self.px[(x, y)] = c
        if outline:
            self.outlined.add((x, y))
        else:
            self.outlined.discard((x, y))

    def band(self, r0, r1, a0, a1, c, outline=False):
        for y in range(H):
            for x in range(W):
                r, a = polar(x, y)
                if r0 <= r <= r1 and a0 <= a <= a1:
                    self.set(x, y, c, outline)

    def line(self, p, q, c, width=0.0, outline=True, over=True):
        (x0, y0), (x1, y1) = p, q
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * 4) + 1
        for i in range(n + 1):
            t = i / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            if width <= 0:
                self.set(int(math.floor(x)), int(math.floor(y)), c, outline, over)
            else:
                for yy in range(int(y - width) - 1, int(y + width) + 2):
                    for xx in range(int(x - width) - 1, int(x + width) + 2):
                        if math.hypot(xx + 0.5 - x, yy + 0.5 - y) <= width:
                            self.set(xx, yy, c, outline, over)

    def ring(self, cx, cy, radius, thick, c, outline=True):
        for y in range(H):
            for x in range(W):
                if abs(math.hypot(x + 0.5 - cx, y + 0.5 - cy) - radius) <= thick:
                    self.set(x, y, c, outline)

    def disk(self, cx, cy, radius, c, outline=True, over=True):
        for y in range(H):
            for x in range(W):
                if math.hypot(x + 0.5 - cx, y + 0.5 - cy) <= radius:
                    self.set(x, y, c, outline, over)

    def finish(self):
        """외곽선: 표시한 칸의 상하좌우 빈 칸을 #2F2F2E로. 판정 경계(32텍셀, ±45.5도) 밖으로는 두르지 않는다."""
        add = []
        for (x, y) in self.outlined:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                q = (x + dx, y + dy)
                if q in self.px or not (0 <= q[0] < W and 0 <= q[1] < H):
                    continue
                r, a = polar(*q)
                if r <= R and abs(a) <= HALF + 0.5:
                    add.append(q)
        for q in add:
            self.px[q] = OUTLINE
        return self.px


# ---------------- 시안 A: 자루 달린 뜰채를 크게 휘두름 ----------------
# 손(몸 오른쪽 아래 손 근처)에서 자루를 뻗어 끝의 둥근 그물(테 반지름 5.5)을 오른쪽(-45도)에서 왼쪽(+45도)으로 쓸어 낸다.
# 테 바깥 끝 = 32텍셀, 테가 가장 바깥으로 갔을 때 가장자리 = ±45도. 지나간 자리는 판정 경계를 따라 호(자국)로 남는다.
HOOP_R = 5.5
HOOP_C = R - HOOP_R - 1.5      # 테 가운데까지 거리(테 + 외곽선이 32 안에 들어가게)
HOOP_SWING = HALF - math.degrees(math.asin((HOOP_R + 1.0) / HOOP_C))  # 테 가장자리가 ±45에 닿는 가운데 각도


def hoop_frame(theta, trail_from=None, trail_to=None, thin=False, reach=HOOP_C):
    cv = Canvas()
    if trail_from is not None:
        if thin:
            cv.band(R - 2.5, R - 1.0, trail_from, trail_to, TRAIL, outline=False)
        else:
            cv.band(R - 4.5, R - 2.5, trail_from, trail_to, TRAIL_MID, outline=False)
            cv.band(R - 2.5, R - 1.0, trail_from, trail_to, TRAIL, outline=False)
            # 자국 바깥 끝 = 판정 경계: 외곽선 한 줄
            cv.band(R - 1.0, R, trail_from, trail_to, OUTLINE, outline=False)
    hc = at(reach, theta)
    # 자루: 손 근처(6텍셀)에서 테 안쪽까지
    cv.line(at(5.0, theta - 8), (hc[0] - (HOOP_R) * math.cos(math.radians(theta)), hc[1] - HOOP_R * math.sin(math.radians(theta))), WOOD, width=0.75)
    # 그물코: 테 안을 가로지르는 두 줄
    for k in (-45, 45):
        a = math.radians(theta + k)
        p = (hc[0] - HOOP_R * math.cos(a), hc[1] - HOOP_R * math.sin(a))
        q = (hc[0] + HOOP_R * math.cos(a), hc[1] + HOOP_R * math.sin(a))
        cv.line(p, q, ROPE_MID, outline=False, over=False)
    cv.ring(hc[0], hc[1], HOOP_R, 0.6, ROPE)
    return cv.finish()


def variant_a():
    s = HOOP_SWING
    return [
        ("A1_windup", hoop_frame(-s)),
        ("A2_sweep", hoop_frame(-s / 3, -HALF, -s / 3)),
        ("A3_sweep", hoop_frame(s / 3, -HALF, s / 3)),
        ("A4_full", hoop_frame(s, -HALF, HALF)),
        ("A5_fade", hoop_frame(s, 0.0, HALF, thin=True)),
        ("A6_return", hoop_frame(s * 0.6, reach=HOOP_C * 0.62)),
    ]


# ---------------- 시안 B: 그물을 펼쳐 던졌다 거둠 ----------------
# 손 앞에서 뭉친 그물이 부채꼴로 펼쳐져 판정 범위(32텍셀, ±45도)를 그대로 덮고, 다시 손으로 거둬진다.
def fan_frame(radius, half, wobble=0.0):
    cv = Canvas()
    inner = 6.0
    # 그물코: 부채꼴 안 방사선과 호(외곽선 없이 가늘게)
    steps = 3
    for i in range(-steps, steps + 1):
        a = half * i / steps
        cv.line(at(inner, a), at(radius - 1.5, a), ROPE_MID, outline=False)
    for frac in (0.45, 0.72):
        rr = inner + (radius - inner) * frac
        cv.band(rr - 0.5, rr + 0.5, -half, half, ROPE_MID, outline=False)
    # 테두리 밧줄: 바깥 호 + 양쪽 끝 방사선(외곽선 두름)
    rim = radius - 1.5
    for y in range(H):
        for x in range(W):
            r, a = polar(x, y)
            if abs(a) > half:
                continue
            w = wobble * math.sin(math.radians(a) * 9.0)
            if rim - 0.75 + w <= r <= rim + 0.5 + w:
                cv.set(x, y, ROPE)
    for a in (-half + 1.5, half - 1.5):
        cv.line(at(inner, a), at(rim, a), ROPE)
    # 추: 테두리 밧줄 위 방사선 끝마다 한 칸
    for i in range(-steps, steps + 1):
        a = (half - 1.5) * i / steps
        x, y = at(rim, a)
        cv.set(int(math.floor(x)), int(math.floor(y)), WEIGHT)
    return cv.finish()


def bundle_frame(dist, size):
    cv = Canvas()
    cx, cy = at(dist, 0)
    cv.disk(cx, cy, size, ROPE)
    cv.disk(cx + 0.8, cy - 0.8, size * 0.55, ROPE_MID, outline=True)
    cv.set(int(cx - 1), int(cy + 1), ROPE_DARK)
    cv.set(int(cx + 1), int(cy), ROPE_DARK)
    return cv.finish()


def variant_b():
    return [
        ("B1_bundle", bundle_frame(9.0, 3.0)),
        ("B2_open", fan_frame(19.0, 27.0)),
        ("B3_full", fan_frame(R, HALF)),
        ("B4_retract", fan_frame(21.0, 30.0, wobble=1.0)),
        ("B5_bundle", bundle_frame(10.0, 3.5)),
    ]


# ---------------- PNG ----------------

def write_png(path, img, w=W, h=H):
    raw = bytearray()
    for row in range(h):
        y = h - 1 - row  # 위에서부터
        raw.append(0)
        for x in range(w):
            c = img.get((x, y))
            raw += bytes((c[0], c[1], c[2], 255)) if c else bytes((0, 0, 0, 0))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b""))


def stats(img):
    rmax = max(polar(x, y)[0] for (x, y) in img)
    amin = min(polar(x, y)[1] for (x, y) in img)
    amax = max(polar(x, y)[1] for (x, y) in img)
    xs = [x for x, _ in img]
    ys = [y for _, y in img]
    return rmax, amin, amax, len(set(img.values())), (max(xs) - min(xs) + 1, max(ys) - min(ys) + 1)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for name, img in variant_a() + variant_b():
        write_png(os.path.join(out, f"net_{name}.png"), img)
        rmax, amin, amax, ncol, size = stats(img)
        print(f"net_{name}: 색 {ncol}, 그린 범위 {size[0]}x{size[1]}텍셀, 원점에서 가장 먼 칸 가운데 {rmax:.2f}텍셀({rmax / 16:.3f}칸), 각도 {amin:+.1f}~{amax:+.1f}도")
