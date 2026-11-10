# モード選択・選曲画面の画像(TakeTaikoSimulator のプログラムで描いていた見た目を、そのまま画像にしたもの)
#   色が変わるもの(板・難易度の大きい板)は、白い「土台」と、縁などの「上にのせる」画像に分ける
#   プログラムが土台にカテゴリの色をかけて描き、その上に「上にのせる」画像を描く
from common import *

M = 'Image/01.ModeSelect/'
S = 'Image/02.SongSelect/'
INK2 = (24, 24, 32)
WHITE = (255, 255, 255)

COURSES = [  # 色・印・名前(かんたん・ふつう・むずかしい・おに・裏おに)
    ((240, 90, 40), '易', 'かんたん'),
    ((130, 190, 40), '普', 'ふつう'),
    ((70, 140, 170), '難', 'むずかしい'),
    ((210, 50, 160), '鬼', 'おに'),
    ((120, 70, 210), '裏', '裏おに'),
]
NONE_COLOR = (120, 122, 132)

def bright(c, f):
    """raylib の ColorBrightness と同じ(f < 0 で暗く)"""
    if f < 0:
        return tuple(int(v * (1 + f)) for v in c[:3])
    return tuple(int(v + (255 - v) * f) for v in c[:3])

def tint(c, a):
    return tuple(int(v + (255 - v) * a) for v in c[:3])

def fit_text(c, cx, cy, txt, size, maxw, fill, stroke=0, stroke_fill=INK2, bold=False):
    """はみ出さない大きさで、真ん中に書く"""
    d = ImageDraw.Draw(Image.new('RGBA', (8, 8)))
    while size > 6:
        b = d.textbbox((0, 0), txt, font=font(c.s(size), bold), stroke_width=int(c.s(stroke)))
        if (b[2] - b[0]) <= c.s(maxw):
            break
        size -= 1
    c.text(cx, cy, txt, size, fill, stroke=stroke, stroke_fill=stroke_fill, bold=bold)

# ===== 斜めの板(932×124) =====

def bars(folder):
    W, H = 932, 124
    # 土台：白い板(プログラムが色をかける)
    c = Canvas(W, H); c.rect(6, 6, W - 6, H - 6, fill=WHITE + (255,), radius=17); c.save(folder + 'Bar_Base.png')
    # 上にのせる：黒い縁と、中の明るい板(白の60%)
    for name, inner in (('Bar_Overlay.png', True), ('Bar_Overlay_Plain.png', False)):
        c = Canvas(W, H)
        ring = Image.new('L', c.im.size, 0); rd = ImageDraw.Draw(ring)
        rd.rounded_rectangle([0, 0, c.s(W) - 1, c.s(H) - 1], radius=c.s(22), fill=255)
        rd.rounded_rectangle([c.s(6), c.s(6), c.s(W - 6), c.s(H - 6)], radius=c.s(17), fill=0)
        layer = Image.new('RGBA', c.im.size, INK2 + (255,)); layer.putalpha(ring)
        c.im.alpha_composite(layer)
        if inner:
            c.rect(70, 20, W - 70, H - 20, fill=(255, 255, 255, 153), radius=8)
        c.save(folder + name)
    # 開いた板のまわりの枠(こい黄色・決定したあとの白)。TaikøNauts の動画の色 (253,253,0)
    #   決定の点滅の明るいときは、プログラムが白を 56.5% 重ねる
    for name, col in (('Bar_Cursor.png', (253, 253, 0)), ('Bar_Cursor_Decide.png', (255, 255, 255))):
        c = Canvas(W + 40, H + 40); c.rect(0, 0, W + 40, H + 40, fill=col + (255,), radius=30); c.save(folder + name)

def label_bar():
    W, H = 932, 124
    c = Canvas(W, H)
    c.rect(0, 0, W, H, fill=(20, 24, 36, 200), radius=22)
    c.rect(70, 20, W - 70, H - 20, fill=(120, 122, 134, 200), radius=8)
    c.save(S + 'Bar_Label.png')

# ===== 開いた曲の板の、難易度の札(175×85 を縦に5つ) =====

def chips():
    W, H = 175, 85
    c = Canvas(W, H * 5)
    for i, (col, mark, name) in enumerate(COURSES):
        y = i * H
        ink = bright(col, -0.7)
        c.rect(0, y, W, y + H, fill=col + (255,))
        c.rect(92, y + 6, W - 6, y + H - 6, fill=bright(col, -0.25) + (255,), radius=7)
        c.text(47, y + 29, mark, 38, WHITE, stroke=3, stroke_fill=ink)
        fit_text(c, 47, y + 69, name, 19, 84, WHITE, stroke=2, stroke_fill=ink)
    c.save(S + 'Chip.png')

# ===== 難易度をえらぶ板 =====

def panel():
    W, H = 1210, 746
    c = Canvas(W, H); c.rect(7, 7, W - 7, H - 7, fill=WHITE + (255,), radius=34); c.save(S + 'Panel_Base.png')
    c = Canvas(W, H)
    ring = Image.new('L', c.im.size, 0); rd = ImageDraw.Draw(ring)
    rd.rounded_rectangle([0, 0, c.s(W) - 1, c.s(H) - 1], radius=c.s(40), fill=255)
    rd.rounded_rectangle([c.s(7), c.s(7), c.s(W - 7), c.s(H - 7)], radius=c.s(34), fill=0)
    layer = Image.new('RGBA', c.im.size, INK2 + (255,)); layer.putalpha(ring)
    c.im.alpha_composite(layer)
    c.rect(36, 35, 36 + 1140, 35 + 210, fill=(255, 255, 255, 140), radius=10)
    c.save(S + 'Panel_Overlay.png')

def gear(c, cx, cy, col, bg):
    for i in range(6):
        a = math.radians(i * 30)
        dx, dy = math.sin(a), -math.cos(a)
        px, py = math.cos(a), math.sin(a)
        pts = [(cx + dx * 23 * s1 + px * 4.5 * s2, cy + dy * 23 * s1 + py * 4.5 * s2) for s1, s2 in ((1, 1), (1, -1), (-1, -1), (-1, 1))]
        c.poly(pts, fill=col)
    c.circle(cx, cy, 17, fill=col)
    c.circle(cx, cy, 7, fill=bg)

def small_boxes():
    # 戻る(110×105)
    W, H = 110, 105
    fill = (170, 122, 52); edge = (81, 48, 9)
    c = Canvas(W, H)
    c.rect(0, 0, W, H, fill=INK2 + (255,), radius=18)
    c.rect(5, 5, W - 5, H - 5, fill=fill + (255,), radius=14)
    c.text(W / 2, 38, '◀', 44, WHITE, stroke=3, stroke_fill=edge)
    fit_text(c, W / 2, 80, 'もどる', 21, W - 14, WHITE, stroke=2, stroke_fill=INK2)
    c.save(S + 'Back.png')
    # オプション(108×105)
    W = 108
    fill = (220, 228, 70)
    c = Canvas(W, H)
    c.rect(0, 0, W, H, fill=INK2 + (255,), radius=18)
    c.rect(5, 5, W - 5, H - 5, fill=fill + (255,), radius=14)
    gear(c, W / 2, 44, (90, 90, 100), fill)
    fit_text(c, W / 2, 84, 'オプション', 20, W - 12, WHITE, stroke=2, stroke_fill=INK2)
    c.save(S + 'Option.png')

def cards():
    # 188×345 を横に5つ(かんたん〜裏おに)。下の段は「その難易度が無い」とき
    W, H = 188, 345
    c = Canvas(W * 5, H * 2)
    for row in (0, 1):
        for i, (col, mark, name) in enumerate(COURSES):
            x, y = i * W, row * H
            color = col if row == 0 else NONE_COLOR
            ink = bright(color, -0.7)
            c.rect(x, y, x + W, y + H, fill=INK2 + (255,), radius=18)
            c.rect(x + 5, y + 5, x + W - 5, y + H - 5, fill=color + (255,), radius=14)
            c.rect(x + 11, y + 11, x + W - 11, y + 11 + 335 * 0.55, fill=tint(color, 0.55) + (255,), radius=10)
            c.circle(x + W / 2, y + 115, 62, fill=(255, 255, 255, 217))
            c.text(x + W / 2, y + 113, mark, 80, color, stroke=4, stroke_fill=ink)
            fit_text(c, x + W / 2, y + 220, name, 34, W - 18, WHITE, stroke=3, stroke_fill=ink)
            if row == 1:
                c.text(x + W / 2, y + 262, 'なし', 30, WHITE, stroke=3, stroke_fill=ink)
                continue
            # ★の列(★はプログラムが描く。ここは空いている点だけ)
            c.rect(x + 13, y + 280, x + W - 13, y + 302, fill=bright(color, -0.45) + (255,), radius=11)
            cell = (W - 26) / 10
            for s in range(10):
                c.circle(x + 13 + cell * (s + 0.5), y + 291, 2.5, fill=(255, 255, 255, 140))
    c.save(S + 'Card.png')
    # 選んでいる箱のまわりの枠(9つに切って伸ばして使う)
    c = Canvas(212, 369); c.rect(0, 0, 212, 369, fill=(255, 240, 40, 255), radius=26); c.save(S + 'Card_Cursor.png')

def marker():
    W, H = 112, 118
    c = Canvas(W, H)
    cx, cy = 56, 56
    c.poly([(cx - 14, cy + 34), (cx, cy + 58), (cx + 14, cy + 34)], fill=INK2 + (255,))
    c.circle(cx, cy, 53, fill=INK2 + (255,))
    c.circle(cx, cy, 48, fill=(230, 60, 50, 255))
    c.poly([(cx - 10, cy + 36), (cx, cy + 52), (cx + 10, cy + 36)], fill=(230, 60, 50, 255))
    c.text(cx, cy - 2, '1P', 46, WHITE, stroke=3, stroke_fill=INK2)
    c.save(S + 'Marker_1P.png')

def hint():
    W, H = 360, 85
    c = Canvas(W, H)
    c.rect(0, 0, W, H, fill=WHITE + (255,), radius=13)
    for x0, txt in ((18, 'カッ：えらぶ'), (190, 'ドン：決定')):
        c.rect(x0, 18, x0 + 152, 68, fill=INK2 + (255,), radius=25)
        c.rect(x0 + 3, 21, x0 + 149, 65, fill=WHITE + (255,), radius=22)
        fit_text(c, x0 + 76, 43, txt, 24, 136, INK2, bold=True)
    c.save(S + 'Hint.png')


# ===== TaikøNauts と同じ並び方の画像(プログラムは A-Style と同じ場所を使う) =====

def star_poly(c, cx, cy, r, fill, stroke=None, sw=0):
    pts = []
    for i in range(10):
        a = math.radians(-90 + i * 36)
        rr = r if i % 2 == 0 else r * 0.45
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    if stroke:
        c.poly(pts, fill=stroke, outline=stroke, width=sw * 2)
    c.poly(pts, fill=fill)

def tn_chips():
    """Difficulty_Panel.png：開いた曲の板の札。1つ 176×86 を 182px ごとに5つ(3,2 から)。★まで描く(数字はプログラム)"""
    c = Canvas(910, 90)
    for i, (col, mark, name) in enumerate(COURSES):
        x, y, W, H = 3 + 182 * i, 2, 176, 86
        ink = bright(col, -0.7)
        c.rect(x, y, x + W, y + H, fill=col + (255,), radius=6)
        c.rect(x + 92, y + 7, x + W - 7, y + H - 7, fill=bright(col, -0.25) + (255,), radius=7)
        c.text(x + 47, y + 30, mark, 38, WHITE, stroke=3, stroke_fill=ink)
        fit_text(c, x + 47, y + 70, name, 19, 84, WHITE, stroke=2, stroke_fill=ink)
        star_poly(c, x + 114, y + 41, 15, WHITE + (255,), INK2 + (255,), 2)
    c.save(S + 'Difficulty_Panel.png')

def number_sheet(rel, cw, ch, size, stroke):
    """0〜9 を横に10個(白い数字に黒いふち)"""
    c = Canvas(cw * 10, ch)
    for n in range(10):
        fit_text(c, cw * n + cw / 2, ch / 2, str(n), size, cw - 2, WHITE, stroke=stroke, stroke_fill=INK2)
    c.save(rel)

def tn_cards():
    """Difficulty_Select/Difficulty_Panel.png：戻る(1,1 114×112)・オプション(133,1 114×112)・カード(265+215×番号, 1 から 196×354)"""
    c = Canvas(1330, 356)
    # 戻る・オプション
    for x0, fill, label in ((1, (170, 122, 52), 'もどる'), (133, (220, 228, 70), 'オプション')):
        c.rect(x0, 1, x0 + 114, 113, fill=INK2 + (255,), radius=18)
        c.rect(x0 + 5, 6, x0 + 109, 108, fill=fill + (255,), radius=14)
        if label == 'もどる':
            c.text(x0 + 57, 42, '◀', 44, WHITE, stroke=3, stroke_fill=(81, 48, 9))
        else:
            gear(c, x0 + 57, 46, (90, 90, 100), fill)
        fit_text(c, x0 + 57, 88, label, 20, 104, WHITE, stroke=2, stroke_fill=INK2)
    for i, (col, mark, name) in enumerate(COURSES):
        x, y, W, H = 265 + 215 * i, 1, 196, 354
        ink = bright(col, -0.7)
        c.rect(x, y, x + W, y + H, fill=INK2 + (255,), radius=18)
        c.rect(x + 5, y + 5, x + W - 5, y + H - 5, fill=col + (255,), radius=14)
        c.rect(x + 11, y + 11, x + W - 11, y + 200, fill=tint(col, 0.55) + (255,), radius=10)
        c.circle(x + W / 2, y + 112, 64, fill=(255, 255, 255, 217))
        c.text(x + W / 2, y + 110, mark, 82, col, stroke=4, stroke_fill=ink)
        fit_text(c, x + W / 2, y + 222, name, 34, W - 20, WHITE, stroke=3, stroke_fill=ink)
        star_poly(c, x + 77.5, 263.5, 14, WHITE + (255,), ink + (255,), 2)
        # ★の列(10個の点。★はプログラムが重ねる)
        c.rect(x + 20, 284, x + 176, 302, fill=bright(col, -0.45) + (255,), radius=9)
        for s in range(10):
            c.circle(x + 30 + 15 * s, 293, 2.2, fill=(255, 255, 255, 150))
    c.save(S + 'Difficulty_Select/Difficulty_Panel.png')
    # 点々に重ねる★(16×16)
    c = Canvas(16, 16); star_poly(c, 8, 8.5, 7.5, WHITE + (255,)); c.save(S + 'Difficulty_Select/Difficulty_Level_Star.png')
    # カードの枠(9つに切って伸ばす)
    c = Canvas(240, 400); c.rect(12, 9, 228, 391, fill=(255, 240, 40, 255), radius=26); c.save(S + 'Difficulty_Select/Cursor_Effect.png')
    c = Canvas(160, 160); c.rect(13, 10, 147, 151, fill=(255, 240, 40, 255), radius=22); c.save(S + 'Difficulty_Select/Cursor_Effect_Small.png')

def balloon():
    """Maker_Balloon.png(870×122)：開いたジャンルの板の上に出る「全 ○ 曲」の吹き出し。下のまんなかに三角(文字はプログラム)
    A-Style と同じ大きさ・同じ場所(箱は 45〜825 × 24〜94、三角の先は 435, 112)"""
    W, H = 870, 122
    c = Canvas(W, H)
    # 黒いふち(箱＋三角)
    c.rect(45, 24, 825, 94, fill=INK2 + (255,), radius=35)
    c.poly([(413, 90), (457, 90), (435, 112)], fill=INK2 + (255,))
    # 中(明るい灰色)
    c.rect(51, 30, 819, 88, fill=(244, 244, 244, 255), radius=29)
    c.poly([(420, 86), (450, 86), (435, 103)], fill=(244, 244, 244, 255))
    c.save(S + 'Maker_Balloon.png')

def option_images():
    """演奏オプションの板(02.SongSelect/Option/)。A-Style と同じ名前・同じ大きさ。Simple なので最低限の形だけ
    Background.png 432×614(暗い板と細い青のふち)・Option_Base.png 364×52(白い帯。選んでいる項目はプログラムが黄色をかける)・
    Option_Value_Base.png 180×40(白い丸い箱。プログラムが暗い色をかける)・Option_Value_Arrow.png 28×32(◀)・PlayerSide.png 64×64 が横に3つ"""
    O = S + 'Option/'
    c = Canvas(432, 614)
    c.rect(6, 6, 426, 608, fill=(30, 33, 44, 245), outline=(59, 130, 246, 255), width=3, radius=22)
    c.save(O + 'Background.png')
    c = Canvas(364, 52)
    c.rect(0, 0, 364, 52, fill=(255, 255, 255, 255), radius=14)
    c.save(O + 'Option_Base.png')
    c = Canvas(180, 40)
    c.rect(0, 0, 180, 40, fill=(255, 255, 255, 255), radius=20)
    c.save(O + 'Option_Value_Base.png')
    c = Canvas(28, 32)
    c.poly([(3, 16), (25, 3), (25, 29)], fill=KA + (255,))
    c.save(O + 'Option_Value_Arrow.png')
    c = Canvas(192, 64)
    for i, (col, txt) in enumerate(((DON, '1P'), (KA, '1P'), (KA, '2P'))):
        cx = 32 + i * 64
        c.circle(cx, 32, 29, fill=(255, 255, 255, 255))
        c.circle(cx, 32, 26, fill=col + (255,))
        c.text(cx, 33, txt, 24, (255, 255, 255, 255), bold=True)
    c.save(O + 'PlayerSide.png')

def run():
    bars(M); bars(S); label_bar()
    panel(); marker(); hint()
    tn_chips(); tn_cards(); balloon(); option_images()
    number_sheet(S + 'Difficulty_Level_Number.png', 31, 37, 40, 3)
    number_sheet(S + 'Difficulty_Select/Difficulty_Level_Number.png', 23, 28, 30, 2)

if __name__ == '__main__':
    run()
