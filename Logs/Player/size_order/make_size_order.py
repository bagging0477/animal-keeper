"""클래스 크기 순서 시안 생성기 (Logs/Player/size_order/).

make_classes_v2.py(지금 프로젝트 그림을 만든 규칙)를 그대로 쓰고, 클래스마다 몸통 지름만 바꾼다.
- 원형 몸통(8방향 대칭), 외곽선 #2F2F2E 1도트, 그늘 2겹(x0.71 / x0.863), 손 3x3(바깥 줄 x0.863), 클래스별 색·무늬 그대로.
- 캔버스 20x20, 몸통 가운데 픽셀 (9, 10) 그대로 - meta(PPU·피벗)·클래스 배율·충돌체를 바꾸지 않는다.
- 지름 9·15를 새로 쓴다. 반지름은 행 폭이 끝에서 크게 뛰었다가 줄어드는 도트 원 꼴(45도 직선이 길게 생기지 않게):
    9: r 4.45 → 3,7,7,9,9,9,7,7,3 / 11: r 5.35 → 3,7,9,9,11,11,11 (지금) / 13: r 6.5 → 5,9,11,11,13 (지금) / 15: r 7.5 → 5,9,11,13,13,15
- 손 가운데 = 몸 가운데 + ((d-1)/2, ±((d-1)/2 - 1)) - 지금 13 → (6, 5), 11 → (5, 4)와 같은 규칙.
- 거너 무늬 3개는 몸 크기에 비례해 자리와 반지름을 늘이거나 줄인다(지금 지름 11 기준).
- 무장 거너 = 거너와 같은 몸 + 지금과 같은 자리의 총과 총을 쥔 손.
사용: python make_size_order.py <출력 폴더>   (current 세트가 지금 프로젝트 PNG와 0칸 차이인지도 확인한다)
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("v2", os.path.join(HERE, "..", "classes_v2", "make_classes_v2.py"))
v2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(v2)

v2.RADIUS.update({9: 4.45, 15: 7.5})
BASE_D = 11  # 거너 무늬를 만든 지름
BASE_BLOBS = list(v2.CAMO_BLOBS)


def hand_offset(d):
    h = (d - 1) // 2
    return h, h - 1


def scout(d):
    white = (0xFF, 0xFF, 0xFF)
    img = v2.shaded_body(d, v2.ring_shade(white))
    dx, dy = hand_offset(d)
    v2.put_hand(img, v2.CX + dx, v2.CY - dy, white)
    v2.put_hand(img, v2.CX + dx, v2.CY + dy, white)
    return img


def trapper(d):
    img = v2.shaded_body(d, v2.ring_shade(v2.TRAP_CENTER))
    dx, dy = hand_offset(d)
    v2.put_hand(img, v2.CX + dx, v2.CY - dy, v2.TRAP_HAND)
    v2.put_hand(img, v2.CX + dx, v2.CY + dy, v2.TRAP_HAND)
    return img


def camo_for(d):
    k = v2.RADIUS[d] / v2.RADIUS[BASE_D]
    v2.CAMO_BLOBS[:] = [((bx * k, by * k), r * k, c) for (bx, by), r, c in BASE_BLOBS]
    return v2.camo_color


def gunner(d):
    img = v2.shaded_body(d, camo_for(d))
    dx, dy = hand_offset(d)
    white = (0xFF, 0xFF, 0xFF)
    v2.put_hand(img, v2.CX + dx, v2.CY - dy, white)
    v2.put_hand(img, v2.CX + dx, v2.CY + dy, white)
    return img


def gunner_armed(d):
    img = v2.shaded_body(d, camo_for(d))
    for x in range(13, 19):
        img[(x, v2.CY - 1)] = v2.OUTLINE
        img[(x, v2.CY + 1)] = v2.OUTLINE
        img[(x, v2.CY)] = v2.GUN
    img[(19, v2.CY)] = v2.OUTLINE
    white = (0xFF, 0xFF, 0xFF)
    v2.put_hand(img, 14, v2.CY - 3, white)
    v2.put_hand(img, 14, v2.CY + 3, white)
    return img


SETS = {
    "current": {"scout": 13, "trapper": 11, "gunner": 11},
    "A": {"scout": 13, "trapper": 15, "gunner": 9},
    "B": {"scout": 11, "trapper": 13, "gunner": 9},
}
FILES = {"scout": "player_base", "trapper": "player_trapper", "gunner": "player_gunner", "gunner_armed": "player_gunner_armed"}


def build(sizes):
    return {
        "scout": scout(sizes["scout"]),
        "trapper": trapper(sizes["trapper"]),
        "gunner": gunner(sizes["gunner"]),
        "gunner_armed": gunner_armed(sizes["gunner"]),
    }


def read_png_rgba(path):
    """프로젝트 PNG를 {(x,y): (r,g,b)}로 읽는다(Pillow 사용)."""
    from PIL import Image
    im = Image.open(path).convert("RGBA")
    return {(x, y): im.getpixel((x, y))[:3] for y in range(im.height) for x in range(im.width) if im.getpixel((x, y))[3] > 0}


if __name__ == "__main__":
    out = sys.argv[1]
    project = os.path.join(HERE, "..", "..", "..", "Assets", "Sprites", "Player")
    report = []
    for set_name, sizes in SETS.items():
        imgs = build(sizes)
        if set_name == "current":
            for cls, img in imgs.items():
                ref = read_png_rgba(os.path.join(project, FILES[cls] + ".png"))
                diff = sum(1 for k in set(ref) | set(img) if ref.get(k) != img.get(k))
                report.append(f"current {cls}: 지금 프로젝트 PNG와 {diff}칸 차이")
            continue
        d = os.path.join(out, set_name)
        os.makedirs(d, exist_ok=True)
        for cls, img in imgs.items():
            v2.write_png(os.path.join(d, FILES[cls] + f"_{set_name}.png"), img)
            lines, legend, n = v2.ascii_art(img)
            report.append(f"== {set_name} {cls} (몸통 {sizes['gunner' if cls == 'gunner_armed' else cls]}도트, {n}색)")
            report += ["  " + l for l in lines] + ["  " + legend]
    for dd in (9, 11, 13, 15):
        report.append(f"지름 {dd} (r {v2.RADIUS[dd]}) 행 폭: {v2.row_widths(dd)}")
    text = "\n".join(report)
    with open(os.path.join(out, "pixels.txt"), "w", encoding="utf-8") as f:
        f.write(text + "\n")
    print(text)
