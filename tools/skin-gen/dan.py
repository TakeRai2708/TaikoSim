# 段位道場の画像(Simple スキン)
#   段位プレート：Image/06.DanSelect/DanPlate/{danIndex}.png（380×742。五級＝0 … 外伝＝19）
#   本家(A-Style)の絵はまねしない。色分けした縦長の板に、段位名(たて書き)と英語だけのシンプルなもの
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import Canvas, INK, OUT, SS, font, fit_digits, hsv, save_image

W, H = 380, 742

# (段位名, 英語, 色の組)。並びは danIndex の順（TaikøNauts と同じ）
DANS = [
    ('五級', 'FIFTH\nKYU', 'kyu'), ('四級', 'FOURTH\nKYU', 'kyu'), ('三級', 'THIRD\nKYU', 'kyu'),
    ('二級', 'SECOND\nKYU', 'kyu'), ('一級', 'FIRST\nKYU', 'kyu'),
    ('初段', 'FIRST\nDAN', 'low'), ('二段', 'SECOND\nDAN', 'low'), ('三段', 'THIRD\nDAN', 'low'),
    ('四段', 'FOURTH\nDAN', 'low'), ('五段', 'FIFTH\nDAN', 'low'),
    ('六段', 'SIXTH\nDAN', 'high'), ('七段', 'SEVENTH\nDAN', 'high'), ('八段', 'EIGHTH\nDAN', 'high'),
    ('九段', 'NINTH\nDAN', 'high'), ('十段', 'TENTH\nDAN', 'high'),
    ('玄人', 'KUROUTO', 'master'), ('名人', 'MEIJIN', 'master'), ('超人', 'CHOJIN', 'master'),
    ('達人', 'TATSUJIN', 'gold'), ('外伝', 'GAIDEN', 'gaiden'),
]

# 板の色(上・下)。落ち着いた色にして、上から下へ少し暗くする
COLORS = {
    'kyu': ((168, 120, 78), (128, 88, 54)),
    'low': ((52, 164, 160), (32, 118, 118)),
    'high': ((214, 72, 58), (160, 44, 36)),
    'master': ((124, 128, 142), (84, 88, 100)),
    'gold': ((222, 178, 54), (170, 126, 26)),
    'gaiden': ((52, 124, 84), (30, 86, 56)),
}

def plate(name, english, tone):
    top, bottom = COLORS[tone]
    c = Canvas(W, H)
    c.rect(10, 10, W - 10, H - 10, fill=INK + (255,), radius=34)                    # 黒いふち
    # 上から下へのグラデーション(横の帯を重ねる)
    steps = 60
    for i in range(steps):
        t = i / (steps - 1)
        col = tuple(int(a + (b - a) * t) for a, b in zip(top, bottom))
        y0 = 20 + (H - 40) * i / steps
        y1 = 20 + (H - 40) * (i + 1) / steps + 1
        c.rect(20, y0, W - 20, y1, fill=col + (255,))
    # 角を丸く見せるため、黒いふちをもう一度上から(中は抜く)
    c.rect(14, 14, W - 14, H - 14, outline=INK + (255,), width=10, radius=30)
    c.rect(32, 32, W - 32, H - 32, outline=(255, 255, 255, 150), width=4, radius=20)    # 内側の白い細い線

    # 段位名(たて書き 2文字)
    for k, ch in enumerate(name):
        c.text(W / 2, 175 + k * 200, ch, 176, fill=(255, 255, 255, 255), stroke=10, stroke_fill=INK + (255,))
    # 英語
    lines = english.split('\n')
    for k, ln in enumerate(lines):
        size = 52 if len(ln) <= 7 else 44
        c.text(W / 2, 590 + k * 58 - (len(lines) - 1) * 29, ln, size, fill=INK + (255,), bold=True)
    return c

def dan_plates():
    for index, (name, english, tone) in enumerate(DANS):
        plate(name, english, tone).save(f'Image/06.DanSelect/DanPlate/{index}.png')

# ===================================================================================
#  段位の画面の画像（11/5 たけらい「シンプルの段位の素材、ほぼ決まりだから組み込んで」）
#    A-Style と同じファイル名・大きさ・コマの並びにして、プログラムが同じ場所に置けるようにする。
#    大きさと中の部品の位置(タブ・枠・バーの場所)だけ A-Style に合わせて、絵は今のシンプルの見た目
#    (クリーム色の板・うす茶のタブ・こい色の縁・平らな色)で描く。A-Style の絵は使わない
# ===================================================================================
SEL = 'Image/06.DanSelect/'
GAME = 'Image/08.DanGame/'

CREAM = (249, 244, 226, 255)
CREAM_EDGE = (176, 150, 112, 255)
TAN = (246, 222, 178, 255)
TAN_EDGE = (168, 132, 92, 255)
LIGHT = (236, 236, 240, 255)
DARK_BAND = (61, 61, 61, 255)
WHITE = (255, 255, 255, 255)
INKA = INK + (255,)
SONG_COLORS = [(132, 204, 92), (88, 186, 226), (236, 132, 160)]   # 1st・2nd・3rd
TIERS = ['kyu', 'low', 'high', 'master', 'gold', 'gaiden']       # 札・パネルの横の模様の並び

def rgba(c, a=255):
    return tuple(c[:3]) + (a,)

def gradient_h(w, h, left, right):
    """左から右への色の変化(RGBA)"""
    t = np.linspace(0, 1, w)[None, :, None]
    a = np.array(left, float)[None, None, :] * (1 - t) + np.array(right, float)[None, None, :] * t
    return Image.fromarray(np.repeat(a, h, axis=0).astype(np.uint8), 'RGBA')

def rainbow_strip(w, h, offset, sat=0.55):
    """虹の帯(offset で色が流れる)"""
    xs = np.arange(w)
    hue = (xs / w * 0.85 + offset) % 1.0
    import colorsys
    cols = np.array([colorsys.hsv_to_rgb(v, sat, 1.0) for v in hue]) * 255
    a = np.zeros((h, w, 4), np.uint8); a[:, :, :3] = cols[None, :, :]; a[:, :, 3] = 255
    return Image.fromarray(a, 'RGBA')

def song_ball(c, cx, cy, r, k, size=None):
    """1st/2nd/3rd の色玉"""
    c.circle(cx, cy, r, fill=rgba(SONG_COLORS[k]), outline=WHITE, width=max(2, r * 0.1))
    c.text(cx, cy + 1, ['1st', '2nd', '3rd'][k], size or r * 0.95, fill=WHITE, stroke=max(2, r * 0.12), stroke_fill=INKA)

def coin(c, cx, cy, r):
    """条件のコイン(シンプル：黄色い丸に白い点)"""
    c.circle(cx, cy, r, fill=(240, 196, 60, 255), outline=(200, 150, 30, 255), width=max(2, r * 0.12))
    c.circle(cx, cy, r * 0.35, fill=(255, 236, 160, 255))

# ---------- 06.DanSelect（段位選択） ----------

def mini_plates():
    """段位の札 80×168 が6色(級・初〜五段・六〜十段・玄人〜超人・達人・外伝)。名前はプログラムが黒い字で書く"""
    c = Canvas(480, 168)
    for k, tone in enumerate(TIERS):
        top, bottom = COLORS[tone]
        x = k * 80
        c.rect(x + 5, 2, x + 73, 162, fill=INKA, radius=10)
        c.rect(x + 9, 6, x + 69, 158, fill=rgba(top), radius=7)
        c.rect(x + 9, 100, x + 69, 158, fill=rgba(bottom), radius=7)
        c.rect(x + 9, 100, x + 69, 120, fill=rgba(bottom))
        c.rect(x + 14, 11, x + 64, 153, outline=(255, 255, 255, 120), width=2, radius=5)
    c.save(SEL + 'MiniPlate.png')

def plate_effect():
    c = Canvas(80, 168)
    c.rect(5, 2, 73, 162, fill=(255, 255, 255, 74), outline=WHITE, width=5, radius=10)
    c.save(SEL + 'Plate_Effect.png')

def plate_arrow():
    c = Canvas(56, 72)
    c.poly([(40, 8), (40, 56), (8, 32)], fill=WHITE, outline=INKA, width=4)
    c.save(SEL + 'Plate_Arrow.png')

def stamps():
    """合格した札に乗せる「合格」90×76 が6つ。並び：(クリア・フルコンボ・ドンダフル)×(赤・金)"""
    rings = [(214, 72, 58), (226, 182, 50), (214, 72, 58), (226, 182, 50), (214, 72, 58), (226, 182, 50)]
    inner = [(255, 250, 240), (255, 250, 240), (230, 236, 244), (230, 236, 244), (255, 236, 250), (255, 236, 250)]
    c = Canvas(540, 76)
    for k in range(6):
        cx = k * 90 + 42
        c.circle(cx, 39, 31, fill=rgba(inner[k]), outline=rgba(rings[k]), width=6)
        if k >= 4:   # ドンダフルは虹の小さい点
            for i in range(8):
                a = i / 8 * math.tau
                c.circle(cx + math.cos(a) * 31, 39 + math.sin(a) * 31, 3, fill=hsv(i / 8, 0.6, 1.0))
        c.text(cx, 40, '合格', 24, fill=rgba(rings[k]), stroke=3, stroke_fill=WHITE)
    c.save(SEL + 'Stamp.png')

def panel():
    """クリーム色の大パネル 1522×858（右下に影・「合格条件」のタブと枠入り）"""
    c = Canvas(1522, 858)
    c.rect(14, 19, 1508, 843, fill=(0, 0, 0, 70), radius=6)          # 影
    c.rect(11, 16, 1505, 840, fill=INKA, radius=6)                    # こい色の縁
    c.rect(20, 25, 1496, 831, fill=CREAM, radius=2)
    c.rect(76, 413, 1440, 831, fill=CREAM, outline=CREAM_EDGE, width=2, radius=22)
    c.rect(76, 379, 486, 415, fill=(96, 72, 52, 255), radius=12)
    c.rect(76, 400, 486, 415, fill=(96, 72, 52, 255))
    c.text(281, 397, '合格条件', 24, fill=WHITE)
    c.save(SEL + 'Panel.png')

def panel_side():
    """パネルの左右の模様 40×806 が6色（札と同じ並び）。シンプルは細いしまだけ"""
    im = Image.new('RGBA', (240, 806), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    for k, tone in enumerate(TIERS):
        top, bottom = COLORS[tone]
        d.rectangle([k * 40, 0, k * 40 + 39, 805], fill=rgba(top))
        for y in range(0, 806, 24):
            d.rectangle([k * 40 + 6, y, k * 40 + 33, y + 11], fill=rgba(bottom))
    save_image(im, SEL + 'Panel_Side.png')

def song_bars():
    """課題曲の帯 1380×110 が3つ（1st/2nd/3rd の玉入り）。難易度のマークと★はプログラムが乗せる"""
    c = Canvas(1380, 330)
    for k in range(3):
        y = k * 110
        c.rect(8, y + 5, 1251, y + 105, fill=WHITE, outline=(214, 208, 196, 255), width=2, radius=50)
        c.rect(108, y + 9, 206, y + 101, fill=(244, 238, 222, 255), radius=10)   # 難易度と★の台
        song_ball(c, 55, y + 55, 40, k)
    im = c.image()
    # 右のはしを、その曲の色でうすく染める
    for k in range(3):
        g = gradient_h(560, 100, rgba(SONG_COLORS[k], 0), rgba(SONG_COLORS[k], 110))
        mask = im.crop((690, k * 110 + 5, 1250, k * 110 + 105)).getchannel('A')
        layer = Image.new('RGBA', im.size, (0, 0, 0, 0)); g.putalpha(Image.fromarray(np.minimum(np.array(g.getchannel('A')), np.array(mask))))
        layer.paste(g, (690, k * 110 + 5)); im = Image.alpha_composite(im, layer)
    save_image(im, SEL + 'SongBar.png')

def course_symbols():
    """難易度のマーク 78×52 が5つ（かんたん・ふつう・むずかしい・おに・裏おに）"""
    cols = [(232, 92, 72), (110, 186, 70), (90, 150, 210), (214, 60, 150), (120, 80, 210)]
    chars = ['易', '普', '難', '鬼', '裏']
    c = Canvas(390, 52)
    for k in range(5):
        cx = k * 78 + 39
        c.circle(cx, 26, 23, fill=rgba(cols[k]), outline=INKA, width=3)
        c.text(cx, 27, chars[k], 26, fill=WHITE)
    c.save(SEL + 'Song_CourseSymbol.png')

def condition_bar():
    """条件の箱 1033×150（上にラベルのタブ）"""
    c = Canvas(1033, 150)
    c.rect(24, 28, 1014, 134, fill=CREAM, outline=CREAM_EDGE, width=2, radius=16)
    c.rect(25, 13, 322, 45, fill=TAN, outline=TAN_EDGE, width=2, radius=16)
    c.save(SEL + 'Condition_Bar.png')

def gauge_condition_bar():
    """魂ゲージの箱 333×184"""
    c = Canvas(333, 184)
    c.rect(5, 25, 321, 173, fill=CREAM, outline=CREAM_EDGE, width=2, radius=16)
    c.rect(31, 9, 297, 41, fill=TAN, outline=TAN_EDGE, width=2, radius=16)
    coin(c, 31, 82, 20)
    c.rect(12, 120, 314, 152, fill=WHITE, radius=16)
    c.save(SEL + 'GaugeCondition_Bar.png')

def condition_content():
    """箱の中身：50px ずつ「コイン／1st／2nd／3rd／白いバー」"""
    c = Canvas(333, 250)
    coin(c, 31, 25, 16)
    for k in range(3):
        song_ball(c, 31, 75 + k * 50, 16, k, size=15)
    c.rect(14, 210, 332, 242, fill=WHITE, radius=16)
    c.save(SEL + 'Condition_Content.png')

def confirm():
    """確認のポップアップ（1368×488 を原寸）：箱(質問の文字入り)・ボタン・カーソル"""
    c = Canvas(1368, 488)
    c.rect(24, 24, 1348, 468, fill=INKA, radius=28)
    c.rect(34, 34, 1334, 454, fill=(214, 72, 58, 255), radius=22)
    c.rect(52, 52, 1316, 436, fill=CREAM, radius=14)
    c.text(684, 160, 'この段位に挑戦する？', 52, fill=INKA)
    c.save(SEL + 'Challenge_Check/Panel_Base.png')

    c = Canvas(1368, 488)
    # スパナ(演奏オプション)
    c.rect(299, 247, 377, 323, fill=(250, 214, 60, 255), outline=INKA, width=4, radius=14)
    c.line([(318, 304), (352, 268)], fill=INKA, width=9)
    c.circle(356, 264, 11, fill=INKA); c.circle(361, 259, 5, fill=(250, 214, 60, 255))
    for x0, label, fill in [(438, '挑戦する', (250, 214, 60, 255)), (776, '挑戦しない', (232, 228, 218, 255))]:
        c.rect(x0, 247, x0 + 282, 321, fill=fill, outline=INKA, width=4, radius=37)
        c.text(x0 + 141, 285, label, 34, fill=INKA)
    c.save(SEL + 'Challenge_Check/Panel.png')

    c = Canvas(350, 1050)
    yellow, white = (255, 236, 40, 255), WHITE
    def pill(k, color, glow=False, fill=False):
        y = k * 150
        if glow:
            c.rect(11, y + 15, 339, y + 135, outline=rgba(color, 110), width=10, radius=60)
        c.rect(17, y + 21, 333, y + 129, fill=(rgba(color, 255) if fill else None), outline=color, width=7, radius=54)
    def box(k, color, glow=False):
        y = k * 150
        if glow:
            c.rect(109, y + 10, 239, y + 140, outline=rgba(color, 110), width=10, radius=22)
        c.rect(115, y + 16, 233, y + 134, outline=color, width=7, radius=18)
    pill(0, yellow); pill(1, yellow, glow=True); pill(2, white); pill(3, white, fill=True)
    box(4, yellow); box(5, yellow, glow=True); box(6, white)
    c.save(SEL + 'Challenge_Check/Select_Effect.png')

# ---------- 08.DanGame（段位の演奏中） ----------

def condition_panel_background():
    """下半分の背景 1920×540"""
    im = Image.new('RGBA', (1920, 540), (226, 226, 232, 255)); d = ImageDraw.Draw(im)
    d.rectangle([0, 0, 1919, 7], fill=INKA)
    d.rectangle([0, 8, 1919, 11], fill=(214, 72, 58, 255))
    for y in range(40, 540, 40):
        d.line([(0, y), (1919, y)], fill=(216, 216, 222, 255), width=1)
    save_image(im, GAME + 'ConditionPanel/Background.png')

def condition_row_base():
    """行の背景 1530×150（タブ入り）"""
    c = Canvas(1530, 150)
    c.rect(3, 3, 1519, 141, fill=(246, 246, 249, 255), outline=(200, 200, 208, 255), width=2, radius=22)
    c.rect(3, 3, 367, 39, fill=TAN, outline=TAN_EDGE, width=2, radius=18)
    c.save(GAME + 'ConditionPanel/Base.png')

def contexts():
    """220×65 が4つ：コイン・1st・2nd・3rd（右へうすくなる帯つき）"""
    im = Image.new('RGBA', (220, 260), (0, 0, 0, 0))
    for k, col in enumerate([(240, 196, 60)] + SONG_COLORS):
        im.alpha_composite(gradient_h(190, 49, rgba(col, 130), rgba(col, 0)), (30, k * 65 + 8))
    c = Canvas(220, 260); c.im = im.resize((220 * SS, 260 * SS), Image.LANCZOS); c.d = type(c.d)(c)
    coin(c, 31, 32, 28)
    for k in range(3):
        song_ball(c, 31, 65 * (k + 1) + 32, 28, k, size=24)
    c.save(GAME + 'ConditionPanel/Context.png')

def mini_contexts():
    c = Canvas(80, 40)
    for k in range(2):
        song_ball(c, k * 40 + 20, 20, 17, k, size=14)
    c.save(GAME + 'ConditionPanel/Mini_Context.png')

def gauge_bases():
    """ゲージの枠：上のコマ＝こい色の枠（中は抜き）、下のコマ＝中の暗い帯"""
    for name, w, frame_r in [('Large', 1008, 14), ('Small', 684, 14)]:
        c = Canvas(w, 240)
        c.rect(15, 14, w - 15, 106, outline=INKA, width=7, radius=frame_r)
        c.rect(21, 140, w - 21, 220, fill=DARK_BAND, radius=8)
        c.save(GAME + f'ConditionPanel/{name}_Gauge_Base.png')
    c = Canvas(254, 100)
    c.rect(9, 8, 246, 43, outline=INKA, width=4, radius=6)
    c.rect(12, 61, 242, 89, fill=DARK_BAND, radius=4)
    c.save(GAME + 'ConditionPanel/Mini_Gauge_Base.png')

def gauge_fills():
    """白いバー（プログラムが色をかける）と虹のコマ"""
    for name, w, h in [('Large', 972, 86), ('Small', 644, 86), ('Mini', 232, 30)]:
        save_image(Image.new('RGBA', (w, h), WHITE), GAME + f'ConditionPanel/{name}_Gauge.png')
    for name, w, frames in [('Large', 972, 16), ('Small', 644, 8)]:
        im = Image.new('RGBA', (w, 86 * frames))
        for f in range(frames):
            im.paste(rainbow_strip(w, 86, f / frames), (0, f * 86))
        save_image(im, GAME + f'ConditionPanel/{name}_Gauge_Rainbow.png')
    save_image(rainbow_strip(232, 30, 0.0), GAME + 'ConditionPanel/Mini_Gauge_Rainbow.png')

def rainbow_digits(rel, w, h, size, stroke):
    """0〜9 の数字 2段（上＝白、下＝虹）。下の段は白い字を縦のグラデーションで塗りかえる"""
    fit_digits(rel, w, h, [(WHITE, INKA, stroke), (WHITE, INKA, stroke)], size, stroke=stroke)
    path = os.path.join(OUT, rel)
    im = Image.open(path).convert('RGBA'); a = np.array(im).astype(float)
    row = a[h:2 * h]
    bright = row[:, :, :3].min(axis=2) > 200
    t = np.linspace(0, 1, h)[:, None]
    top, mid, bot = np.array([255, 120, 90.]), np.array([255, 214, 70.]), np.array([120, 200, 255.])
    col = np.where(t < 0.5, top * (1 - t * 2) + mid * (t * 2), mid * (2 - t * 2) + bot * (t * 2 - 1))
    col = np.repeat(col[:, None, :], row.shape[1], axis=1)
    row[:, :, :3] = np.where(bright[:, :, None], col, row[:, :, :3])
    a[h:2 * h] = row
    Image.fromarray(a.astype(np.uint8), 'RGBA').save(path)

def disqualify_text():
    """「達成失敗」972×86 が2つ（大きいバー用・小さいバー用）"""
    c = Canvas(972, 172)
    c.text(486, 43, '達成失敗', 60, fill=(236, 60, 50, 255), stroke=7, stroke_fill=WHITE)
    c.text(322, 129, '達成失敗', 60, fill=(236, 60, 50, 255), stroke=7, stroke_fill=WHITE)
    c.save(GAME + 'ConditionPanel/Disqualify_Text.png')

def mini_position():
    """まだ遊んでいない曲の灰色の丸と帯 288×56"""
    c = Canvas(288, 56)
    c.circle(20, 28, 18, fill=(210, 210, 214, 255))
    c.rect(46, 12, 280, 44, fill=(210, 210, 214, 255), radius=6)
    c.save(GAME + 'ConditionPanel/Mini_Gauge_Position.png')

def gauge_plate():
    """魂ゲージの札 610×86（タブと赤い矢印入り）・目印 43×70"""
    c = Canvas(610, 86)
    c.rect(20, 14, 606, 74, fill=(246, 246, 249, 255), outline=(200, 200, 208, 255), width=2, radius=14)
    c.rect(20, 14, 316, 50, fill=TAN, outline=TAN_EDGE, width=2, radius=16)
    c.rect(577, 28, 587, 46, fill=(222, 40, 36, 255))
    c.poly([(570, 45), (594, 45), (582, 62)], fill=(222, 40, 36, 255))
    c.save(GAME + 'Gauge/Condition_Plate.png')
    c = Canvas(43, 70)
    c.rect(8, 5, 35, 65, fill=INKA, radius=5)
    c.rect(12, 9, 31, 61, fill=WHITE, radius=3)
    c.save(GAME + 'Gauge/Condition_Stopper.png')

def course_symbol():
    """左のパネルの「段位道場」マーク 136×136"""
    c = Canvas(136, 136)
    c.poly([(68, 6), (112, 50), (68, 94), (24, 50)], fill=(214, 72, 58, 255), outline=INKA, width=5)
    c.text(68, 51, '段', 40, fill=WHITE, stroke=4, stroke_fill=INKA)
    c.text(68, 118, '段位道場', 25, fill=WHITE, stroke=4, stroke_fill=INKA)   # A-Style も絵の下に字が入っている
    c.save(GAME + 'MiniTaiko/CourseSymbol.png')

def end_text():
    """最後の字 800×150 が3つ：金の合格・赤の合格・不合格"""
    c = Canvas(800, 450)
    c.text(400, 75, '合格', 110, fill=(250, 204, 50, 255), stroke=12, stroke_fill=INKA)
    c.text(400, 225, '合格', 110, fill=(236, 72, 56, 255), stroke=12, stroke_fill=INKA)
    c.text(400, 375, '不合格', 110, fill=(110, 186, 236, 255), stroke=12, stroke_fill=INKA)
    c.save(GAME + 'End/Text.png')

def screen():
    """曲と曲のあいだのふすま 1422×195（左半分・右半分）。DanShutter のコードのふすまと同じ絵"""
    wood, wood_light = (78, 52, 34, 255), (112, 78, 50, 255)
    c = Canvas(1422, 195)
    for k in range(2):
        x = k * 711
        c.rect(x, 0, x + 710, 194, fill=wood)
        c.rect(x + 2, 2, x + 708, 192, outline=wood_light, width=2)
    im = c.image(); d = ImageDraw.Draw(im, 'RGBA')
    for k in range(2):
        x = k * 711 + 9
        for i in range(177):
            t = i / 176
            col = tuple(int(a + (b - a) * t) for a, b in zip((240, 233, 216), (226, 216, 194)))
            d.line([(x, 9 + i), (x + 692, 9 + i)], fill=col + (255,))
        for y in range(22, 186, 17):
            d.line([(x, y), (x + 692, y)], fill=(214, 202, 178, 90))
        for f in (0.2, 0.8):
            d.rectangle([x, 9 + 177 * f, x + 692, 10 + 177 * f], fill=(196, 182, 156, 255))
    c2 = Canvas(1422, 195); c2.im = im.resize((1422 * SS, 195 * SS), Image.LANCZOS); c2.d = type(c2.d)(c2)
    for kx in (711 - 9 - 34, 711 + 9 + 34):
        c2.circle(kx, 97, 17, fill=(150, 118, 62, 255)); c2.circle(kx, 97, 14, fill=(196, 162, 92, 255)); c2.circle(kx, 97, 10, fill=(58, 40, 26, 255))
    c2.save(GAME + 'Screen.png')

def dan_screens():
    mini_plates(); plate_effect(); plate_arrow(); stamps(); panel(); panel_side(); song_bars(); course_symbols()
    condition_bar(); gauge_condition_bar(); condition_content(); confirm()
    condition_panel_background(); condition_row_base(); contexts(); mini_contexts(); gauge_bases(); gauge_fills()
    rainbow_digits(GAME + 'ConditionPanel/Large_Gauge_Number.png', 80, 100, 92, 7)
    rainbow_digits(GAME + 'ConditionPanel/Mini_Gauge_Number.png', 30, 38, 34, 3)
    disqualify_text(); mini_position(); gauge_plate(); course_symbol(); end_text(); screen()

def run():
    dan_plates()
    dan_screens()

if __name__ == '__main__':
    run()
