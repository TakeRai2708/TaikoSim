# TaikoSim-Simple：王冠（銀・金・虹）とエントリーの黄色い板
#   11/26 たけらい「シンプルスキンの王冠ダサくない？」→ 見本で決めた形（まん中の丸は無し）
#   11/26 たけらい「エントリー画面は文字がずれてる。ここは素材にできない？」→ CreditBar.png
from PIL import Image
from common import Canvas, save_image, lerp, INK, font, SS

S = 'Image/02.SongSelect/'
EN = 'Image/01.Entry/'

# 王冠の形（見本の SVG と同じ。幅 90・高さ 70 の箱の座標）
CROWN = [(10, 58), (6, 18), (26, 34), (45, 8), (64, 34), (84, 18), (80, 58)]
BAND = (10, 46, 80, 60)
STYLES = {
    'none':    ((96, 92, 100), (74, 70, 78), (40, 36, 44)),
    'silver':  ((217, 221, 227), (180, 186, 196), (58, 63, 71)),
    'gold':    ((255, 204, 51), (240, 168, 0), (107, 61, 0)),
    'rainbow': (None, (201, 184, 255), (43, 35, 80)),
}
RAINBOW = [(255, 122, 168), (255, 216, 74), (110, 224, 176), (122, 184, 255)]

def rainbow_at(t):
    t = max(0.0, min(1.0, t)) * (len(RAINBOW) - 1)
    i = min(int(t), len(RAINBOW) - 2)
    return lerp(RAINBOW[i], RAINBOW[i + 1], t - i)

def crown(x, y, w, kind):
    """左上 (x,y)・幅 w の王冠を、透明な画像で返す（高さは w*70/90）"""
    k = w / 90
    h = int(round(70 * k))
    c = Canvas(int(round(w)), h)
    body, band, ink = STYLES[kind]
    pts = [(px * k, py * k) for px, py in CROWN]
    sw = 4 * k
    if kind == 'rainbow':
        # 横に虹色：体の形で切り抜いた虹の帯
        mask = Canvas(c.w, c.h)
        mask.poly(pts, fill=(255, 255, 255, 255))
        grad = Image.new('RGBA', mask.im.size)
        px = grad.load()
        for gx in range(grad.width):
            col = rainbow_at(gx / grad.width) + (255,)
            for gy in range(grad.height):
                px[gx, gy] = col
        grad.putalpha(mask.im.split()[3])
        c.im.alpha_composite(grad)
        c.poly(pts, outline=ink + (255,), width=sw)
    else:
        c.poly(pts, fill=body + (255,), outline=ink + (255,), width=sw)
    c.rect(BAND[0] * k, BAND[1] * k, BAND[2] * k, BAND[3] * k, fill=band + (255,), outline=ink + (255,), width=sw, radius=3 * k)
    return c.image()

def difficulty_crowns():
    # 難易度パネルの王冠：50×40 が横に4つ（なし・銀・金・虹）。データ選択・自己ベストの王冠もこれを使う
    out = Image.new('RGBA', (200, 40), (0, 0, 0, 0))
    for i, kind in enumerate(['none', 'silver', 'gold', 'rainbow']):
        im = crown(0, 0, 46, kind)
        out.alpha_composite(im, (i * 50 + (50 - im.width) // 2, (40 - im.height) // 2))
    save_image(out, S + 'Difficulty_Select/Crown.png')

COURSES = [('か', (240, 120, 60)), ('ふ', (110, 190, 80)), ('む', (90, 150, 200)), ('お', (200, 60, 150)), ('裏', (130, 70, 200))]

def clear_type_symbols():
    # 曲のバーの王冠：80×80 が 横3（銀・金・虹）× 縦5（かんたん〜おに裏）。右下に難易度の丸
    out = Image.new('RGBA', (240, 400), (0, 0, 0, 0))
    for row, (ch, col) in enumerate(COURSES):
        for k, kind in enumerate(['silver', 'gold', 'rainbow']):
            im = crown(0, 0, 66, kind)
            cell = Canvas(80, 80)
            cell.im.alpha_composite(im.resize((im.width * SS, im.height * SS), Image.LANCZOS), (int(4 * SS), int(12 * SS)))
            cell.circle(62, 60, 14, fill=INK + (255,))
            cell.circle(62, 60, 11.5, fill=col + (255,))
            cell.text(62, 60.5, ch, 15, (255, 255, 255, 255), bold=True)
            out.alpha_composite(cell.image(), (k * 80, row * 80))
    save_image(out, S + 'ClearType_Symbol.png')

def credit_bar():
    # エントリーの「太鼓をたたいてスタート!」の板：1172×190 が縦に2つ（1P・2P）
    #   文字はゲームが描く。文字の高さのまん中（1P は上から 99.5・2P は 85.5。LayoutAdjustments.Entry）に板のまん中を合わせる
    W, H = 1172, 190
    c = Canvas(W, H * 2)
    for i, (label, mid, col) in enumerate((('1P', 99.5, (232, 72, 58)), ('2P', 85.5, (58, 140, 220)))):
        y0 = i * H + mid - 78
        c.rect(14, y0 + 6, W - 14, y0 + 162, fill=(0, 0, 0, 70), radius=34)
        c.rect(14, y0, W - 14, y0 + 156, fill=(255, 226, 61, 255), outline=INK + (255,), width=5, radius=34)
        c.rect(26, y0 + 12, W - 26, y0 + 40, fill=(255, 240, 150, 255), radius=14)
        c.rect(70, y0 + 34, 330, y0 + 122, fill=col + (255,), outline=INK + (255,), width=5, radius=44)
        c.text(200, y0 + 79, label, 62, (255, 255, 255, 255), stroke=6, stroke_fill=INK + (255,))
    c.save(EN + 'CreditBar.png')

def run():
    difficulty_crowns(); clear_type_symbols(); credit_bar()

if __name__ == '__main__':
    run()
