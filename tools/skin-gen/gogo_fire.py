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
    """11/5 たけらい「A-Style の右上の炎の形に寄せて」→ A-Style の GoGoSoul.png を1コマずつ見て、つくりを合わせた
       ・炎の先は「とがっている」（丸くしない）
       ・細い炎が S字にくねりながら、玉の右上の肩から「上向き」に立ち、先で右へフックのように曲がる
       ・1コマに長さのちがう炎が 2〜4本。コマごとに形が大きく変わる（ちらちらする）
       ・ときどき、両はしがとがった葉っぱ形のかけらが右上へちぎれて飛ぶ
       ・玉の右上はこぶのように盛り上がって、そこから炎が分かれる。炎の根元は玉となめらかにつながる
       玉（半径95）はふちがゆらゆら波打つ。うすいオレンジのべた塗り"""
    S = SS
    ys, xs = np.mgrid[0:H * S, 0:W * S]
    X = xs / S
    Y = ys / S
    t = f / N * 2 * math.pi
    dx, dy = X - CX, Y - CY
    theta = np.arctan2(dy, dx)
    # 11/5：玉は判定枠の小さい丸(半径約50)より大きく、大きい丸(約77)より小さく → 画面で約63 ＝ 0.9倍で描くので 70
    BALL = 70
    ball_r = BALL + 2.0 * np.sin(2 * theta - t) + 1.2 * np.sin(3 * theta + 2 * t) + 1.2 * math.sin(t * 2)   # ほぼ丸。少しだけゆれる
    # 右上の肩を少し盛り上げる（A-Style も、玉の右上がこぶのようにふくらんで、そこから炎が分かれている）
    hump_c = math.radians(-48)
    hd = np.angle(np.exp(1j * (theta - hump_c)))
    ball_r = ball_r + 9 * np.exp(-(hd / 0.5) ** 2)
    m = dx * dx + dy * dy <= ball_r ** 2
    rng = np.random.default_rng(100 + f)          # コマごとにちがう形（決まった乱数なので毎回同じ画像になる）

    def stroke(points, widths):
        nonlocal m
        for (px, py), w in zip(points, widths):
            if w > 0.25:
                m |= (X - px) ** 2 + (Y - py) ** 2 <= w * w

    def curve(x, y, heading, length, turn, wiggle, steps=70, waves=1.3):
        """くねる線：先へ行くほど turn だけ右へ曲がり、wiggle の大きさで waves 回くねる"""
        pts = []
        for i in range(steps + 1):
            u = i / steps
            h = heading + turn * u ** 1.6 + wiggle * math.sin(2 * math.pi * u * waves)
            if i:
                x += math.cos(h) * length / steps
                y += math.sin(h) * length / steps
            pts.append((x, y))
        return pts

    # 炎 3〜4本：玉の上〜右上（−95°〜−30°）から上向きに立って、2〜3回くねりながら右へなびく
    count = int(rng.integers(3, 5))
    bases = np.sort(rng.uniform(-85, -20, count))
    for k, b in enumerate(bases):
        a = math.radians(b)
        rr = BALL + 9 * math.exp(-((a - hump_c) / 0.5) ** 2) - 12
        bx, by = CX + math.cos(a) * rr, CY + math.sin(a) * rr
        length = rng.uniform(55, 130) * (1.0 + 0.25 * (b > -60))          # 右上側の炎ほど長め
        heading = math.radians(rng.uniform(-62, -34))                   # ななめ右上へ(11/5「もう少し寝かせて」)
        turn = math.radians(rng.uniform(30, 70))                        # 先へ行くほど右へ
        wiggle = math.radians(rng.uniform(18, 32)) * (1 if rng.random() < 0.5 else -1)
        waves = rng.uniform(1.0, 1.6)
        w0 = rng.uniform(13, 19)
        pts = curve(bx, by, heading, length, turn, wiggle, 80, waves)
        n = len(pts)
        ws = []
        for i in range(n):
            u = i / (n - 1)
            lobe = 1 + 0.22 * math.sin(2 * math.pi * u * waves + 1.0)       # ところどころふくらむ
            ws.append(w0 * (1 - u) ** 1.05 * lobe * (1 + 0.3 * math.exp(-u / 0.1)))
        stroke(pts, ws)
        # 長い炎は、ときどき途中から小さい枝が出る
        if length > 85 and rng.random() < 0.6:
            j = int(n * rng.uniform(0.35, 0.55))
            bxs, bys = pts[j]
            sub = curve(bxs, bys, heading + turn * (j / n) ** 1.6 - math.radians(35), length * 0.35, math.radians(50), 0, 30)
            stroke(sub, [ws[j] * 0.55 * (1 - i / 30) for i in range(31)])

    # 葉っぱ形のかけら：半分くらいのコマで 1〜2個、右上へ
    for j in range(int(rng.integers(0, 3)) if rng.random() < 0.6 else 0):
        x0 = CX + rng.uniform(110, 190); y0 = CY - rng.uniform(100, 170)
        pts = curve(x0, y0, math.radians(rng.uniform(-70, -30)), rng.uniform(30, 48), math.radians(rng.uniform(40, 80)), 0, 30)
        n = len(pts)
        stroke(pts, [rng.uniform(5, 7.5) * math.sin(math.pi * i / (n - 1)) ** 0.8 for i in range(n)])   # 両はしがとがる
    # 玉と炎のつなぎ目だけ少しなめらかに（ぼかしは小さく。先のとがりは残す）
    soft = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2.2 * S))
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
