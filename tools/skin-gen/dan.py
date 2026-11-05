# 段位道場の画像(Simple スキン)
#   段位プレート：Image/06.DanSelect/DanPlate/{danIndex}.png（380×742。五級＝0 … 外伝＝19）
#   本家(A-Style)の絵はまねしない。色分けした縦長の板に、段位名(たて書き)と英語だけのシンプルなもの
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import Canvas, INK

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

def run():
    dan_plates()

if __name__ == '__main__':
    run()
