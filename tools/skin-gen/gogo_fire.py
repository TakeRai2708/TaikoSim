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
    S = SS
    ys, xs = np.mgrid[0:H * S, 0:W * S]
    X = xs / S
    Y = ys / S
    t = f / N * 2 * math.pi
    dx, dy = X - CX, Y - CY
    rho = np.hypot(dx, dy)
    theta = np.arctan2(dy, dx)  # 画面は下が +y なので、上は -90°
    # 外に行くほど右へねじる(炎の先が右へ曲がる)
    k = 0.20 + 0.05 * math.sin(t)
    th = theta - k * np.clip(rho - R, 0, None) / R
    # 玉のふちから出る炎(角度・高さ・幅・揺れのずれ)。高さは時間で伸び縮み
    radius = R * (1 + 0.04 * np.sin(theta))
    for ang, height, sigma, ph in ((-145, 45, 0.20, 0.0), (-112, 120, 0.22, 1.1), (-84, 170, 0.24, 2.3),
                                    (-56, 140, 0.21, 3.6), (-30, 70, 0.19, 4.8)):
        c = math.radians(ang) + 0.06 * math.sin(t + ph)
        h = height * (1 + 0.16 * math.sin(t * 2 + ph))
        d = np.angle(np.exp(1j * (th - c)))  # 角度の差(-π〜π)
        radius = np.maximum(radius, R + h * np.exp(-np.abs(d / sigma) ** 1.35))
    # ふちを少し波打たせる
    radius = radius + 5 * np.sin(rho * 0.07 - t * 2) * (rho > R)
    m = rho <= radius
    # 火の粉：右上へ上がって、小さくなる(8コマで入れ替わる)
    for j, (sx, sy, r0, ph) in enumerate(((330, 110, 13, 0.0), (285, 80, 9, 0.33), (370, 150, 8, 0.66))):
        u = (f / N + ph) % 1.0
        x = sx + 35 * u + 6 * math.sin(t + j)
        y = sy - 55 * u
        m |= blob_mask(X, Y, x, y, r0 * (1 - 0.55 * u), math.radians(-60))
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
