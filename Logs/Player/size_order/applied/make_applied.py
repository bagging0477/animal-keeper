"""적용본 생성기: 트래퍼 = 시안 A(15도트), 스카우트 = 지금(13도트), 거너·무장 거너 = 13도트.
make_size_order.py(= 지금 그림 규칙)를 그대로 쓴다. 무장 거너는 거너와 같은 몸 + 지금과 같은 자리의 총(총신 row 10 col 13~18, 끝 col 19).
총을 쥔 손은 지금 그림처럼 총신 위아래(row 7·13)에 두고, 가로는 몸 오른쪽 끝 칸(지금 11도트: col 14 → 13도트: col 15)에 맞춘다.
사용: python make_applied.py <출력 폴더>
"""
import importlib.util, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("so", os.path.join(HERE, "..", "make_size_order.py"))
so = importlib.util.module_from_spec(spec); spec.loader.exec_module(so)
v2 = so.v2

def gunner_armed(d):
    img = v2.shaded_body(d, so.camo_for(d))
    for x in range(13, 19):
        img[(x, v2.CY - 1)] = v2.OUTLINE
        img[(x, v2.CY + 1)] = v2.OUTLINE
        img[(x, v2.CY)] = v2.GUN
    img[(19, v2.CY)] = v2.OUTLINE
    white = (0xFF, 0xFF, 0xFF)
    hx = v2.CX + (d - 1) // 2
    v2.put_hand(img, hx, v2.CY - 3, white)
    v2.put_hand(img, hx, v2.CY + 3, white)
    return img

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    # 같은 규칙 확인: 지름 11로 그리면 지금 무장 거너와 0칸 차이여야 한다.
    ref = so.read_png_rgba(os.path.join(HERE, "..", "..", "..", "..", "Assets", "Sprites", "Player", "player_gunner_armed.png"))
    chk = gunner_armed(11)
    print("규칙 확인(무장 거너 11도트 vs 지금 PNG):", sum(1 for k in set(ref) | set(chk) if ref.get(k) != chk.get(k)), "칸 차이")
    imgs = {"player_gunner": so.gunner(13), "player_gunner_armed": gunner_armed(13)}
    for name, img in imgs.items():
        v2.write_png(os.path.join(out, name + "_13.png"), img)
        lines, legend, n = v2.ascii_art(img)
        print(f"== {name}_13 ({n}색)"); print("\n".join("  " + l for l in lines)); print("  " + legend)
