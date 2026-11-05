# ゴーゴーの炎(Lane/GoGoSoul.png)
#   11/5 に作り直した：玉から火の粉が流れて、根元はつながる形(frame の説明を見て)。下の昔の説明は前の版のもの
# (前の版) 作り直し
#   玉と炎を1つの形として描く：玉の上側が、上へ行くほど大きく波打ちながら右上へ伸びて、先がくるっと曲がる
#   その形を「しずく形 × 波」の式で、1ピクセルずつ中か外かを決めて塗る(3倍で描いて縮める)
#   ちぎれた火の粉(小さいしずく)が右上へ上がっていく。オレンジのべた塗り。8コマで揺れる
from common import *
import numpy as np

E = 'Image/04.EnsoGame/'
ORANGE = (240, 120, 20)

W, H, N = 480, 600, 8
CX, CY, R = 243, 297, 92
R2 = 98    # 11/5 の作り直しの玉の半径


def tongue_mask(X, Y, ox, oy, angle, length, width, t, phase, curl):
    """玉の中心(ox,oy)から angle の向きへのびる炎1本。中か外かを返す"""
    # 炎の向きの座標に直す(s：根元→先、q：横)
    ca, sa = math.cos(angle), math.sin(angle)
    dx, dy = X - ox, Y - oy
    s = dx * ca + dy * sa
    q = -dx * sa + dy * ca
    u = np.clip(s / length, 0, 1)
    # 真ん中の線を、先へ行くほど大きく横にくねらせる(先はくるっと曲げる)
    center = (np.sin(u * 3.0 - t + phase) * 0.22 * width * u + curl * width * u ** 2.2)
    # 太さ：根元は玉と同じ、先へ細くなる。ふちを波打たせる
    w = width * (1 - u) ** 0.9
    w = w * (1 + 0.18 * np.sin(u * 7.0 - t * 1.5 + phase * 2) * u)
    inside = (s >= 0) & (s <= length) & (np.abs(q - center) <= w)
    return inside


def blob_mask(X, Y, x, y, r, angle):
    """火の粉(しずく形。とがった方が angle の向き)"""
    ca, sa = math.cos(angle), math.sin(angle)
    dx, dy = X - x, Y - y
    s = dx * ca + dy * sa
    q = -dx * sa + dy * ca
    # 後ろは丸、前はとがる
    circle = s * s + q * q <= r * r
    tip = (s >= 0) & (s <= r * 2.2) & (np.abs(q) <= r * np.clip(1 - s / (r * 2.2), 0, 1) ** 1.2)
    return circle | tip


def frame(f):
    """11/5 たけらいと決めた形（見本の「火の粉が玉とつながる版」の2番・細め）
       判定枠をくるむ玉から、右上(−30°)へ丸い火の粉が流れていく。近い丸どうしは溶け合ってくっつく(メタボール)ので、
       玉の近くは1本の帯につながって見えて、先へ行くほどだんだん細く・小さくなって、ぽつぽつちぎれる。
       根元のつながりは見本より約5%太く。外はうすいオレンジ、真ん中に明るい黄色の丸。8コマでくり返す"""
    S = SS
    ys, xs = np.mgrid[0:H * S, 0:W * S]
    X = xs / S
    Y = ys / S
    u_loop = f / N

    def blob(cx, cy, r):
        return (r * r / ((X - cx) ** 2 + (Y - cy) ** 2 + 1.0)) ** 2   # 4乗：近いところだけくっつく

    field = blob(CX, CY, 80)
    rng = np.random.default_rng(5)
    n = 18
    for k in range(n):
        s0 = (k + rng.random() * 0.6) / n
        a = math.radians(-30 + rng.normal(0, 12))
        size = 0.8 + 0.4 * rng.random()
        u = (s0 + u_loop) % 1.0
        d = 75 + 160 * u
        r = (18.9 * (1 - u) ** 1.15 + 4 * u) * size        # 先へいくほどなめらかに細く(根元は約5%太く)
        field += blob(CX + math.cos(a) * d, CY + math.sin(a) * d, r)
    outer = field >= 1.0
    dx, dy = X - CX, Y - CY
    core = dx * dx + dy * dy <= 52 * 52
    rgba = np.zeros((H * S, W * S, 4), dtype=np.uint8)
    rgba[outer] = (255, 140, 60, 100)
    rgba[outer & core] = (255, 214, 120, 110)
    return Image.fromarray(rgba, 'RGBA').resize((W, H), Image.LANCZOS)


def fire():
    sheet = Image.new('RGBA', (W * N, H), (0, 0, 0, 0))
    for f in range(N):
        sheet.paste(frame(f), (f * W, 0))
    save_image(sheet, E + 'Lane/GoGoSoul.png')


if __name__ == '__main__':
    fire()
