# コンボのキラキラ(MiniTaiko/Effect.png)。ゴーゴーの炎は gogo_fire.py
from common import *
import numpy as np

E = 'Image/04.EnsoGame/'

def sparkle():
    """30×30 の小さい十字の星。真ん中が白、細い腕は黄色で、先にいくほど消える
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
