# TaikoSim-Simple スキンを作る共通の道具(画像はすべてここで新しく描く)
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math, os

HERE = os.path.dirname(os.path.abspath(__file__))
# できた画像・音を入れる場所(リポジトリの Simple スキン)。環境変数 TAIKOSIM_SKIN_OUT で変えられる
OUT = os.environ.get('TAIKOSIM_SKIN_OUT', os.path.normpath(os.path.join(HERE, '..', '..', 'TaikoSim', 'Skins', 'TaikoSim-Simple')))

def _find_font(env, candidates):
    """フォントを探す。環境変数 → 候補の順。どれも無ければ Simple スキンの Font/default.otf(Noto Sans CJK JP Bold)"""
    for path in [os.environ.get(env)] + candidates:
        if path and os.path.exists(path):
            return path
    return os.path.join(OUT, 'Font', 'default.otf')

# もともとは Noto Sans CJK の Black(太い)と Bold で描いた。無ければ default.otf で代わりに描く(少し細くなる)
FONT_BLACK = _find_font('TAIKOSIM_FONT_BLACK', [
    os.path.join(HERE, 'fonts', 'NotoSansCJK-Black.ttc'),
    '/usr/share/fonts/opentype/noto/NotoSansCJK-Black.ttc',
    'C:/Windows/Fonts/NotoSansCJKjp-Black.otf',
])
FONT_BOLD = _find_font('TAIKOSIM_FONT_BOLD', [
    os.path.join(HERE, 'fonts', 'NotoSansCJK-Bold.ttc'),
    '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc',
    'C:/Windows/Fonts/NotoSansCJKjp-Bold.otf',
])
SS = 3  # なめらかにするため、3倍で描いて縮める

# 色(落ち着いた暗め)
DON = (232, 72, 58)
DON_DARK = (170, 40, 32)
KA = (58, 172, 220)
KA_DARK = (30, 110, 160)
ROLL = (246, 190, 30)
ROLL_DARK = (190, 130, 10)
BALLOON = (255, 128, 40)
RIM = (245, 240, 228)
INK = (28, 28, 34)
LANE = (40, 40, 44)
PANEL = (52, 56, 72)
ACCENT = (255, 196, 64)

def font(size, bold=False):
    return ImageFont.truetype(FONT_BOLD if bold else FONT_BLACK, int(size), index=0)

def _translucent(*cols):
    return any(c is not None and len(c) == 4 and c[3] < 255 for c in cols)

class _BlendDraw:
    """半透明の色は、別の層に描いてから重ねる(PIL はそのまま上書きしてしまうので)"""
    def __init__(self, canvas):
        self.c = canvas
        self.raw = ImageDraw.Draw(canvas.im)
    def __getattr__(self, name):
        fn = getattr(self.raw, name)
        def call(*args, **kw):
            cols = [kw.get('fill'), kw.get('outline')] + [a for a in args if isinstance(a, tuple) and len(a) in (3, 4) and all(isinstance(v, int) for v in a)]
            if _translucent(*cols):
                layer = Image.new('RGBA', self.c.im.size, (0, 0, 0, 0))
                getattr(ImageDraw.Draw(layer), name)(*args, **kw)
                self.c.im.alpha_composite(layer)
            else:
                fn(*args, **kw)
        return call

class Canvas:
    """SS倍で描いて、save で元の大きさに縮める"""
    def __init__(self, w, h, bg=(0, 0, 0, 0)):
        self.w, self.h = w, h
        self.im = Image.new('RGBA', (w * SS, h * SS), bg)
        self.d = _BlendDraw(self)
    def s(self, v):
        return v * SS
    def circle(self, cx, cy, r, fill=None, outline=None, width=0):
        self.d.ellipse([self.s(cx - r), self.s(cy - r), self.s(cx + r), self.s(cy + r)],
                       fill=fill, outline=outline, width=int(self.s(width)))
    def rect(self, x0, y0, x1, y1, fill=None, outline=None, width=0, radius=0):
        if radius:
            self.d.rounded_rectangle([self.s(x0), self.s(y0), self.s(x1), self.s(y1)], radius=self.s(radius),
                                     fill=fill, outline=outline, width=int(self.s(width)))
        else:
            self.d.rectangle([self.s(x0), self.s(y0), self.s(x1), self.s(y1)], fill=fill, outline=outline,
                             width=int(self.s(width)))
    def poly(self, pts, fill=None, outline=None, width=0):
        p = [(self.s(x), self.s(y)) for x, y in pts]
        self.d.polygon(p, fill=fill)
        if outline and width:
            self.d.line(p + [p[0]], fill=outline, width=int(self.s(width)), joint='curve')
    def line(self, pts, fill, width):
        self.d.line([(self.s(x), self.s(y)) for x, y in pts], fill=fill, width=int(self.s(width)), joint='curve')
    def text(self, x, y, txt, size, fill, stroke=0, stroke_fill=INK, anchor='mm', bold=False):
        self.d.text((self.s(x), self.s(y)), txt, font=font(self.s(size), bold), fill=fill, anchor=anchor,
                    stroke_width=int(self.s(stroke)), stroke_fill=stroke_fill)
    def paste(self, other, x, y):
        self.im.alpha_composite(other.im, (int(self.s(x)), int(self.s(y))))
    def image(self):
        return self.im.resize((self.w, self.h), Image.LANCZOS)
    def save(self, rel):
        path = os.path.join(OUT, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.image().save(path)
        return path

def save_image(im, rel):
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path)

def glow(im, radius, strength=1.0):
    """光のにじみ(アルファをぼかして足す)"""
    blurred = im.filter(ImageFilter.GaussianBlur(radius))
    out = Image.alpha_composite(blurred, im)
    return out

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def hsv(h, s, v, a=255):
    import colorsys
    r, g, b = colorsys.hsv_to_rgb(h % 1.0, s, v)
    return (int(r * 255), int(g * 255), int(b * 255), a)

def fit_digits(rel, w, h, rows, size, stroke=0, margin=2, bottom=3):
    """0〜9 を横に10個並べる。字(縁取り込み)が1マスに必ず収まるように大きさを決める。
       rows：[(字の色, 縁の色 or None, 縁の太さ)] … 段ごと。どの段も同じ位置に描く(重ねて使う画像があるので)"""
    max_stroke = max([stroke] + [r[2] for r in rows])
    while True:
        f = font(size * SS)
        st = int(max_stroke * SS)
        d = ImageDraw.Draw(Image.new('RGBA', (8, 8)))
        boxes = [d.textbbox((0, 0), str(n), font=f, anchor='ls', stroke_width=st) for n in range(10)]
        top = min(b[1] for b in boxes); bot = max(b[3] for b in boxes)
        wid = max(b[2] - b[0] for b in boxes)
        if wid <= (w - 2 * margin) * SS and bot - top <= (h - margin - bottom) * SS:
            break
        size -= 1
    c = Canvas(w * 10, h * len(rows))
    for r, (fill, sf, sw) in enumerate(rows):
        for n in range(10):
            ink = d.textbbox((0, 0), str(n), font=f, anchor='ls')
            x = (n * w + w / 2) * SS - (ink[0] + ink[2]) / 2
            y = (r * h + h - bottom) * SS - bot
            c.d.text((x, y), str(n), font=f, fill=fill, anchor='ls', stroke_width=int(sw * SS), stroke_fill=sf or INK)
    c.save(rel)
    return size
