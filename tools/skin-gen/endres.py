# 演奏後の演出・リザルト画面の画像
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import *
import numpy as np
E = 'Image/04.EnsoGame/End/'
R = 'Image/05.Result/'

# ===== オリジナルのキャラクター(ポンとピコ) =====
PON = (150, 100, 220)   # 左：むらさき
PIKO = (80, 190, 130)   # 右：みどり

def chara(c, ox, oy, color, pose, mirror, kimono=False):
    """ox,oy はコマ(200×250)の左上。体はカプセル形。pose で腕の形を変える"""
    def X(x): return ox + (200 - x if mirror else x)
    def Y(y): return oy + y
    bx0, bx1, by0, by1 = 66, 134, 52, 212
    # 足
    for fx in (82, 118):
        c.d.ellipse([c.s(X(fx) - 15), c.s(Y(206)), c.s(X(fx) + 15), c.s(Y(230))], fill=INK)
        c.d.ellipse([c.s(X(fx) - 12), c.s(Y(209)), c.s(X(fx) + 12), c.s(Y(227))], fill=lerp(color, (0, 0, 0), 0.25))
    # 腕(肩 → 手)。pose：0 下ろす、1 横に広げる、2 両手を上、3 片手を上、4 手をふる
    arms = {0: [((70, 120), (48, 168)), ((130, 120), (152, 168))],
            1: [((70, 110), (26, 112)), ((130, 110), (174, 112))],
            2: [((70, 100), (40, 34)), ((130, 100), (160, 34))],
            3: [((70, 110), (34, 140)), ((130, 100), (160, 34))],
            4: [((70, 105), (30, 70)), ((130, 105), (170, 70))]}[pose]
    for (sx, sy), (hx, hy) in arms:
        c.line([(X(sx), Y(sy)), (X(hx), Y(hy))], INK, 13)
        c.line([(X(sx), Y(sy)), (X(hx), Y(hy))], color, 7)
        c.circle(X(hx), Y(hy), 11, fill=INK); c.circle(X(hx), Y(hy), 8, fill=(255, 245, 230))
    # 体
    c.rect(min(X(bx0), X(bx1)) - 5, Y(by0) - 5, max(X(bx0), X(bx1)) + 5, Y(by1) + 5, fill=INK, radius=39)
    c.rect(min(X(bx0), X(bx1)), Y(by0), max(X(bx0), X(bx1)), Y(by1), fill=color, radius=34)
    c.rect(min(X(bx0), X(bx1)) + 10, Y(by0) + 60, max(X(bx0), X(bx1)) - 10, Y(by1) - 10, fill=(255, 255, 255, 60), radius=24)
    if kimono:  # ドンダフルコンボのときの晴れ着：金の帯と白い衿
        c.poly([(X(68), Y(96)), (X(100), Y(130)), (X(132), Y(96)), (X(132), Y(108)), (X(100), Y(142)), (X(68), Y(108))], fill=(250, 250, 250))
        c.rect(min(X(66), X(134)), Y(146), max(X(66), X(134)), Y(166), fill=(240, 190, 40))
        c.rect(min(X(66), X(134)), Y(146), max(X(66), X(134)), Y(149), fill=INK)
        c.rect(min(X(66), X(134)), Y(163), max(X(66), X(134)), Y(166), fill=INK)
    # 頭のアンテナ(星)
    c.line([(X(100), Y(52)), (X(108), Y(26))], INK, 4)
    sx, sy, k = X(108), Y(20), 11
    c.poly([(sx + math.cos(math.radians(-90 + i * 36)) * (k if i % 2 == 0 else k * 0.45),
             sy + math.sin(math.radians(-90 + i * 36)) * (k if i % 2 == 0 else k * 0.45)) for i in range(10)], fill=(255, 220, 60))
    # 顔
    for ex in (88, 112):
        c.circle(X(ex), Y(84), 6, fill=INK)
        c.circle(X(ex) - 2, Y(82), 2, fill=(255, 255, 255))
    c.d.arc([c.s(X(92) if not mirror else X(108)), c.s(Y(90)), c.s(X(108) if not mirror else X(92)), c.s(Y(104))], 20, 160, fill=INK, width=c.s(3))
    c.circle(X(80), Y(98), 6, fill=(255, 140, 160, 120)); c.circle(X(120), Y(98), 6, fill=(255, 140, 160, 120))

def chara_sheet():
    # キャラクターは無し。本体が読み込めるように、同じ大きさの透明な画像だけ置く
    save_image(Image.new('RGBA', (1200, 750), (0, 0, 0, 0)), E + 'Chara.png')

def end_texts():
    c = Canvas(800, 300)
    c.text(398.5, 75, 'FAILED', 120, (130, 170, 230), stroke=8, stroke_fill=(16, 22, 40))
    c.text(398.5, 225, 'CLEAR', 120, (255, 190, 90), stroke=8, stroke_fill=(50, 24, 10))
    c.save(E + 'Text.png')
    for rel, w, txt, size, col in [(E + 'FullCombo_Text.png', 800, 'FULL COMBO', 112, (255, 210, 60)),
                                   (E + 'DondaFullCombo_Text.png', 1000, 'ALL PERFECT', 116, None)]:
        c = Canvas(w, 400)
        # 上の段：色。ドンダは虹色
        c.text(w / 2, 100, txt, size, (255, 255, 255), stroke=9, stroke_fill=INK)
        top = c.image()
        arr = np.array(top).astype(float)
        mask = (arr[:, :, :3].mean(axis=2) > 200) & (arr[:, :, 3] > 0)
        xs = np.arange(w)[None, :].repeat(400, 0)
        if col is None:
            import colorsys
            lut = np.array([colorsys.hsv_to_rgb(0.55 + i / 256 * 0.35, 0.35, 1.0) for i in range(256)]) * 255
            arr[mask, :3] = lut[((xs / w) * 255).astype(int)][mask]
        else:
            ys = np.arange(400)[:, None].repeat(w, 1)
            t = np.clip((ys - 40) / 120, 0, 1)
            arr[mask, 0] = (col[0] * (1 - t) + 255 * t)[mask]
            arr[mask, 1] = (col[1] * (1 - t) + 140 * t)[mask]
            arr[mask, 2] = (col[2] * (1 - t) + 30 * t)[mask]
        img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), 'RGBA')
        # 下の段：真っ白(形は上と同じ)
        white = np.array(img).copy(); white[:, :, :3] = 255
        white = np.roll(white, 200, axis=0); white[:200] = 0
        full = Image.alpha_composite(img, Image.fromarray(white, 'RGBA'))
        save_image(full, rel)
    # ドンダの背景(金の帯)
    c = Canvas(1422, 240)
    c.rect(0, 0, 1422, 240, fill=(20, 18, 30, 210))
    c.rect(0, 10, 1422, 13, fill=(230, 190, 90))
    c.rect(0, 227, 1422, 230, fill=(230, 190, 90))
    c.save(E + 'DondaFullCombo_Background.png')

def fans():
    c = Canvas(700, 1320)
    for f in range(6):
        span = [3, 14, 28, 42, 56, 66][f]
        for side in (0, 1):
            px, py = side * 350 + 175, f * 220 + 206
            R = 190
            a0, a1 = -90 - span, -90 + span
            c.d.pieslice([c.s(px - R), c.s(py - R), c.s(px + R), c.s(py + R)], a0, a1, fill=INK)
            c.d.pieslice([c.s(px - R + 5), c.s(py - R + 5), c.s(px + R - 5), c.s(py + R - 5)], a0 + 1, a1 - 1,
                         fill=(150, 60, 70) if side == 0 else (60, 90, 150))
            c.d.arc([c.s(px - R + 16), c.s(py - R + 16), c.s(px + R - 16), c.s(py + R - 16)], a0 + 2, a1 - 2, fill=(230, 190, 90), width=c.s(3))
    c.save(E + 'Fan.png')

def confetti():
    # 紙ふぶき(520×368 のコマが 横10・縦5 で 50コマ)
    #   コマの順番は「縦に読む」：0〜4 コマ目が1列目の上から下、5〜9 コマ目が2列目…(TaikøNauts の Confetti.png と同じ並び)
    #   (10/29 に A-Style の画像を調べて分かった。横に読むと、となりのコマと形がまったくつながらない)
    #   動き：コマの (190, 90) あたりから、上と横へはじけて、ひらひら回りながら落ちる。50コマ(30fpsで1.67秒)でつながってくり返す
    #   プログラムは扇と文字の「後ろ」に描くので、扇より上に飛んだ紙だけが見える(TaikøNauts と同じ)
    rnd = random.Random(21)
    W, H = 520, 368
    cols = [(230, 190, 90), (240, 240, 245), (200, 170, 230), (130, 200, 235), (240, 150, 180)]
    pieces = []
    for i in range(30):
        pieces.append(dict(birth=i * 50 / 30 + rnd.uniform(0, 1.5), vx=rnd.uniform(-2.6, 3.4), vy=rnd.uniform(-7.5, -3.5),
                           ph=rnd.uniform(0, 2 * math.pi), spin=rnd.uniform(0.25, 0.5), sw=rnd.uniform(3, 7),
                           size=rnd.uniform(7, 11), col=rnd.choice(cols)))
    G = 0.36       # 1コマごとに下へ速くなる量(px)
    DRAG = 0.965   # 横の速さは少しずつ落ちる
    c = Canvas(W * 10, H * 5)
    for f in range(50):
        ox, oy = (f // 5) * W, (f % 5) * H   # 縦に読む
        for p in pieces:
            age = (f - p['birth']) % 50
            vx = p['vx'] * (1 - DRAG ** age) / (1 - DRAG)
            xx = 190 + vx + math.sin(age * 0.35 + p['ph']) * p['sw']
            yy = 90 + p['vy'] * age + 0.5 * G * age * age
            if yy > H - 20 or not (10 < xx < W - 10):
                continue
            ang = p['ph'] + age * p['spin']   # くるくる回る(横幅が変わって見える)
            sz = p['size']
            w2 = sz * abs(math.cos(ang)) + 1.2
            h2 = sz * 0.6
            tilt = p['ph'] + age * 0.08
            pts = []
            for dx, dy in ((-w2, -h2), (w2, -h2), (w2, h2), (-w2, h2)):
                rx = dx * math.cos(tilt) - dy * math.sin(tilt)
                ry = dx * math.sin(tilt) + dy * math.cos(tilt)
                pts.append((ox + xx + rx, oy + yy + ry))
            c.poly(pts, fill=p['col'])
    c.save(E + 'Confetti.png')

def star():
    c = Canvas(64, 64)
    pts = []
    for i in range(8):
        r = 29 if i % 2 == 0 else 7
        a = math.radians(-90 + i * 45)
        pts.append((32 + math.cos(a) * r, 32 + math.sin(a) * r))
    c.poly(pts, fill=(255, 255, 220))
    save_image(glow(c.image(), 2), E + 'Star.png')

# ===== リザルト =====
def result_backgrounds():
    def bg(top, bottom, accent, seed):
        c = Canvas(1920, 1080)
        for y in range(1080):
            c.rect(0, y, 1920, y + 1, fill=lerp(top, bottom, y / 1080))
        return c.image()
    top = bg((70, 40, 90), (140, 60, 80), (255, 255, 255, 24), 1)
    bottom = bg((30, 70, 100), (40, 120, 140), (255, 255, 255, 24), 2)
    full = Image.new('RGBA', (1920, 2160)); full.paste(top, (0, 0)); full.paste(bottom, (0, 1080))
    save_image(full, R + 'Background.png')
    save_image(bg((150, 110, 200), (230, 140, 170), None, 3), R + 'Background_Clear.png')
    c = Canvas(1920, 172)
    c.rect(0, 0, 1920, 160, fill=(24, 22, 32, 230))
    c.rect(0, 160, 1920, 163, fill=(230, 190, 90))
    c.save(R + 'Header.png')

def result_panel():
    c = Canvas(1920, 1080)
    for side in (0, 1):
        ox = 0 if side == 0 else 960
        col = (215, 70, 60) if side == 0 else (40, 170, 200)
        c.rect(ox + 40, 209, ox + 920, 1018, fill=INK, radius=26)
        c.rect(ox + 60, 270, ox + 905, 975, fill=(250, 244, 244), radius=14)
        # スコアの箱
        c.rect(ox + 75, 284, ox + 497, 412, fill=col, radius=14)
        c.rect(ox + 75, 284, ox + 497, 314, fill=lerp(col, (0, 0, 0), 0.25), radius=14)
        c.text(ox + 286, 299, 'スコア', 19, (255, 255, 255), stroke=3)
        # 良・可・不可・連打数・最大コンボ数
        labels = [('良', (255, 200, 40)), ('可', (255, 255, 255)), ('不可', (110, 140, 255)), ('連打数', (255, 200, 40)), ('最大コンボ数', (255, 150, 40))]
        for i, (txt, tc) in enumerate(labels):
            y = 282 + i * 62
            c.rect(ox + 515, y, ox + 887, y + 60, fill=(225, 225, 230) if i % 2 == 0 else (245, 242, 245))
            c.text(ox + 528, y + 31, txt, 26, tc, stroke=3, anchor='lm')
        # 下の大きな箱
        c.rect(ox + 60, 605, ox + 905, 975, fill=col)
    c.save(R + 'Panel.png')

def result_numbers():
    fit_digits(R + 'Score_Number.png', 70, 85, [((0, 0, 0), INK, 6), ((255, 255, 255), None, 0)], 68)
    fit_digits(R + 'Judge_Number.png', 39, 50, [((30, 30, 36), None, 0)], 44)
    items = [('かんたん', (240, 120, 60)), ('ふつう', (110, 190, 80)), ('むずかしい', (90, 150, 200)), ('おに', (200, 60, 150)), ('おに裏', (130, 70, 200))]
    c = Canvas(1360, 95)
    for i, (name, col) in enumerate(items):
        ox = i * 272
        c.rect(ox + 30, 26, ox + 250, 82, fill=INK, radius=16)
        c.rect(ox + 34, 30, ox + 246, 78, fill=col, radius=13)
        c.text(ox + 50, 54, name, 22, (255, 255, 255), stroke=3, anchor='lm')
    c.save(R + 'CourseSymbol.png')

def shine(im, cell_w, cell_h, row, pos):
    """光が斜めに横切るコマを作る(pos 0〜1)"""
    arr = np.array(im).astype(float)
    ys, xs = np.mgrid[0:cell_h, 0:cell_w]
    band = np.abs((xs + ys * 0.8) - (pos * (cell_w + cell_h * 0.8))) < 18
    return band

def rank_crown():
    from play import RANKS, rank_letter
    W, H = 326, 276
    base = Canvas(W * 7, H)
    for i in range(7):
        rank_letter(base, i * W + 163, 138, i, 120)   # 11/15 たけらい「スコアランクの大きさのバランスおかしい」：170 → 120
    b = np.array(base.image()).astype(float)
    full = np.zeros((H * 4, W * 7, 4))
    for row in range(4):
        layer = b.copy()
        if row > 0:
            ys, xs = np.mgrid[0:H, 0:W * 7]
            local = xs % W
            band = np.abs((local + ys * 0.8) - ([0, 0.3, 0.55, 0.8][row] * (W + H * 0.8))) < 20
            m = band & (layer[:, :, 3] > 0)
            layer[m, :3] = 255
        full[row * H:(row + 1) * H] = layer
    save_image(Image.fromarray(full.astype(np.uint8), 'RGBA'), R + 'ScoreRank.png')
    # 王冠(クリア 銀・フルコンボ 金・ドンダ 虹)
    W, H = 170, 160
    base = Canvas(W * 3, H)
    for i in range(3):
        ox = i * W
        pts = [(ox + 20, 50), (ox + 52, 82), (ox + 85, 22), (ox + 118, 82), (ox + 150, 50), (ox + 140, 120), (ox + 30, 120)]
        base.poly(pts, fill=INK, outline=INK, width=12)
        for x, y in [(ox + 20, 46), (ox + 85, 18), (ox + 150, 46)]:
            base.circle(x, y, 12, fill=INK)
        base.rect(ox + 26, 126, ox + 144, 142, fill=INK, radius=4)
    for i in range(3):
        ox = i * W
        fill = [(200, 220, 230), (255, 205, 60), (255, 255, 255)][i]
        inner = [(ox + 24, 56), (ox + 52, 88), (ox + 85, 30), (ox + 118, 88), (ox + 146, 56), (ox + 136, 116), (ox + 34, 116)]
        base.poly(inner, fill=fill)
        for x, y in [(ox + 20, 46), (ox + 85, 18), (ox + 150, 46)]:
            base.circle(x, y, 8, fill=fill)
        base.rect(ox + 30, 128, ox + 140, 138, fill=fill, radius=3)
    b = np.array(base.image()).astype(float)
    # 虹(3つ目)
    import colorsys
    ys, xs = np.mgrid[0:H, 0:W]
    lut = np.array([colorsys.hsv_to_rgb(i / 256, 0.55, 1.0) for i in range(256)]) * 255
    part = b[:, 2 * W:3 * W]
    m = (part[:, :, :3].mean(axis=2) > 200) & (part[:, :, 3] > 0)
    part[m, :3] = lut[((xs / W) * 255).astype(int)][m]
    full = np.zeros((H * 4, W * 3, 4))
    for row in range(4):
        layer = b.copy()
        if row > 0:
            ys, xs = np.mgrid[0:H, 0:W * 3]
            band = np.abs(((xs % W) + ys * 0.8) - ([0, 0.3, 0.55, 0.85][row] * (W + H * 0.8))) < 16
            mm = band & (layer[:, :, 3] > 0) & (layer[:, :, :3].mean(axis=2) > 60)
            layer[mm, :3] = 255
        full[row * H:(row + 1) * H] = layer
    # ひとまわり小さく(マスの大きさは同じ。中身だけ 85%)
    src = Image.fromarray(full.astype(np.uint8), 'RGBA')
    out = Image.new('RGBA', src.size, (0, 0, 0, 0))
    for r in range(4):
        for k in range(3):
            cell = src.crop((k * W, r * H, k * W + W, r * H + H)).resize((int(W * 0.85), int(H * 0.85)), Image.LANCZOS)
            out.alpha_composite(cell, (k * W + (W - cell.width) // 2, r * H + (H - cell.height) // 2))
    save_image(out, R + 'Crown.png')

def balloons():
    W, H = 536, 350
    styles = [((255, 170, 210), (255, 230, 240)), ((255, 220, 80), (255, 245, 170)), ((140, 160, 255), (200, 170, 255)), ((190, 190, 196), (235, 235, 240))]
    c = Canvas(W, H * 4)
    for row, (c1, c2) in enumerate(styles):
        y0 = row * H
        c.rect(70, y0 + 26, 514, y0 + 326, fill=INK, radius=110)
        c.poly([(85, y0 + 200), (26, y0 + 250), (110, y0 + 240)], fill=INK)
        for y in range(34, 318):
            c.rect(78, y0 + y, 506, y0 + y + 1, fill=c1)
        c.d.rounded_rectangle([c.s(78), c.s(y0 + 34), c.s(506), c.s(y0 + 318)], radius=c.s(102), outline=None)
        # 角の外側(四角に塗った分)を消す
        mask = Image.new('L', (c.s(W), c.s(H)), 0)
        md = ImageDraw.Draw(mask)
        md.rounded_rectangle([c.s(70), c.s(26), c.s(514), c.s(326)], radius=c.s(110), fill=255)
        md.polygon([(c.s(85), c.s(200)), (c.s(26), c.s(250)), (c.s(110), c.s(240))], fill=255)
        region = c.im.crop((0, c.s(y0), c.s(W), c.s(y0 + H)))
        region.putalpha(Image.composite(region.getchannel('A'), Image.new('L', region.size, 0), mask))
        c.im.paste(region, (0, c.s(y0)))
        c.poly([(92, y0 + 206), (44, y0 + 244), (112, y0 + 232)], fill=c1)
    c.save(R + 'Speech_Balloon.png')
    words = [['かんぺき!', 'さいこう!', 'すごすぎ!'], ['いいね!', 'クリア!', 'やったね!'], ['おしい!', 'もうすこし!', 'つぎこそ!'], ['ドンマイ', 'がんばろう', 'ファイト!']]
    c = Canvas(W * 3, H * 4)
    for row in range(4):
        for col in range(3):
            c.text(col * W + 292, row * H + 172, words[row][col], 54, (255, 255, 255), stroke=6)
    c.save(R + 'Speech_Balloon_Text.png')

def run():
    chara_sheet(); end_texts(); fans(); confetti(); star()
    result_backgrounds(); result_panel(); result_numbers(); rank_crown()

if __name__ == '__main__':
    run()
