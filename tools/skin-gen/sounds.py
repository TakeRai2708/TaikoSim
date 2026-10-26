# 効果音とBGM(すべてプログラムで合成。著作物は使っていない)
import numpy as np, subprocess, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import OUT
SR = 44100
rng = np.random.default_rng(1)

def t(sec): return np.arange(int(SR * sec)) / SR
def env(n, a=0.005, d=0.2, sustain=0.0, release=None):
    x = np.arange(n) / SR
    e = np.minimum(1, x / max(a, 1e-4)) * (sustain + (1 - sustain) * np.exp(-x / d))
    return e
def note(freq, sec, wave='tri', d=0.4, amp=0.3, a=0.005):
    x = t(sec)
    ph = 2 * np.pi * freq * x
    if wave == 'sin': y = np.sin(ph)
    elif wave == 'tri': y = 2 / np.pi * np.arcsin(np.sin(ph))
    elif wave == 'sq': y = np.sign(np.sin(ph)) * 0.5
    else: y = np.sin(ph) + 0.3 * np.sin(2 * ph) + 0.15 * np.sin(3 * ph)
    return y * env(len(x), a=a, d=d) * amp
def bell(freq, sec, amp=0.25):
    x = t(sec)
    y = (np.sin(2 * np.pi * freq * x) + 0.5 * np.sin(2 * np.pi * freq * 2.76 * x) * np.exp(-x / 0.15)
         + 0.25 * np.sin(2 * np.pi * freq * 5.4 * x) * np.exp(-x / 0.06))
    return y * np.exp(-x / 0.6) * amp
def mix(total, parts):
    out = np.zeros(int(SR * total))
    for start, sig in parts:
        i = int(SR * start); j = min(len(out), i + len(sig))
        out[i:j] += sig[:j - i]
    return out
def mtof(m): return 440 * 2 ** ((m - 69) / 12)
def save(rel, y):
    y = y / max(1e-6, np.abs(y).max()) * 0.85
    path = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    wav = path[:-4] + '.wav'
    import wave
    with wave.open(wav, 'w') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((y * 32767).astype(np.int16).tobytes())
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', wav, '-c:a', 'libvorbis', '-q:a', '5', path], check=True)
    os.remove(wav)

def don():
    x = t(0.35)
    f = 70 + 110 * np.exp(-x / 0.03)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) * np.exp(-x / 0.12)
    y += rng.normal(0, 1, len(x)) * np.exp(-x / 0.006) * 0.4
    return y
def ka():
    x = t(0.12)
    n = rng.normal(0, 1, len(x))
    n = np.convolve(n, [1, -0.9], 'same')
    y = n * np.exp(-x / 0.012) * 0.6 + np.sin(2 * np.pi * 1900 * x) * np.exp(-x / 0.03) * 0.8 + np.sin(2 * np.pi * 3100 * x) * np.exp(-x / 0.02) * 0.4
    return y
def pop():
    x = t(0.25)
    return rng.normal(0, 1, len(x)) * np.exp(-x / 0.03) + np.sin(2 * np.pi * 600 * x * (1 + x * 3)) * np.exp(-x / 0.05) * 0.6

def arp(midis, step, sec_each, wave='tri', amp=0.3, d=0.5):
    return [(i * step, note(mtof(m), sec_each, wave, d=d, amp=amp)) for i, m in enumerate(midis)]

def fanfare(kind):
    if kind == 'fail':
        parts = arp([67, 64, 60, 55], 0.28, 1.2, 'tri', 0.3, 0.6) + [(1.2, note(mtof(48), 2.5, 'sin', d=1.2, amp=0.35))]
        return mix(4.5, parts)
    if kind == 'clear':
        parts = arp([60, 64, 67, 72], 0.12, 1.0, 'tri', 0.3) + [(0.55, sum_chord([60, 64, 67, 72], 2.8, 0.18))]
        return mix(4.5, parts)
    if kind == 'fc':
        parts = arp([60, 64, 67, 72, 76, 79], 0.1, 1.0, 'saw', 0.22) + [(0.7, sum_chord([60, 64, 67, 72, 76], 3.5, 0.15))]
        parts += [(1.2 + i * 0.25, bell(mtof(84 + (i % 3) * 3), 1.0, 0.12)) for i in range(8)]
        return mix(6.5, parts)
    parts = arp([60, 64, 67, 72, 76, 79, 84], 0.09, 1.0, 'saw', 0.22) + [(0.7, sum_chord([60, 64, 67, 71, 74, 79], 4.5, 0.15))]
    parts += [(1.0 + i * 0.18, bell(mtof(84 + (i * 5) % 12), 1.0, 0.12)) for i in range(16)]
    return mix(7.5, parts)
def sum_chord(midis, sec, amp):
    return sum(note(mtof(m), sec, 'tri', d=sec * 0.5, amp=amp, a=0.02) for m in midis)

def bgm():
    bpm = 120; beat = 60 / bpm; bars = 16; total = bars * 4 * beat
    prog = [[60, 64, 67], [57, 60, 64], [65, 69, 72], [67, 71, 74]]
    parts = []
    for bar in range(bars):
        chord = prog[bar % 4]
        t0 = bar * 4 * beat
        parts.append((t0, sum(note(mtof(m), 4 * beat, 'sin', d=2.0, amp=0.08, a=0.05) for m in chord)))
        for b in range(4):
            parts.append((t0 + b * beat, note(mtof(chord[0] - 24), beat * 0.9, 'tri', d=0.25, amp=0.25)))
            parts.append((t0 + b * beat, don()[:int(SR * 0.3)] * (0.35 if b % 2 == 0 else 0.0)))
            parts.append((t0 + b * beat + beat / 2, ka()[:int(SR * 0.1)] * 0.15))
        melody = [chord[2] + 12, chord[1] + 12, chord[0] + 12, chord[1] + 12]
        for i, m in enumerate(melody):
            parts.append((t0 + i * beat, bell(mtof(m), beat * 1.5, 0.08)))
    return mix(total, parts)

def sparkle(n_notes, tail):
    """キラリ✨：高い音が速く上がって、きらきらが残る"""
    base = [84, 88, 91, 96, 100, 103, 108]
    parts = [(i * 0.045, bell(mtof(base[i]), 0.9 + tail * 0.5, 0.22)) for i in range(n_notes)]
    # きらきら(高い音の粒が、だんだん小さくなりながら散る)
    r = np.random.default_rng(5)
    for k in range(int(10 * tail)):
        st = 0.15 + k * 0.09 + r.uniform(0, 0.04)
        m = 96 + r.integers(0, 4) * 4
        parts.append((st, bell(mtof(m), 0.5, 0.07 * (1 - k / (10 * tail + 1)))))
    # 最初の「シャッ」
    x = t(0.25)
    shh = rng.normal(0, 1, len(x)); shh = shh - np.convolve(shh, np.ones(6) / 6, 'same')
    parts.append((0, shh * np.exp(-x / 0.05) * 0.10))
    return mix(0.4 + tail + 0.6, parts)

def kyoton():
    """きょとん？：ポンと鳴って、「ん？」と少し上がって止まる"""
    x1 = t(0.18)
    f1 = 330 * np.exp(-x1 * 2.0)
    pon = np.sin(2 * np.pi * np.cumsum(f1) / SR) * np.exp(-x1 / 0.06)
    x2 = t(0.45)
    f2 = 300 + 140 * np.clip(x2 / 0.25, 0, 1) ** 1.5
    hm = np.sin(2 * np.pi * np.cumsum(f2) / SR) + 0.2 * np.sin(4 * np.pi * np.cumsum(f2) / SR)
    hm *= np.minimum(1, x2 / 0.02) * np.exp(-np.maximum(0, x2 - 0.25) / 0.06) * 0.55
    return mix(1.0, [(0, pon), (0.32, hm)])

def run():
    save('Sound/Taiko/0.Taiko/don.ogg', don())
    save('Sound/Taiko/0.Taiko/ka.ogg', ka())
    save('Sound/Taiko/Balloon.ogg', pop())
    save('Sound/EnsoGame/End/Failed.ogg', kyoton())
    save('Sound/EnsoGame/End/Clear.ogg', sparkle(3, 1.0))
    save('Sound/EnsoGame/End/FullCombo.ogg', sparkle(5, 1.3))
    save('Sound/EnsoGame/End/DondaFullCombo.ogg', sparkle(7, 1.6))
    save('Sound/Result/ScoreDon.ogg', mix(1.0, [(0, don() * 0.8), (0.02, bell(mtof(84), 0.9, 0.2))]))
    save('Sound/Result/ScoreRank.ogg', mix(1.2, [(0, bell(mtof(79), 1.1, 0.25)), (0.08, bell(mtof(84), 1.1, 0.25))]))
    save('Sound/Result/Crown_Clear.ogg', mix(1.5, [(i * 0.07, bell(mtof(m), 1.2, 0.2)) for i, m in enumerate([72, 76, 79])]))
    save('Sound/Result/Crown_FullCombo.ogg', mix(1.8, [(i * 0.07, bell(mtof(m), 1.4, 0.2)) for i, m in enumerate([72, 76, 79, 84])]))
    save('Sound/Result/Crown_DondaFullCombo.ogg', mix(2.2, [(i * 0.06, bell(mtof(m), 1.6, 0.2)) for i, m in enumerate([72, 76, 79, 84, 88, 91])]))
    # 吹き出しの音とリザルトの BGM は無し

if __name__ == '__main__':
    run()
