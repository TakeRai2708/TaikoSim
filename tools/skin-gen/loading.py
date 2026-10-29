# 曲の読み込み画面の画像(Image/03.SongLoading/)。TaikøNauts と同じ名前・同じ大きさ・同じ使い方
#   Simple スキンなので、本家の再現はしない。必要なものだけ、いちばん簡単な形で作る
#   Background.png       1920×1080 … 幕が閉じたときの背景(上が黒 → 下がこい灰色)
#   Background_Fade.png  1920×1080 … 幕(背景と同じ色。真ん中に下が広いすき間。すき間のふちに細い灰色の線)
#   Chara.png            1920×1080 … 左下に赤丸・右下に青丸(白い細いふちだけ。文字や影は無し)
#   Plate.png            1600×270  … 曲名の板(両はしが透明に溶ける)
#   キラキラとヒント(Background_Light.png)は作らない(無ければプログラムは何も描かない)
from common import *
import numpy as np

L = 'Image/03.SongLoading/'
W, H = 1920, 1080
TOP = (12, 12, 14)        # 上の色(ほぼ黒)
BOTTOM = (48, 48, 54)     # 下の色(こい灰色)
EDGE = (96, 96, 104)      # 幕のすき間のふち

def gradient():
    t = np.linspace(0, 1, H, dtype=np.float32)[:, None, None]
    top = np.array(TOP, dtype=np.float32)[None, None, :]
    bottom = np.array(BOTTOM, dtype=np.float32)[None, None, :]
    rgb = np.broadcast_to(top + (bottom - top) * t, (H, W, 3))
    a = np.full((H, W, 1), 255, dtype=np.float32)
    return Image.fromarray(np.concatenate([rgb, a], axis=2).round().astype(np.uint8), 'RGBA')

def background():
    save_image(gradient(), L + 'Background.png')

def gap_half_width(y):
    """幕のすき間の半分の幅(上は細く、下へ行くほど広い)"""
    return 4 + 330 * (y / H) ** 2.2

def background_fade():
    # SS 倍で形を作って縮める(すき間のふちをなめらかにするため)
    big = (W * SS, H * SS)
    left = [(960 - gap_half_width(y), y) for y in range(0, H + 1, 4)]
    right = [(960 + gap_half_width(y), y) for y in range(H, -1, -4)]
    hole = Image.new('L', big, 255)
    ImageDraw.Draw(hole).polygon([(x * SS, y * SS) for x, y in left + right], fill=0)
    edge = Image.new('L', big, 0)
    d = ImageDraw.Draw(edge)
    for side in (left, right):
        d.line([(x * SS, y * SS) for x, y in side], fill=255, width=3 * SS)
    hole = hole.resize((W, H), Image.LANCZOS)
    edge = edge.resize((W, H), Image.LANCZOS)

    im = gradient()
    im.paste(Image.new('RGBA', (W, H), EDGE + (255,)), (0, 0), edge)     # ふちの線
    alpha = np.maximum(np.asarray(hole), np.asarray(edge))               # 線はすき間側にはみ出しても見えるように
    im.putalpha(Image.fromarray(alpha))
    save_image(im, L + 'Background_Fade.png')

def chara():
    c = Canvas(W, H)
    for cx, col in ((200, DON), (1720, KA)):
        c.circle(cx, 790, 116, fill=(255, 255, 255, 255))   # 白い細いふち
        c.circle(cx, 790, 110, fill=col + (255,))
    c.save(L + 'Chara.png')

def plate():
    w, h = 1600, 270
    x = np.arange(w, dtype=np.float32)
    k = np.clip(np.minimum(x, w - 1 - x) / 240, 0, 1)   # 両はしの溶ける幅 240px
    k = k * k * (3 - 2 * k)
    arr = np.zeros((h, w, 4), dtype=np.uint8)
    arr[..., :3] = (246, 244, 250)
    arr[..., 3] = (210 * k)[None, :].astype(np.uint8)
    for yy in list(range(6, 10)) + list(range(h - 10, h - 6)):   # 上と下に細い金の線
        arr[yy, :, :3] = ACCENT
        arr[yy, :, 3] = (255 * k).astype(np.uint8)
    save_image(Image.fromarray(arr), L + 'Plate.png')

def run():
    background(); background_fade(); chara(); plate()
    old = os.path.join(OUT, L, 'Background_Light.png')   # 前に作っていたキラキラとヒントは消す
    if os.path.exists(old):
        os.remove(old)

if __name__ == '__main__':
    run()
