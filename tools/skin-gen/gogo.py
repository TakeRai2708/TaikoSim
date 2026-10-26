# コンボのキラキラ(MiniTaiko/Effect.png)。ゴーゴーの炎は gogo_fire.py
from common import *

E = 'Image/04.EnsoGame/'

def sparkle():
    S = 30
    c = Canvas(S, S)
    m = S / 2
    # 4つの角がある星(白)。まわりに少しだけ光
    for r, a in ((13, 70), (11, 255)):
        w = 2.6 if a == 255 else 4.5
        c.poly([(m, m - r), (m + w, m - w), (m + r, m), (m + w, m + w), (m, m + r), (m - w, m + w), (m - r, m), (m - w, m - w)],
               fill=(255, 255, 240, a))
    c.circle(m, m, 2.4, fill=(255, 255, 255, 255))
    c.save(E + 'MiniTaiko/Effect.png')

if __name__ == '__main__':
    sparkle()
