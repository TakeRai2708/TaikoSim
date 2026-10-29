# 曲の読み込み画面の画像(Image/03.SongLoading/)。TaikøNauts と同じ名前・同じ大きさ・同じ使い方
#   Background.png       1920×1080 … 幕が閉じたときの背景
#   Background_Fade.png  1920×1080 … 幕(左半分・右半分に分けて両側から動かす。真ん中に、下が広い すき間)
#   Background_Light.png 1920×1080 … キラキラとヒントの文字(背景の上に重ねる)
#   Chara.png            1920×1080 … 左下と右下の太鼓(左半分・右半分に分けて動かす)
#   Plate.png            1600×270  … 曲名の板(両はしが透明に溶ける)
from common import *
import random
import numpy as np

L = 'Image/03.SongLoading/'
W, H = 1920, 1080

# 落ち着いた虹色のしま(8本)。Simple スキンの暗めの色に合わせて、色を弱く・少し暗く
def stripe_colors():
    return [hsv(0.30 - i * 0.11, 0.42, 0.62) for i in range(8)]

def stripes(c, cols, x0=0, x1=W):
    for i, col in enumerate(cols):
        a, b = max(x0, i * 240), min(x1, (i + 1) * 240)
        if a < b:
            c.rect(a, 0, b, H, fill=col)
    # うすい水玉(どの色の上でも見える、白 6%)。1枚の層にまとめて描いてから重ねる(1つずつ重ねると遅い)
    layer = Image.new('RGBA', c.im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for y in range(30, H, 60):
        for x in range(30 + (y // 60 % 2) * 30, W, 60):
            if x0 <= x < x1:
                d.ellipse([c.s(x - 9), c.s(y - 9), c.s(x + 9), c.s(y + 9)], fill=(255, 255, 255, 16))
    c.im.alpha_composite(layer)

def background():
    c = Canvas(W, H)
    stripes(c, stripe_colors())
    c.save(L + 'Background.png')

def gap_half_width(y):
    """幕のすき間の半分の幅(上は細く、下へ行くほど広い)"""
    t = y / H
    return 4 + 330 * t ** 2.2

def background_fade():
    c = Canvas(W, H)
    stripes(c, stripe_colors())
    # 真ん中のすき間をくりぬく(大きく描いてから、すき間の形で透明にする)
    im = c.im
    mask = Image.new('L', im.size, 255)
    d = ImageDraw.Draw(mask)
    pts_l = [(960 - gap_half_width(y), y) for y in range(0, H + 1, 4)]
    pts_r = [(960 + gap_half_width(y), y) for y in range(H, -1, -4)]
    d.polygon([(x * SS, y * SS) for x, y in pts_l + pts_r], fill=0)
    # すき間のふちに、少し暗い影(幕のひだ)
    shade = Image.new('RGBA', im.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    for side in (-1, 1):
        edge = [(960 + side * gap_half_width(y), y) for y in range(0, H + 1, 4)]
        sd.line([(x * SS, y * SS) for x, y in edge], fill=(0, 0, 0, 70), width=int(24 * SS))
    shade = shade.filter(ImageFilter.GaussianBlur(10 * SS))
    im.alpha_composite(shade)
    alpha = Image.fromarray(np.minimum(np.asarray(im.getchannel('A')), np.asarray(mask)))
    im.putalpha(alpha)
    c.save(L + 'Background_Fade.png')

def sparkle(c, x, y, r, a=255):
    """4本の光(細いひし形を2つ)と、まわりのにじみ"""
    c.circle(x, y, r * 0.55, fill=(255, 255, 230, a // 5))
    c.poly([(x, y - r), (x + r * 0.16, y), (x, y + r), (x - r * 0.16, y)], fill=(255, 255, 240, a))
    c.poly([(x - r, y), (x, y - r * 0.16), (x + r, y), (x, y + r * 0.16)], fill=(255, 255, 240, a))

def background_light():
    c = Canvas(W, H)
    rnd = random.Random(1029)
    for _ in range(26):
        x, y = rnd.uniform(40, W - 40), rnd.uniform(40, H - 40)
        if 200 < y < 600 and 200 < x < 1720:  # 曲名の板の場所はよける
            continue
        sparkle(c, x, y, rnd.uniform(10, 30), rnd.randint(150, 255))
    c.im = glow(c.im, 6 * SS)
    c.d = type(c.d)(c)  # 絵を入れ替えたので、描く道具も作り直す
    # ヒント(白い字に、暗い縁)
    lines = [('ゲームのヒント', 30), ('重いと感じたら', 28), ('設定の「デバッグ記録」で', 28), ('どこが重いか調べられるよ！', 28)]
    y = 735
    for txt, size in lines:
        c.text(960, y, txt, size, (255, 255, 255, 255), stroke=5, stroke_fill=INK, bold=True)
        y += 48 if size == 30 else 42
    c.save(L + 'Background_Light.png')

def drum(c, cx, cy, r, col, dark):
    c.circle(cx + 6, cy + 10, r + 8, fill=(0, 0, 0, 70))       # 影
    c.circle(cx, cy, r + 8, fill=INK + (255,))                  # 黒い縁
    c.circle(cx, cy, r, fill=RIM + (255,))                      # 白いふち
    c.circle(cx, cy, r * 0.78, fill=dark + (255,))
    c.circle(cx, cy - r * 0.03, r * 0.72, fill=col + (255,))    # 面
    c.circle(cx - r * 0.28, cy - r * 0.30, r * 0.16, fill=(255, 255, 255, 90))  # 光

def chara():
    c = Canvas(W, H)
    drum(c, 200, 790, 150, DON, DON_DARK)     # 左(ドン)
    drum(c, 1720, 790, 150, KA, KA_DARK)      # 右(カッ)
    c.text(200, 790, 'ドン', 70, (255, 255, 255, 255), stroke=6, stroke_fill=INK, bold=True)
    c.text(1720, 790, 'カッ', 70, (255, 255, 255, 255), stroke=6, stroke_fill=INK, bold=True)
    c.save(L + 'Chara.png')

def plate():
    w, h = 1600, 270
    im = Image.new('RGBA', (w, h), (246, 244, 250, 210))
    a = np.full((h, w), 210, dtype=np.float32)
    ramp = 240  # 両はしの溶ける幅
    x = np.arange(w, dtype=np.float32)
    k = np.clip(np.minimum(x, w - 1 - x) / ramp, 0, 1)
    a *= (k * k * (3 - 2 * k))[None, :]
    # 上と下に細い金の線
    arr = np.asarray(im).copy()
    arr[..., 3] = a.astype(np.uint8)
    for yy in list(range(6, 10)) + list(range(h - 10, h - 6)):
        arr[yy, :, :3] = ACCENT
        arr[yy, :, 3] = (255 * k * k * (3 - 2 * k)).astype(np.uint8)
    save_image(Image.fromarray(arr), L + 'Plate.png')

def run():
    background(); background_fade(); background_light(); chara(); plate()

if __name__ == '__main__':
    run()
