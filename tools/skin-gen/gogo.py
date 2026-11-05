# コンボのキラキラ(MiniTaiko/Effect.png)。ゴーゴーの炎は gogo_fire.py
from common import *
import numpy as np
import math

E = 'Image/04.EnsoGame/'

def sparkle():
    """40×40 の白い4本の角の星に、うすい灰色のふち(11/5 に作り直した)
    シンプルの数字は明るい銀なので、加算で白を足しても見えなかった(六段の動画)。
    ふちがあるので、プログラムはふつうの重ね方で描く(暗いところがある星はふつう・無い星は加算、と PlayerEffects が決める)
    本家の星は銀の数字でも金の数字でも白のまま(六段の動画の 99→100→103 コンボ)"""
    # 11/5：「ちょっと主張激しい」→ 40→32px・ふちを細く、うすい灰色に・全体を少し透かす
    S = 32
    c = Canvas(S, S)
    m = S / 2
    pts = []
    for i in range(8):
        a = i * math.pi / 4 - math.pi / 2
        r = 14 if i % 2 == 0 else 3.0
        pts.append((m + math.cos(a) * r, m + math.sin(a) * r))
    c.poly(pts, fill=(255, 255, 255, 225), outline=(100, 102, 116, 140), width=1.2)
    c.circle(m, m, 2.4, fill=(255, 255, 255, 235))
    c.save(E + 'MiniTaiko/Effect.png')

def sparkle_old():
    """(前の版)30×30 の小さい十字の星。真ん中が白、細い腕は黄色で、先にいくほど消える
    A-Style の Effect.png を測った形に合わせた(腕の長さ 約13px・太さ 約3px・腕の色 (254,254,64))
    前の版は太い光の輪がついていて、光りすぎていた"""
    S = 30
    m = (S - 1) / 2
    y, x = np.mgrid[0:S, 0:S].astype(float)
    dx, dy = np.abs(x - m), np.abs(y - m)

    def arm(along, across):
        length = np.clip(1 - along / 12.5, 0, 1) ** 1.0   # 先にいくほどうすく
        width = np.exp(-(across / 1.7) ** 2)              # 細い
        return length * width

    alpha = np.maximum(arm(dx, dy), arm(dy, dx))
    alpha = np.maximum(alpha, np.exp(-((dx ** 2 + dy ** 2) / 14.0)))  # 真ん中の丸
    white = np.clip((alpha - 0.75) / 0.25, 0, 1)[..., None]         # こいところほど白
    yellow = np.array([254, 254, 64], float)
    rgb = yellow * (1 - white) + 255 * white
    im = np.dstack([rgb, alpha[..., None] * 255]).clip(0, 255).astype(np.uint8)
    save_image(Image.fromarray(im, 'RGBA'), E + 'MiniTaiko/Effect.png')

if __name__ == '__main__':
    sparkle()
