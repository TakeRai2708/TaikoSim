# 演奏画面の画像
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import *
import numpy as np

E = 'Image/04.EnsoGame/'
NC = (97.5, 95.5)  # 音符・判定枠の画像の中心(A-Style と同じ。判定の位置と合う)
JF = NC            # 判定枠：音符と同じ中心。本体は判定の瞬間、音符の画像と枠の画像を同じ左上に重ねる(11/11)

def note_disc(c, cx, cy, r, color, dark):
    # 平らな丸：外に細い黒、白い縁、中は1色
    c.circle(cx, cy, r, fill=INK)
    c.circle(cx, cy, r - 3, fill=RIM)
    c.circle(cx, cy, r - 7, fill=color)

# 風船の形。A-Style の Notes/Normal/0/10.png を 1px ずつ測った「まんなかから上下の高さ」
#   頭の丸(半径52) → 首の帯(平ら、高さ24) → くびれ(高さ20) → ふくらみ(最大42) → 先は丸い
#   x=140 側は頭の丸のうしろに隠れる。顔(目・口)は描かない
BALLOON_X = [140, 167, 171, 184, 190, 196, 202, 208, 214, 220, 226, 232, 238, 244,
             256, 262, 268, 274, 280, 286, 292, 295, 297]
BALLOON_H = [ 24,  24,  20,  20,  22,  24,  27,  29,  32,  35,  37,39.5,  41,  42,
              42,  40,  39,  36,  32,  27,  19,  12,   0]
BALLOON_BODY = (240, 96, 28)   # ふくらみ。頭(BALLOON)より少し濃いオレンジ
BALLOON_NECK = (146, 164.5)    # 首の帯の左右。右はしに細い黒を入れてふくらみと分ける

def balloon_body(c, cy, outline=4):
    """頭の丸のうしろから右へ伸びる風船。顔(目・口)は描かない"""
    xs = np.arange(BALLOON_X[0], BALLOON_X[-1] + 0.5, 0.5)
    hs = np.interp(xs, BALLOON_X, BALLOON_H)

    def shape(shrink, fill):
        # 内側は上下に shrink 縮めつつ、先っぽも shrink 手前で閉じる(先だけ輪郭が細くならないように)
        x0, x1 = BALLOON_X[0], BALLOON_X[-1]
        scale = (x1 - shrink - x0) / (x1 - x0)
        pts = [(x0 + (x - x0) * scale, max(0.0, h - shrink)) for x, h in zip(xs, hs)]
        c.poly([(x, cy - h) for x, h in pts] + [(x, cy + h) for x, h in reversed(pts)], fill=fill)

    shape(0, INK)
    shape(outline, BALLOON_BODY)
    # 首の帯は頭と同じ明るいオレンジ
    n0, n1 = BALLOON_NECK
    half = BALLOON_H[0] - outline
    c.rect(n0, cy - half, n1 - 2, cy + half, fill=BALLOON)
    c.rect(n1 - 2, cy - BALLOON_H[0], n1, cy + BALLOON_H[0], fill=INK)

def notes():
    for folder in ('0', '1'):
        d = E + 'Notes/Normal/' + folder + '/'
        for i, (color, dark, r) in enumerate([(DON, DON_DARK, 51.5), (KA, KA_DARK, 51.5), (DON, DON_DARK, 78.5), (KA, KA_DARK, 78.5)]):
            c = Canvas(195, 195); note_disc(c, *NC, r, color, dark)
            c.save(d + f'{i}.png')
        for head, body, end, r in [(4, 5, 6, 51.5), (7, 8, 9, 78.5)]:
            c = Canvas(195, 195); note_disc(c, *NC, r, ROLL, ROLL_DARK); c.save(d + f'{head}.png')
            c = Canvas(195, 195)
            c.rect(-2, NC[1] - r, 197, NC[1] + r, fill=INK)
            c.rect(-2, NC[1] - r + 3, 197, NC[1] + r - 3, fill=RIM)
            c.rect(-2, NC[1] - r + 7, 197, NC[1] + r - 7, fill=ROLL)
            c.save(d + f'{body}.png')
            c = Canvas(195, 195)
            c.d.pieslice([c.s(-r), c.s(NC[1] - r), c.s(r), c.s(NC[1] + r)], -90, 90, fill=INK)
            c.d.pieslice([c.s(-r + 3), c.s(NC[1] - r + 3), c.s(r - 3), c.s(NC[1] + r - 3)], -90, 90, fill=RIM)
            c.d.pieslice([c.s(-r + 7), c.s(NC[1] - r + 7), c.s(r - 7), c.s(NC[1] + r - 7)], -90, 90, fill=ROLL)
            c.rect(-2, NC[1] - r + 7, 1, NC[1] + r - 7, fill=ROLL)
            c.save(d + f'{end}.png')
        # 風船：A-Style と同じ形(首でくびれて、ふくらんで、先は丸い)。顔は描かない
        c = Canvas(390, 195)
        balloon_body(c, NC[1])
        note_disc(c, *NC, 51.5, BALLOON, (190, 80, 20))
        c.save(d + '10.png')

def judge_frame():
    c = Canvas(195, 195)
    c.circle(*JF, 78, outline=(160, 160, 170, 110), width=2)
    c.circle(*JF, 52, outline=(220, 220, 228, 200), width=3)
    c.save(E + 'Notes/JudgementFrame.png')

def se_notes():
    words = ['ドン', 'ド', 'コ', 'カ', 'カッ', 'ドン(大)', 'カッ(大)', '連打', 'っ!!', 'ふうせん', 'くすだま']
    c = Canvas(130, 440)
    for i, w in enumerate(words):
        size = 27 if len(w) <= 3 else 22
        c.text(65, i * 40 + 20, w, size, (255, 255, 255), stroke=3, stroke_fill=INK)
    c.save(E + 'SENotes.png')
    im = Image.new('RGBA', (1, 40), (0, 0, 0, 0))
    for y, col in [(13, (110, 110, 118, 255)), (14, (255, 255, 255, 255)), (15, (255, 255, 255, 255)), (16, (255, 255, 255, 255)),
                   (17, (255, 255, 255, 255)), (18, (255, 255, 255, 255)), (19, (255, 255, 255, 255)), (20, (255, 255, 255, 255)),
                   (21, (255, 255, 255, 255)), (22, (255, 255, 255, 255)), (23, (110, 110, 118, 255)), (24, (40, 40, 44, 230))]:
        im.putpixel((0, y), col)
    save_image(im, E + 'SENotes_Rolling.png')

def lanes():
    c = Canvas(1426, 264)
    c.rect(0, 0, 1426, 264, fill=(0, 0, 0))
    c.rect(0, 12, 1426, 206.6, fill=LANE)
    c.rect(0, 213, 1426, 251.6, fill=(112, 112, 122))
    c.save(E + 'Lane/Background_1P.png')
    def gradient(rel, color, a0, a1, color2=None):
        w, h = 1426, 219
        x = np.arange(w) / w
        alpha = np.clip(a0 + (a1 - a0) * x, 0, 255)
        arr = np.zeros((h, w, 4), np.uint8)
        col = np.array(color, float)[None, :] * (1 - x[:, None]) + np.array(color2 or color, float)[None, :] * x[:, None]
        arr[12:207, :, :3] = col.astype(np.uint8)[None, :, :]
        arr[12:207, :, 3] = alpha.astype(np.uint8)[None, :]
        save_image(Image.fromarray(arr, 'RGBA'), rel)
    gradient(E + 'Lane/Red.png', (255, 40, 30), 255, 40)
    gradient(E + 'Lane/Blue.png', (40, 170, 255), 255, 40)
    gradient(E + 'Lane/Yellow.png', (255, 220, 40), 255, 40)
    gradient(E + 'Lane/GoGo.png', (110, 20, 60), 248, 248, (220, 80, 150))
    gradient(E + 'Lane/Branch_Normal.png', (40, 40, 44), 0, 0)
    gradient(E + 'Lane/Branch_Expert.png', (40, 130, 200), 253, 253)
    gradient(E + 'Lane/Branch_Master.png', (200, 50, 175), 253, 253)
    for name, txt, col in [('Normal', '普通譜面', (235, 235, 240)), ('Expert', '玄人譜面', (140, 220, 255)), ('Master', '達人譜面', (255, 160, 230))]:
        c = Canvas(1426, 195)
        c.text(1249.5, 95, txt, 62, col + (220,), stroke=4, stroke_fill=(20, 20, 26, 200))
        c.save(E + f'Lane/Branch_{name}_Text.png')

def panel():
    c = Canvas(499, 264)
    c.rect(0, 0, 499, 264, fill=(0, 0, 0))
    for y in range(12, 252):
        t = (y - 12) / 240
        c.rect(0, y, 499, y + 1, fill=lerp((150, 38, 48), (96, 24, 36), t))
    c.rect(0, 252, 499, 264, fill=(0, 0, 0))
    c.save(E + 'MiniTaiko/Background_1P.png')
    c = Canvas(499, 264)
    c.rect(0, 8, 303, 63, fill=(0, 0, 0, 127), radius=12)
    c.save(E + 'MiniTaiko/Background_Score.png')

def mini_taiko():
    c = Canvas(180, 1000)
    # 0：太鼓
    # 胴：パネルの赤に溶けないように、明るいオレンジ＋黒い輪郭
    body, outline = (255, 150, 45), INK
    c.d.ellipse([c.s(12), c.s(68), c.s(168), c.s(192)], fill=outline)
    c.rect(12, 84, 168, 132, fill=outline)
    c.d.ellipse([c.s(15), c.s(71), c.s(165), c.s(189)], fill=body)
    c.rect(15, 84, 165, 132, fill=body)
    c.d.ellipse([c.s(10), c.s(7), c.s(169), c.s(161)], fill=INK)
    c.d.ellipse([c.s(16), c.s(13), c.s(163), c.s(155)], fill=(200, 160, 110))
    c.d.ellipse([c.s(29.5), c.s(22), c.s(149.5), c.s(127)], fill=(246, 238, 220))
    c.line([(89.5, 26), (89.5, 123)], (190, 180, 160), 2)
    # 1・2：ドン(右・左の半分)
    c.d.pieslice([c.s(29.5), c.s(22 + 200), c.s(149.5), c.s(127 + 200)], -90, 90, fill=DON + (230,))
    c.d.pieslice([c.s(29.5), c.s(22 + 400), c.s(149.5), c.s(127 + 400)], 90, 270, fill=DON + (230,))
    # 3・4：カッ(右・左のふち)
    for row, a0, a1 in [(3, -90, 90), (4, 90, 270)]:
        c.d.arc([c.s(12), c.s(9 + row * 200), c.s(167), c.s(159 + row * 200)], a0, a1, fill=KA + (240,), width=c.s(12))
    c.save(E + 'MiniTaiko/MiniTaiko.png')

def numbers():
    fit_digits(E + 'MiniTaiko/Combo_Number.png', 66, 78, [((255, 255, 255), INK, 5), ((210, 220, 235), INK, 5), ((255, 205, 80), INK, 5)], 84)
    c = Canvas(134, 47); c.text(60, 28, 'コンボ', 28, (255, 255, 255), stroke=4); c.save(E + 'MiniTaiko/Combo_MiniText.png')
    fit_digits(E + 'MiniTaiko/Score.png', 39, 50, [((255, 255, 255), INK, 3), ((255, 150, 60), INK, 3), ((110, 220, 240), INK, 3)], 52)
    fit_digits(E + 'Popup/Roll_Number.png', 90, 108, [((255, 255, 255), INK, 6)], 118)
    fit_digits(E + 'Popup/Balloon_Number.png', 72, 86, [((255, 255, 255), INK, 5)], 94)
    fit_digits(E + 'Scoreboard/Number.png', 20, 23, [((255, 255, 255), INK, 2)], 25, margin=1, bottom=1)

def course_symbols():
    items = [('かんたん', (240, 120, 60)), ('ふつう', (110, 190, 80)), ('むずかしい', (90, 150, 200)), ('おに', (200, 60, 150)), ('おに裏', (130, 70, 200))]
    c = Canvas(133, 450)
    for i, (name, col) in enumerate(items):
        y = i * 90
        c.circle(66.5, y + 36, 26, fill=col)
        c.text(66.5, y + 76, name, 17 if len(name) < 5 else 14, (255, 255, 255), stroke=3)
    c.save(E + 'MiniTaiko/CourseSymbol.png')

def judge_text():
    c = Canvas(150, 450)
    c.text(75, 76, '良', 66, (255, 200, 40), stroke=5)
    c.text(75, 226, '可', 66, (255, 255, 255), stroke=5)
    c.text(74, 376, '不可', 58, (110, 140, 255), stroke=5)
    c.save(E + 'Judge.png')

def hit_effects():
    c = Canvas(810, 195)
    for cx, cy, r, col in [(100.5, 96.5, 62, (255, 214, 60)), (295.5, 96.5, 62, (235, 235, 240)), (490, 95, 88, (255, 214, 60)), (685, 95, 86, (235, 235, 240))]:
        c.circle(cx, cy, r, outline=col + (255,), width=5)
        c.circle(cx, cy, r - 8, fill=col + (60,))
    im = glow(c.image(), 4)
    save_image(im, E + 'Effects/Hit_Effects.png')

def hit_explosion():
    W = 390
    c = Canvas(W * 5, W * 4, (0, 0, 0, 255))
    for row in range(4):
        good = row in (0, 2); big = row >= 2
        for f in range(5):
            cx, cy = f * W + W / 2, row * W + W / 2 - 2
            t = f / 4
            R = (100 if not big else 122) + 40 * t
            k = 1 - t * 0.75
            base = (255, 210, 70) if good else (160, 175, 185)
            col = tuple(int(v * k) for v in base)
            c.circle(cx, cy, R, outline=col, width=(7 if big else 6) * (1 - t * 0.5))
    c.save(E + 'Effects/Hit_Explosion.png')
    c = Canvas(390, 780)
    for row, col in [(0, (255, 235, 70)), (1, (150, 170, 180))]:
        cx, cy = 195, row * 390 + 193
        c.circle(cx, cy, 150, outline=col, width=5)
    c.save(E + 'Effects/Hit_Explosion_Big.png')
    c = Canvas(390, 195)
    c.circle(97.5, 95.5, 51.5, fill=(255, 255, 255)); c.circle(292.5, 95.5, 78.5, fill=(255, 255, 255))
    c.save(E + 'Effects/Note_Flush.png')

def soul_explosion():
    c = Canvas(448, 224)
    for tile, n, R, r in [(0, 8, 56, 18), (1, 12, 89, 21)]:
        cx = tile * 224 + 111.5
        for k in range(n):
            a = -math.pi / 2 + 2 * math.pi * k / n
            c.circle(cx + math.cos(a) * R, 111.5 + math.sin(a) * R, r, fill=(250, 250, 250))
    c.save(E + 'Effects/Soul_Explosion.png')

def fireworks():
    rnd = random.Random(7)
    parts = [(rnd.uniform(0, 2 * math.pi), rnd.uniform(0.3, 1.0), rnd.choice([(255, 220, 120), (255, 255, 255)])) for _ in range(24)]
    c = Canvas(256 * 13, 256, (0, 0, 0, 255))
    for f in range(13):
        t = (f + 1) / 13
        for a, sp, col in parts:
            d = 115 * sp * (1 - (1 - t) ** 2)
            x, y = f * 256 + 128 + math.cos(a) * d, 128 + math.sin(a) * d + 12 * t * t
            k = max(0.0, 1 - t * 0.9)
            c.circle(x, y, 3.2 * (1 - t * 0.5), fill=tuple(int(v * k) for v in col))
    c.save(E + 'Effects/FireWorks.png')

def gogo_splash():
    rnd = random.Random(11)
    W, H = 280, 544
    c = Canvas(W * 10, H * 3, (0, 0, 0, 255))
    parts = [(rnd.gauss(0, 18), rnd.uniform(0.4, 1.0), rnd.uniform(-1, 1)) for _ in range(50)]
    for i in range(30):
        col, row = i % 10, i // 10
        t = i / 29
        for dx, sp, sway in parts:
            h = 470 * sp * min(1, t * 2.2)
            y = row * H + H - 10 - h + max(0, t - 0.5) * 120 * sp
            x = col * W + W / 2 + dx * (1 + t * 2.5) + sway * 30 * t
            k = max(0.0, 1 - max(0, t - 0.45) * 1.8)
            c.circle(x, y, 2.6, fill=(int(255 * k), int(235 * k), int(170 * k)))
    c.save(E + 'Effects/GoGoSplash.png')

def recolor_mask(src, rel, colorfn):
    """A-Style の画像からは「形(どこが不透明か)」だけを使って、色はこっちで塗る。
       ゲージの棒のような、位置の計算がプログラムと結び付いている画像だけに使う"""
    a = np.array(Image.open(src).convert('RGBA')).astype(float)
    out = colorfn(a)
    save_image(Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA'), rel)

# ゲージの棒だけは、A-Style(TaikøNauts のスキン)の画像の「形」を使って色を塗り直す(位置の計算をプログラムと合わせるため)
#   A-Style の 04.EnsoGame フォルダの場所を環境変数 TAIKOSIM_ASTYLE_ENSO で渡す。無ければゲージは作らない(今の画像のまま)
AS = os.environ.get('TAIKOSIM_ASTYLE_ENSO', '').rstrip('/\\') + '/'

def gauge():
    if not os.environ.get('TAIKOSIM_ASTYLE_ENSO') or not os.path.isdir(AS):
        print('[gauge] A-Style の場所(TAIKOSIM_ASTYLE_ENSO)が無いので、ゲージは作りません')
        return
    def base(a):
        out = a.copy()
        rgb = a[:, :, :3]; al = a[:, :, 3]
        lum = rgb.mean(axis=2)
        red = (rgb[:, :, 0] > 60) & (rgb[:, :, 1] < 40)          # 1P のノルマ前
        teal = (rgb[:, :, 1] > 50) & (rgb[:, :, 0] < 60)         # 2P のノルマ前
        olive = (rgb[:, :, 0] > 60) & (rgb[:, :, 1] > 40) & (rgb[:, :, 2] < 40)  # ノルマ後
        out[:, :, :3] = 22
        out[red] = [90, 26, 40, 0]; out[teal] = [24, 70, 84, 0]; out[olive] = [96, 80, 28, 0]
        out[:, :, 3] = al
        return out
    recolor_mask(AS + 'Gauge/Gauge_Base.png', E + 'Gauge/Gauge_Base.png', base)
    def light(a):
        out = a.copy(); rgb = a[:, :, :3]
        r = (rgb[:, :, 0] > 150) & (rgb[:, :, 1] < 120)
        cy = (rgb[:, :, 2] > 150) & (rgb[:, :, 0] < 120)
        y = (rgb[:, :, 0] > 150) & (rgb[:, :, 1] > 150)
        out[r, :3] = [250, 70, 60]; out[cy, :3] = [70, 200, 240]; out[y, :3] = [255, 214, 60]
        return out
    recolor_mask(AS + 'Gauge/Gauge_Light.png', E + 'Gauge/Gauge_Light.png', light)
    recolor_mask(AS + 'Gauge/Gauge_Light_Glow.png', E + 'Gauge/Gauge_Light_Glow.png', light)
    def rainbow(a):
        out = a.copy(); h, w = a.shape[:2]
        fh = h / 24
        ys, xs = np.mgrid[0:h, 0:w]
        frame = (ys // fh) % 8
        hue = (xs / w * 0.9 + frame / 8.0) % 1.0
        import colorsys
        lut = np.array([colorsys.hsv_to_rgb(i / 256, 0.55, 1.0) for i in range(256)]) * 255
        col = lut[(hue * 255).astype(int)]
        dark = a[:, :, :3].mean(axis=2) < 40
        # A-Style の虹は、マスの下のふち（1コマの72行目）に暗い影がところどころある。
        # それを黒い帯とまちがえて塗ると、マスの下にギザギザが出る（11/5 たけらいの動画）。
        # 72行目は「1つ上も暗いとき」だけ黒にする（上が色なら上の色につなげる）
        for f in range(24):
            y = int(round(f * fh)) + 72
            if 0 < y < h:
                dark[y] &= dark[y - 1]
        out[:, :, :3] = col
        out[dark, :3] = 22
        return out
    recolor_mask(AS + 'Gauge/Rainbow.png', E + 'Gauge/Rainbow.png', rainbow)
    # 魂(8コマ)：光る玉のまわりを小さな星が回る
    c = Canvas(212 * 8, 224)
    for f in range(8):
        cx, cy = f * 212 + 100, 140
        col = [(255, 140, 60), (255, 200, 60), (170, 200, 255), (255, 140, 60), (255, 150, 220), (255, 140, 60), (255, 200, 60), (255, 140, 60)][f]
        c.circle(cx, cy, 52 + (f % 2) * 4, fill=col + (70,)); c.circle(cx, cy, 42, fill=col)
    save_image(glow(c.image(), 5), E + 'Gauge/Soul_Anime.png')
    c = Canvas(85, 255)
    for i, (col, st) in enumerate([((120, 120, 128), INK), ((255, 255, 255), INK), ((255, 245, 180), (120, 90, 20))]):
        c.text(42.5, i * 85 + 44, '魂', 66, col, stroke=5, stroke_fill=st)
    save_image(glow(c.image(), 2), E + 'Gauge/Soul_Text.png')
    c = Canvas(90, 80)
    c.text(47, 20, 'クリア', 24, (150, 150, 158), stroke=3)
    c.text(47, 60, 'クリア', 24, (255, 255, 255), stroke=3)
    c.save(E + 'Gauge/Gauge_Clear_Text.png')

def popups():
    # 連打の扇(5コマ。閉じた形 → 開く)
    c = Canvas(380, 1200)
    spans = [4, 30, 55, 80, 78]
    for f, span in enumerate(spans):
        px, py = 193.5, f * 240 + 214
        R = 190
        a0, a1 = -90 - span, -90 + span
        c.d.pieslice([c.s(px - R), c.s(py - R), c.s(px + R), c.s(py + R)], a0, a1, fill=INK)
        c.d.pieslice([c.s(px - R + 6), c.s(py - R + 6), c.s(px + R - 6), c.s(py + R - 6)], a0 + 1, a1 - 1, fill=(250, 200, 60))

    c.save(E + 'Popup/Roll_Base.png')
    c = Canvas(89, 35); c.text(44.5, 18, '連打!!', 26, (255, 255, 255), stroke=3); c.save(E + 'Popup/Roll_Text.png')
    c = Canvas(284, 253)
    c.d.ellipse([c.s(18), c.s(20), c.s(268), c.s(200)], fill=INK)
    c.poly([(80, 180), (66, 242), (130, 194)], fill=INK)
    c.d.ellipse([c.s(24), c.s(26), c.s(262), c.s(194)], fill=(255, 252, 235))
    c.poly([(86, 182), (74, 230), (126, 192)], fill=(255, 252, 235))
    c.save(E + 'Popup/Balloon_Base.png')
    c = Canvas(390, 2880)
    for f in range(8):
        y0 = f * 360
        if f < 7:
            r = [28, 42, 60, 80, 120, 140, 166][f]
            cx, cy = 18 + r, y0 + 180 + (60 if f < 3 else 0)
            c.circle(cx, cy, r + 4, fill=INK)
            c.circle(cx, cy, r, fill=lerp(BALLOON, (255, 240, 170), f / 6))
            c.rect(6, cy - 8, 20, cy + 8, fill=BALLOON)
        else:
            cx, cy = 186, y0 + 180
            for k in range(14):
                a = 2 * math.pi * k / 14
                c.line([(cx + math.cos(a) * 130, cy + math.sin(a) * 130), (cx + math.cos(a) * 160, cy + math.sin(a) * 160)], (255, 255, 255), 6)
    c.save(E + 'Popup/Balloon_Anime.png')

def branch_popup():
    c = Canvas(308, 280)
    for row, txt, col in [(0, 'レベルアップ', (245, 140, 40)), (1, 'レベルダウン', (40, 130, 160))]:
        y = row * 140 + (20 if row == 0 else 42)
        c.rect(10, y, 250, y + 80, fill=INK, radius=40)
        c.rect(15, y + 5, 245, y + 75, fill=col, radius=35)
        ay = y - 8 if row == 0 else y + 88
        tip = y - 8 if row == 0 else y + 92
        base = y + 40
        c.poly([(255, base), (300, base), (277.5, tip)], fill=INK)
        c.poly([(260, base - (4 if row == 0 else -4)), (295, base - (4 if row == 0 else -4)), (277.5, tip + (8 if row == 0 else -8))], fill=col)
        c.text(130, y + 40, txt, 34, (255, 255, 255), stroke=4)
    c.save(E + 'MiniTaiko/Branch_Popup.png')

def scoreboard():
    c = Canvas(244, 320)
    for row, col in [(0, (170, 50, 50)), (1, (40, 120, 140))]:
        y = row * 160
        c.rect(17, y + 15, 219, y + 139, fill=(24, 24, 30), radius=10)
        c.rect(17, y + 15, 23, y + 139, fill=col)
        for i, label in enumerate(['良', '可', '不可', '連打数']):
            ly = y + 23 + i * 25.7 + 11.5
            c.text(77, ly, label, 16, [(255, 205, 60), (255, 255, 255), (130, 160, 255), (255, 205, 60)][i], stroke=2)
    c.save(E + 'Scoreboard/Base.png')

RANKS = [('E', (150, 150, 160)), ('D', (120, 170, 210)), ('C', (110, 200, 140)), ('B', (240, 200, 70)),
         ('A', (255, 150, 60)), ('S', (255, 110, 150)), ('SS', (190, 130, 255))]

def rank_letter(c, cx, cy, i, size):
    ch, col = RANKS[i]
    c.text(cx, cy, ch, size * (0.78 if len(ch) == 2 else 1), col, stroke=size * 0.08, stroke_fill=INK, bold=False)

def rank_symbols():
    # 0 が一番下(50万) → 6 が一番上(満点)
    c = Canvas(1400, 200)
    for i in range(7):
        rank_letter(c, i * 200 + 100, 100, i, 130)
    c.save(E + 'ScoreRankSymbol.png')

def backgrounds():
    import os
    root = OUT + '/' + E + 'Background/'
    def aup2(path, img, w, h, fade=False):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if fade:
            # 0〜59 コマ(1秒)で 透明度 100→0、60〜119 コマはそのまま(Config.json の bgInEndFrame=60 から繰り返す)
            obj = lambda i, f0, f1, tr: f'''[{i}]
layer=0
frame={f0},{f1}
[{i}.0]
effect.name=画像ファイル
ファイル={img}
[{i}.1]
effect.name=標準描画
X=0.00
Y=0.00
Z=0.00
拡大率=100.000
透明度={tr}
合成モード=通常
'''
            with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
                f.write(f'''[project]
version=2002100
[scene.0]
scene=0
name=Root
video.width={w}
video.height={h}
video.rate=60
video.scale=1
''' + obj(0, 0, 59, '100.00,0.00,直線移動,0') + obj(1, 60, 119, '0.00'))
            return
        with open(path, 'w', encoding='utf-8', newline='\r\n') as f:
            f.write(f'''[project]
version=2002100
[scene.0]
scene=0
name=Root
video.width={w}
video.height={h}
video.rate=60
video.scale=1
[0]
layer=0
frame=0,0
[0.0]
effect.name=画像ファイル
ファイル={img}
[0.1]
effect.name=標準描画
X=0.00
Y=0.00
Z=0.00
拡大率=100.000
透明度=0.00
合成モード=通常
''')
    def config(path, loop=None):
        with open(path, 'w', encoding='utf-8') as f:
            f.write('{\n  "songGenreID": "default"' + (f',\n  "bgInEndFrame": {loop}' if loop else '') + '\n}')
    def sky(w, h, top, bottom, seed, dots=True):
        c = Canvas(w, h)
        for y in range(h):
            c.rect(0, y, w, y + 1, fill=lerp(top, bottom, y / h))
        return c
    for kind, top, bottom in [('Normal_1P', (60, 40, 90), (110, 50, 80))]:
        d = root + f'Upper/0/{kind}/0/'
        sky(1920, 276, top, bottom, 5).save(E + f'Background/Upper/0/{kind}/0/bg.png')
        aup2(d + 'Anime.aup2', 'bg.png', 1920, 276)
    config(root + 'Upper/0/Config.json')
    for kind, top, bottom, seed in [('Normal/0', (34, 30, 48), (54, 40, 70), 9), ('Clear/0', (90, 76, 130), (130, 96, 150), 13)]:
        d = root + f'Lower/{kind}/0/'
        sky(1920, 540, top, bottom, seed).save(E + f'Background/Lower/{kind}/0/bg.png')
        aup2(d + 'Anime.aup2', 'bg.png', 1920, 540, fade=kind.startswith('Clear'))
        config(root + f'Lower/{kind}/Config.json', 60 if kind.startswith('Clear') else None)

def run():
    notes(); judge_frame(); se_notes(); lanes(); panel(); mini_taiko(); numbers(); course_symbols(); judge_text()
    hit_effects(); hit_explosion(); soul_explosion(); fireworks(); gogo_splash(); gauge(); popups(); branch_popup()
    scoreboard(); rank_symbols(); backgrounds()

if __name__ == '__main__':
    run()
