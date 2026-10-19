"""티저 소리: 배경음(합성)과 효과음을 시뮬레이션 사건 기록(events.json)에 맞춰 섞는다.

사용: python tools/cine_audio.py <events.json> <out.wav>
효과음은 게임 자산(assets/audio)과 합성음을 쓴다. 거리가 멀수록 작게, 큰 함선일수록 멀리까지 들린다.
"""
import json
import sys
from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, resample_poly, sosfilt, fftconvolve

SR = 48000
ROOT = Path(__file__).resolve().parent.parent
RNG = np.random.default_rng(7)


def lp(x, hz, order=2):
    return sosfilt(butter(order, hz, "low", fs=SR, output="sos"), x)


def hp(x, hz, order=2):
    return sosfilt(butter(order, hz, "high", fs=SR, output="sos"), x)


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], "band", fs=SR, output="sos"), x)


def t_axis(sec):
    return np.arange(int(sec * SR)) / SR


def noise(sec):
    return RNG.standard_normal(int(sec * SR))


def env_exp(sec, decay, attack=0.004):
    t = t_axis(sec)
    a = np.minimum(1, t / max(attack, 1e-4))
    return a * np.exp(-t / decay)


def load(name, pitch=1.0):
    sr, d = wavfile.read(ROOT / "assets" / "audio" / f"{name}.wav")
    d = d.astype(np.float32) / 32768.0
    if d.ndim == 1:
        d = np.stack([d, d], 1)
    up, down = 160, 147  # 44.1k -> 48k
    if pitch != 1.0:
        down = int(round(down * pitch))
    return np.stack([resample_poly(d[:, c], up, down) for c in range(2)], 1)


def stereo(mono, pan=0.0):
    l = np.cos((pan + 1) * np.pi / 4)
    r = np.sin((pan + 1) * np.pi / 4)
    return np.stack([mono * l, mono * r], 1)


# ── 합성 효과음 ────────────────────────────────────────────

def explosion(size=1.0, sec=3.2):
    t = t_axis(sec)
    f = 30 + 40 * np.exp(-t / 0.18)
    thump = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(sec, 0.55, 0.003) * 1.2
    body = lp(noise(sec), 900) * env_exp(sec, 0.35 + 0.25 * size, 0.002)
    rumble = lp(noise(sec), 160, 4) * env_exp(sec, 1.2 + 0.6 * size, 0.05) * 1.8
    crackle = hp(noise(sec) * (RNG.random(int(sec * SR)) > 0.985), 1500) * env_exp(sec, 0.6, 0.01) * 0.6
    return thump + body + rumble + crackle


def whoosh(sec=1.6):
    """미사일 사출·점화음(게임의 missile_launch*.wav와 같은 구성)."""
    t = t_axis(sec)
    x = noise(sec)
    lo = bp(x, 250, 900) * np.clip(1 - t / sec, 0, 1)
    hi = bp(x, 1200, 4500) * np.exp(-t / 0.35)
    rum = lp(x, 120, 4) * env_exp(sec, 0.8, 0.05) * 2
    return (lo * 0.8 + hi * 0.6 + rum) * env_exp(sec, 0.9, 0.02)


def pop(sec=0.25):
    return hp(noise(sec), 900) * env_exp(sec, 0.04, 0.001) + np.sin(2 * np.pi * 180 * t_axis(sec)) * env_exp(sec, 0.06) * 0.5


def pd_burst(n):
    sec = 0.03 * n + 0.1
    out = np.zeros(int(sec * SR))
    click = hp(noise(0.012), 2500) * env_exp(0.012, 0.003, 0.0005)
    for i in range(n):
        s = int(i * 0.03 * SR)
        out[s:s + len(click)] += click * (0.7 + 0.3 * RNG.random())
    return out


def ping(freq=1400, sec=0.6):
    t = t_axis(sec)
    tone = np.sin(2 * np.pi * freq * t) * env_exp(sec, 0.12, 0.002)
    echo = np.concatenate([np.zeros(int(0.12 * SR)), tone[: len(tone) - int(0.12 * SR)]]) * 0.35
    return tone + echo


def beep(freq=1800, sec=0.09):
    t = t_axis(sec)
    return np.sin(2 * np.pi * freq * t) * np.minimum(1, np.minimum(t, sec - t) / 0.005)


def tom(sec=1.0, pitch=70):
    t = t_axis(sec)
    f = pitch * (0.55 + 0.45 * np.exp(-t / 0.08))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * env_exp(sec, 0.35, 0.002)
    skin = lp(noise(sec), 2000) * env_exp(sec, 0.03, 0.001) * 0.5
    return body + skin


def braam(sec=6.0):
    t = t_axis(sec)
    notes = [36.71, 73.42, 110.0, 146.83, 174.61]  # D1 D2 A2 D3 F3
    x = np.zeros_like(t)
    for i, f in enumerate(notes):
        for det in (-0.4, 0.4):
            ph = 2 * np.pi * (f + det) * t
            saw = 2 * ((ph / (2 * np.pi)) % 1) - 1
            x += saw * (0.5 if i < 2 else 0.3)
    x = lp(x, 900) * env_exp(sec, 2.2, 0.02)
    return x + explosion(1.5, sec) * 0.5


def riser(sec=2.0):
    t = t_axis(sec)
    x = noise(sec)
    out = np.zeros_like(t)
    for i in range(8):
        a, b = int(i * len(t) / 8), int((i + 1) * len(t) / 8)
        lo = 300 + 2400 * i / 8
        out[a:b] = bp(x[a:b], lo, lo * 2.2)
    return out * (t / sec) ** 2


# ── 배경음 ────────────────────────────────────────────────

def plan(duration, shots):
    """편집본별 음악 계획: 세기 곡선, 북 구간, 8분음 장면, 상승음 시각, 느린 구간."""
    start = {s["name"]: s["start"] for s in shots}
    if "brawl" in start:   # v3 티저
        kill = start["brawl"] + 4.6
        return dict(
            keys=[(0, 0.35), (start["contact"], 0.25), (start["salvo"], 0.6), (start["defense"], 0.7), (start["broadside"], 0.75),
                  (start["strike"], 0.85), (start["brawl"], 1.0), (kill, 0.25), (start["title"], 0.9), (duration, 0.5)],
            drums=(start["salvo"], kill), driving=("strike", "brawl"), double="brawl",
            risers=((start["brawl"] - 1.8, 1.8), (start["title"] + 1.3 - 1.6, 1.6)),
            slow=(kill, start["brawl"] + 8.5), title=start["title"])
    # v4: 정박지 → 편대 → 머리 위 → 정찰 → 방공 → 강습 → 원거리 격침 → 제목.
    kill = start["farkill"] + 3.5
    return dict(
        keys=[(0, 0.3), (start["lineup"], 0.42), (start["overhead"], 0.6), (start["probe"], 0.4), (start["laser"], 0.75),
              (start["assault"], 0.9), (start["farkill"], 0.65), (kill, 1.0), (kill + 1.0, 0.35), (start["title"], 0.9), (duration, 0.5)],
        drums=(start["overhead"], kill), driving=("assault",), double="assault",
        risers=((kill - 1.6, 1.6), (start["title"] + 1.3 - 1.6, 1.6)),
        slow=(start["assault"] + 4.2, start["assault"] + 6.2), title=start["title"])


def music(duration, shots):
    t = t_axis(duration)
    p = plan(duration, shots)
    # 장면별 세기 곡선.
    xs, ys = zip(*p["keys"])
    intensity = np.interp(t, xs, ys)
    # 저음 드론 + 단조 화음 패드(D 단조), 세기에 따라 필터가 열린다.
    drone = np.sin(2 * np.pi * 36.71 * t) * 0.6 + np.sin(2 * np.pi * 55.0 * t) * 0.25
    pad = np.zeros_like(t)
    for f in (146.83, 174.61, 220.0, 293.66):
        for det in (-0.6, 0.0, 0.7):
            ph = 2 * np.pi * (f + det) * t
            pad += (2 * ((ph / (2 * np.pi)) % 1) - 1) * 0.08
    pad = lp(pad, 700) * (0.4 + 0.6 * intensity) * (0.7 + 0.3 * np.sin(2 * np.pi * t / 7))
    bed = drone * 0.5 + pad
    # 북: 96 BPM, 미사일전부터 격침 직전까지. 난전에서 8분음을 더한다.
    beat = 60 / 96
    drums = np.zeros_like(t)
    n, kill = p["drums"]
    i = 0
    while n < kill:
        name = next(s["name"] for s in shots if s["start"] <= n < s["start"] + s["duration"])
        hit = i % 4 in (0, 2) or name in p["driving"]
        if hit:
            h = tom(1.0, 62 if i % 4 == 0 else 78) * (1.0 if i % 4 == 0 else 0.6)
            s0 = int(n * SR)
            seg = drums[s0:s0 + len(h)]
            seg += h[: len(seg)]
        n += beat / (2 if name == p["double"] else 1)
        i += 1
    out = stereo(bed * 0.35) + stereo(drums * 0.55)
    # 상승음: 난전 직전과 제목 직전.
    for at, sec in p["risers"]:
        r = riser(sec) * 0.25
        s0 = int(at * SR)
        out[s0:s0 + len(r)] += stereo(r)[: len(out) - s0]
    # 제목: 큰 화음 타격.
    b = braam(6.5) * 0.55
    s0 = int((p["title"] + 1.3) * SR)
    out[s0:s0 + len(b)] += stereo(b)[: len(out) - s0]
    return out


def reverb(x, sec=2.2, mix=0.25):
    ir = noise(sec) * np.exp(-t_axis(sec) / (sec / 5))
    ir = lp(ir, 3000) / np.sqrt(np.sum(ir ** 2))
    wet = np.stack([fftconvolve(x[:, c], ir)[: len(x)] for c in range(2)], 1)
    return x * (1 - mix) + wet * mix


def main(events_path, out_path):
    data = json.loads(Path(events_path).read_text(encoding="utf-8"))
    duration = data["duration"] + 0.5
    shots = data["shots"]
    start = {s["name"]: s["start"] for s in shots}
    n = int(duration * SR)
    sfx = np.zeros((n, 2))

    # 피탄음은 게임과 같은 변주 4종을 돌려 쓴다(바로 앞과 같은 변주는 피하고, 음높이도 ±7% 흔든다).
    def variants(name):
        return [load(name)] + [load(f"{name}_v{i}") for i in (2, 3, 4)]
    banks = {
        "rail": [load("railgun_fire", p) for p in (0.97, 1.0, 1.04)],
        "rail_big": [load("railgun_fire", p) for p in (1.3, 1.36, 1.42)],
        "shield": variants("shield_impact"), "armor": variants("armor_block"),
        "module": variants("hull_penetration"), "crit": variants("critical_impact"),
        "pd": [load(f"pd_fire_0{i}") for i in (1, 2, 3)],
    }
    last = {}

    def pick(bank):
        clips = banks[bank]
        choices = [i for i in range(len(clips)) if i != last.get(bank)]
        i = int(RNG.choice(choices))
        last[bank] = i
        clip = clips[i]
        jitter = float(RNG.uniform(0.93, 1.07))
        if abs(jitter - 1) > 0.01:   # 짧은 리샘플로 음높이·길이를 함께 흔든다
            n = max(1, int(len(clip) / jitter))
            idx = np.minimum((np.arange(n) * jitter).astype(int), len(clip) - 1)
            clip = clip[idx]
        return clip
    synth = {"launch": whoosh(), "missile": explosion(0.6, 2.4), "pop": pop(), "boom": explosion(1.0), "kill": explosion(2.0, 5.0),
             "break": explosion(1.4, 4.0)}
    slow_from, slow_to = plan(duration, shots)["slow"]

    def place(clip, at, gain, pan=0.0, slow=False):
        if clip.ndim == 1:
            clip = stereo(clip, pan)
        if slow:
            clip = np.stack([resample_poly(clip[:, c], 5, 3) for c in range(2)], 1)
        s0 = int(at * SR)
        if s0 >= n:
            return
        e = min(n, s0 + len(clip))
        sfx[s0:e] += clip[: e - s0] * gain

    counts = {}
    for e in data["events"]:
        kind, at, d, size = e["kind"], e["t"], e["d"], e["s"]
        ref = max(200.0, size * 2.5)
        gain = float(np.clip(1.0 / (1.0 + (d / ref) ** 1.3), 0.0, 1.0))
        if gain < 0.015:
            continue
        # 같은 순간에 같은 소리가 겹쳐 쌓이지 않게 30 ms 안의 같은 종류는 하나로.
        key = (kind, round(at / 0.03))
        if key in counts:
            continue
        counts[key] = 1
        pan = float(RNG.uniform(-0.5, 0.5))
        slow = slow_from <= at <= slow_to
        if kind == "rail":
            place(pick("rail_big" if size > 600 else "rail"), at, 0.9 * gain, pan, slow)
        elif kind == "shield":
            place(pick("shield"), at, 0.7 * gain, pan, slow)
        elif kind == "armor":
            place(pick("armor"), at, 0.75 * gain, pan, slow)
        elif kind == "module":
            place(pick("module"), at, 0.8 * gain, pan, slow)
            place(pick("crit"), at, 0.5 * gain, pan, slow)
        elif kind == "pd":
            place(pick("pd"), at, 0.3 * gain, pan, slow)
        elif kind == "probe":
            place(whoosh(0.9) * 0.6, at, 0.5 * gain, pan, slow)
        elif kind == "ping":
            place(ping(1100), at, 0.35, 0.0, slow)
        elif kind in synth:
            g = {"launch": 0.55, "missile": 0.6, "pop": 0.4, "boom": 0.75, "kill": 1.0, "break": 0.8}[kind]
            place(synth[kind], at, g * gain, pan * 0.5, slow)

    # 탐지 화면: 탐색 빔이 접촉을 지날 때 핑, 잠금 순간 삑.
    if "contact" in start:
        c0 = start["contact"]
        for i in range(9):
            place(ping(1300 + 40 * i), c0 + 0.35 + i * 0.09, 0.12)
        place(ping(900), c0 + 1.9, 0.18)
        for k in range(2):
            place(beep(1800), c0 + 3.0 + k * 0.13, 0.22)
    # v4 원거리 격침: 30 km 밖이라 폭발음은 거의 들리지 않는다. 대신 게임 전과 알림처럼
    # 리본이 튈 때마다 짧은 핑(같은 리본이 쌓이면 음이 오른다), 격침 배너에 낮은 타격음과 삑.
    if "farkill" in start:
        f0 = start["farkill"]
        ribbons = ((0.9, "hit"), (1.5, "hit"), (1.8, "pen"), (2.3, "hit"), (2.6, "pen"), (2.9, "hit"))
        seen = {}
        for at, kind in ribbons:
            seen[kind] = seen.get(kind, 0) + 1
            place(ping((1500 if kind == "hit" else 1250) + 90 * seen[kind]), f0 + at, 0.14)
        place(synth["kill"], f0 + 3.5, 0.5)
        place(beep(1400), f0 + 3.5, 0.2)
        place(beep(1900), f0 + 3.62, 0.2)

    mix = music(duration, shots) * 0.8 + reverb(sfx, 1.6, 0.22) * 0.9
    mix = reverb(mix, 2.4, 0.12)
    # 마스터: 부드러운 제한 + 앞뒤 페이드.
    mix = np.tanh(mix * 1.4) / np.tanh(1.4)
    fade_in = np.minimum(1, t_axis(duration)[: len(mix)] / 1.2)
    fade_out = np.clip((duration - 0.3 - t_axis(duration)[: len(mix)]) / 2.0, 0, 1)
    mix *= (fade_in * fade_out)[:, None]
    mix /= max(1e-6, np.max(np.abs(mix))) / 0.89
    wavfile.write(out_path, SR, (mix * 32767).astype(np.int16))
    print(f"audio: {duration:.1f}s, {len(data['events'])} events -> {out_path}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
