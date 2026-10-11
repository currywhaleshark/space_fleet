"""미사일 발사음(티저 v2의 합성음)을 게임용 WAV 세 가지 변주로 만든다.

python tools/make-missile-launch-audio.py  ->  assets/audio/missile_launch.wav, _v2, _v3
사출 순간의 둔탁한 저음 + 모터 점화의 쉭 소리 + 멀어지는 고음 꼬리. 결정적(시드 고정)이며 44.1 kHz 스테레오 16비트.
"""
from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 44100
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "audio"


def band(x, lo, hi, rng_order=2):
    return sosfilt(butter(rng_order, [lo, hi], "band", fs=SR, output="sos"), x)


def low(x, hz, order=2):
    return sosfilt(butter(order, hz, "low", fs=SR, output="sos"), x)


def env(t, decay, attack):
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)


def whoosh(seed, sec=1.6, tilt=1.0):
    """티저(tools/cine_audio.py whoosh)와 같은 구성. tilt로 고음 꼬리 길이를 바꿔 변주를 만든다."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(sec * SR)) / SR
    x = rng.standard_normal(len(t))
    lo = band(x, 250, 900) * np.clip(1 - t / sec, 0, 1)
    hi = band(x, 1200, 4500) * np.exp(-t / (0.35 * tilt))
    rum = low(x, 120, 4) * env(t, 0.8, 0.05) * 2
    y = (lo * 0.8 + hi * 0.6 + rum) * env(t, 0.9, 0.02)
    # 끝 30 ms 페이드: 잘린 꼬리의 딸깍 소리 방지.
    y[-int(0.03 * SR):] *= np.linspace(1, 0, int(0.03 * SR))
    # 좌우에 아주 작은 지연을 줘 공간감(가운데 고정은 유지).
    d = int(0.004 * SR)
    left, right = y, np.concatenate([np.zeros(d), y[:-d]])
    st = np.stack([left, right], 1)
    return st / np.max(np.abs(st)) * 0.6


for name, seed, tilt in (("missile_launch", 7, 1.0), ("missile_launch_v2", 19, 0.8), ("missile_launch_v3", 31, 1.25)):
    wavfile.write(OUT / f"{name}.wav", SR, (whoosh(seed, tilt=tilt) * 32767).astype(np.int16))
    print("wrote", name)
