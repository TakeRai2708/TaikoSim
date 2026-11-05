# ゴーゴーの炎(Lane/GoGoSoul.png) 作り直し
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
    """11/5 たけらいの絵(判定枠をくるむ玉＋右上へ1本の短い舌。しずく形・彗星みたいな形)に合わせて作り直した
       画面では 0.9倍なので、玉の半径 R=105 → 画面で約95px。舌の先は中心から約200(画面で約180)・右上 −30°"""
    S = SS
    ys, xs = np.mgrid[0:H * S, 0:W * S]
    X = xs / S
    Y = ys / S
    t = f / N * 2 * math.pi
    dx, dy = X - CX, Y - CY
    rho = np.hypot(dx, dy)
    theta = np.arctan2(dy, dx)  # 画面は下が +y なので、上は -90°
    # 外に行くほど少し右へねじる(舌の先が少し上へ反る)
    k = 0.10 + 0.03 * math.sin(t)
    th = theta + k * np.clip(rho - R2, 0, None) / R2
    radius = R2 * (1 + 0.025 * np.sin(theta * 3 + t))
    # しずく形の土台：右上(−32°)へ太く伸びる
    c0 = math.radians(-32) + 0.04 * math.sin(t)
    d0 = np.angle(np.exp(1j * (th - c0)))
    radius = np.maximum(radius, R2 + 100 * (1 + 0.08 * math.sin(t * 2)) * np.exp(-np.abs(d0 / 0.62) ** 1.25))
    # 上のふちにゆらめく細い炎(角度・高さ・幅・揺れのずれ)。炎っぽく見えるように、先をとがらせる
    for ang, height, sigma, ph in ((-88, 26, 0.09, 0.5), (-64, 40, 0.10, 1.7), (-44, 48, 0.10, 2.9), (-24, 44, 0.09, 4.1), (-6, 26, 0.08, 5.3)):
        c = math.radians(ang) + 0.06 * math.sin(t + ph)
        h = height * (0.75 + 0.35 * (0.5 + 0.5 * math.sin(t * 2 + ph)))
        d = np.angle(np.exp(1j * (th - c)))
        base = R2 + 100 * np.exp(-np.abs((c - c0) / 0.62) ** 1.25)
        lick = np.clip(1 - np.abs(d / sigma), 0, 1) ** 1.6
        radius = np.maximum(radius, np.where(lick > 0, base * 0.97 + h * lick, 0))
    radius = radius + 3 * np.sin(rho * 0.08 - t * 2) * (rho > R2)
    m = rho <= radius
    # 火の粉：舌の先から右上へ2つだけ(小さく)
    for j, (sx, sy, r0, ph) in enumerate(((405, 190, 8, 0.0), (430, 228, 6, 0.5))):
        u = (f / N + ph) % 1.0
        x = sx + 18 * u + 3 * math.sin(t + j)
        y = sy - 34 * u
        m |= blob_mask(X, Y, x, y, r0 * (1 - 0.6 * u), math.radians(-40))
    rgba = np.zeros((H * S, W * S, 4), dtype=np.uint8)
    rgba[m] = (*ORANGE, int(255 * 0.62))
    return Image.fromarray(rgba, 'RGBA').resize((W, H), Image.LANCZOS)


def fire():
    sheet = Image.new('RGBA', (W * N, H), (0, 0, 0, 0))
    for f in range(N):
        sheet.paste(frame(f), (f * W, 0))
    save_image(sheet, E + 'Lane/GoGoSoul.png')


if __name__ == '__main__':
    fire()
