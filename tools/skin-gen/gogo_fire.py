# ゴーゴーの炎(Lane/GoGoSoul.png)
#   11/5 に作り直した：A-Style のつくりを参考に、玉＋右上に巻いた炎3本＋たまに火の粉1つ(frame の説明を見て)。下の昔の説明は前の版のもの
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
    """11/5 たけらい「火の粉が多くて気が散る。A-Style 参考にして」→ A-Style の GoGoSoul.png のつくりを参考に作り直した
       （A-Style：丸い玉の右上から、くるっと巻いた炎が2〜4本。コマごとに長さと巻き方が変わる。火の粉はときどき1つだけ）
       シンプル：玉（半径95。ふちがゆらゆら波打つ）＋右上に巻いた炎3本（コマごとに伸び縮み）。うすいオレンジのべた塗り。
       最後に形をぼかして切り直して、角を丸くする（このとき小さい火の粉は消えるので、火の粉は実際には出ない）"""
    S = SS
    ys, xs = np.mgrid[0:H * S, 0:W * S]
    X = xs / S
    Y = ys / S
    t = f / N * 2 * math.pi
    # 玉：ふちがゆらゆら波打って、ゆっくり形が変わる(11/5「判定まわりの円、動きないじゃん」)
    dx, dy = X - CX, Y - CY
    theta = np.arctan2(dy, dx)
    ball_r = 95 + 4.5 * np.sin(2 * theta - t) + 3.0 * np.sin(3 * theta + 2 * t) + 2.0 * math.sin(t * 2)
    m = dx * dx + dy * dy <= ball_r ** 2

    def stroke(points, widths):
        nonlocal m
        for (px, py), w in zip(points, widths):
            if w > 0.3:
                m |= (X - px) ** 2 + (Y - py) ** 2 <= w * w

    # 巻いた炎(根元の角度・長さ・太さ・巻き・揺れのずれ)。根元は玉のふち
    tips = []
    for ang, length, width, curl, ph in ((-62, 150, 24, 1.5, 0.0), (-35, 115, 21, 1.25, 2.1), (-12, 72, 16, 1.0, 4.2)):
        L = length * (0.72 + 0.28 * math.sin(t + ph))            # 伸び縮み
        a = math.radians(ang + 4 * math.sin(t * 2 + ph))
        bx, by = CX + math.cos(a) * 88, CY + math.sin(a) * 88
        pts, ws = [], []
        steps = 60
        for i in range(steps + 1):
            u = i / steps
            # 根元は外向き → 先へ行くほど右上へなびいて、先がくるっと巻く
            heading = a + (math.radians(-35) - a) * u * 0.6 + curl * u ** 2.2 * (1.2 + 0.3 * math.sin(t + ph))
            if i == 0:
                x, y = bx, by
            else:
                x += math.cos(heading) * L / steps
                y += math.sin(heading) * L / steps
            pts.append((x, y))
            ws.append(width * (1 - u) ** 1.1 + 1.2 * (1 - u))
        stroke(pts, ws)
        tips.append(pts[-1])
    # 火の粉：コマ 3〜5 のあいだだけ、いちばん長い炎の先から1つちぎれて上がる
    if 3 <= f <= 5:
        u = (f - 3) / 3
        x, y = tips[0]
        m |= blob_mask(X, Y, x + 18 * u + 6, y - 26 * u - 6, 7 * (1 - 0.5 * u), math.radians(-55))
    # 角を丸める(11/5「かくかくしすぎ」)：形をぼかしてから半分で切り直す → とがった角・つなぎ目が丸くなる
    soft = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(7 * S))
    m = np.array(soft) >= 128
    rgba = np.zeros((H * S, W * S, 4), dtype=np.uint8)
    rgba[m] = (255, 146, 60, 112)
    return Image.fromarray(rgba, 'RGBA').resize((W, H), Image.LANCZOS)


def fire():
    sheet = Image.new('RGBA', (W * N, H), (0, 0, 0, 0))
    for f in range(N):
        sheet.paste(frame(f), (f * W, 0))
    save_image(sheet, E + 'Lane/GoGoSoul.png')


if __name__ == '__main__':
    fire()
