"""Prepare generated propulsion/PD audio and distinct impact variants.

Uses the existing numpy installation. Keeps source WAVs and existing runtime
sounds unchanged; refuses to overwrite this pack. Run from any directory.
"""
import hashlib
import json
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1] / "assets/audio"
RATE = 44100
SOURCES = {}


def read(relative):
    path = ROOT / (relative + ".wav")
    with wave.open(str(path), "rb") as wav:
        if wav.getsampwidth() != 2 or wav.getnchannels() not in (1, 2):
            raise ValueError(f"Expected mono/stereo PCM16: {path}")
        rate, channels = wav.getframerate(), wav.getnchannels()
        data = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2").reshape(-1, channels).astype(float) / 32768
    if not len(data) or not np.isfinite(data).all() or np.max(np.abs(data)) < .001:
        raise ValueError(f"Invalid or silent source: {path}")
    SOURCES[relative] = {"sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                         "seconds": len(data) / rate, "sample_rate": rate, "channels": channels}
    if channels == 1:
        data = np.repeat(data, 2, axis=1)
    return speed(data, rate / RATE)


def speed(data, ratio):
    x = np.arange(int(len(data) / ratio)) * ratio
    return np.column_stack([np.interp(x, np.arange(len(data)), data[:, channel]) for channel in range(2)])


def band(data, low=30, high=12000):
    frequency = np.fft.rfftfreq(len(data), 1 / RATE)
    weights = np.minimum(1, (frequency / low) ** 4) / (1 + (frequency / high) ** 6)
    weights[0] = 0
    return np.fft.irfft(np.fft.rfft(data, axis=0) * weights[:, None], n=len(data), axis=0)


def onset(data):
    block = RATE // 200
    rms = np.array([np.sqrt(np.mean(data[i:i + block] ** 2)) for i in range(0, len(data), block)])
    index = np.flatnonzero(rms >= rms.max() * .18)[0]
    return data[max(0, index * block - int(.006 * RATE)):]


def mix(seconds, layers):
    out = np.zeros((round(seconds * RATE), 2))
    for data, ratio, gain, delay, low, high in layers:
        layer = band(speed(data, ratio), low, high)
        start = round(delay * RATE)
        count = min(len(layer), len(out) - start)
        if count > 0:
            out[start:start + count] += layer[:count] * gain
    return out


def loop(data, seconds, low, high):
    data = band(data, low, high)
    length = min(round(seconds * RATE), len(data) - round(.3 * RATE))
    # Use the most even interior section; prompts alone do not ensure a loop.
    best, score = None, float("inf")
    for start in range(round(.15 * RATE), len(data) - length + 1, round(.1 * RATE)):
        candidate = data[start:start + length]
        levels = np.array([np.sqrt(np.mean(part ** 2)) for part in np.array_split(candidate, 12)])
        variation = levels.std() / max(levels.mean(), 1e-8)
        if variation < score:
            best, score = candidate, variation
    if best is None:
        raise ValueError("Loop source is too short")
    count = round(.3 * RATE)
    weight = (.5 - .5 * np.cos(np.linspace(0, np.pi, count)))[:, None]
    seam = best[-count:] * (1 - weight) + best[:count] * weight
    out = np.concatenate([best[count:-count], seam])
    # Rotate an already crossfaded loop to a naturally low-slope sample boundary.
    cut = 1 + np.argmin(np.max(np.abs(np.diff(out, axis=0)), axis=1))
    return np.concatenate([out[cut:], out[:cut]])


def main():
    names = ["engine_drive_loop", "boost_drive_loop", "pd_fire_01", "pd_fire_02", "pd_fire_03"]
    names += [f"{kind}_v{i}" for kind in ["armor_block", "shield_impact", "hull_penetration", "critical_impact"] for i in (2, 3, 4)]
    report_path = ROOT / "flight_manifest.json"
    if report_path.exists() or any((ROOT / (name + ".wav")).exists() for name in names):
        raise FileExistsError("Pack outputs already exist; preserve them before regenerating")
    engine = read("source/engine_drive_20261010")
    boost = read("source/boost_drive_20261010")
    pd = onset(read("source/pd_fire_20261010"))
    metal = onset(read("source/hull_strike_20261010"))
    armor, shield = read("armor_impact"), read("shield_impact")
    rail, penetration, critical = read("railgun_fire"), read("hull_penetration"), read("critical_impact")
    sounds = {
        "engine_drive_loop": loop(engine, 3.6, 32, 1800),
        "boost_drive_loop": loop(boost, 2.8, 45, 4200),
        "pd_fire_01": mix(.19, [(pd, 1, 1, 0, 220, 10000)]),
        "pd_fire_02": mix(.22, [(pd, .89, .9, 0, 350, 7400), (rail, 1.7, .12, .01, 900, 9500)]),
        "pd_fire_03": mix(.16, [(pd, 1.18, 1, 0, 170, 11500)]),
        "armor_block_v2": mix(.32, [(metal, 1.12, 1, 0, 250, 8000)]),
        "armor_block_v3": mix(.36, [(armor, .93, .7, 0, 180, 2800), (metal, 1.4, .6, .014, 1500, 12000)]),
        "armor_block_v4": mix(.27, [(metal, 1.35, .85, 0, 480, 10000), (armor, 1.2, .45, .007, 120, 2000)]),
        "shield_impact_v2": mix(.65, [(shield, 1.16, 1, 0, 240, 9500), (pd, 1.3, .18, .009, 1700, 12000)]),
        "shield_impact_v3": mix(.85, [(shield, .81, 1, 0, 70, 2300), (rail, 1.5, .13, .021, 1200, 7500)]),
        "shield_impact_v4": mix(.5, [(shield, 1.32, 1, 0, 460, 6200), (shield, .76, .18, .035, 80, 1400)]),
        "hull_penetration_v2": mix(.95, [(metal, .87, 1, 0, 45, 9500), (rail, .72, .55, .018, 30, 950)]),
        "hull_penetration_v3": mix(1.15, [(penetration, .83, .65, 0, 40, 2200), (metal, 1.18, .85, .01, 900, 12000)]),
        "hull_penetration_v4": mix(.8, [(metal, 1.02, 1, 0, 80, 6800), (armor, .7, .5, .045, 30, 1200)]),
        "critical_impact_v2": mix(1.4, [(metal, .71, .9, 0, 35, 9000), (rail, .52, .85, .015, 25, 850), (shield, .82, .25, .06, 200, 2300)]),
        "critical_impact_v3": mix(1.7, [(critical, .82, .85, 0, 30, 2600), (metal, 1.03, .8, .017, 800, 10000)]),
        "critical_impact_v4": mix(1.2, [(metal, .88, 1, 0, 45, 6000), (rail, .62, .8, .03, 30, 1250), (armor, 1.13, .4, .11, 550, 6500)]),
    }
    report = {"generator": "Agent Audio / Stable Audio 3 Medium", "backend": "TFLite CPU",
              "runtime_revision": "779434a908193105335fd8d833418603625b2859",
              "model_revision": "da6edc54ddba10bfd79a077102ded687f80e882b", "sources": SOURCES,
              "method": "New engine/boost/PD/metal sources; spectral editing, resampling, layering, fades, crossfaded propulsion loops.",
              "quality_scope": "PCM levels, clipping, loop boundaries and runtime mixer checked; no listening assessment.", "assets": {}}
    for name, data in sounds.items():
        is_loop = name.endswith("_loop")
        if not is_loop:
            envelope = np.minimum(1, np.minimum(np.arange(len(data)) / (.002 * RATE), np.arange(len(data))[::-1] / (.032 * RATE)))
            data *= envelope[:, None]
        data *= (10 ** ((-6 if is_loop else -4) / 20)) / max(np.max(np.abs(data)), 1e-9)
        assert np.isfinite(data).all() and np.sqrt(np.mean(data ** 2)) > .003, name
        pcm = np.round(data * 32767).astype("<i2")
        path = ROOT / (name + ".wav")
        with wave.open(str(path), "wb") as wav:
            wav.setparams((2, 2, RATE, 0, "NONE", "not compressed")); wav.writeframes(pcm.tobytes())
        seam = float(np.max(np.abs(pcm[0].astype(float) - pcm[-1]))) / 32768
        assert not is_loop or seam < .002, (name, seam)
        report["assets"][name] = {"seconds": len(data) / RATE, "sample_rate": RATE, "channels": 2,
            "peak": float(np.max(np.abs(pcm.astype(float)))) / 32768, "rms": float(np.sqrt(np.mean(data ** 2))),
            "clipped_samples": int(np.count_nonzero(np.abs(pcm.astype(float)) >= 32767)),
            "loop": is_loop, "boundary_step": seam, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["assets"], indent=2))


if __name__ == "__main__":
    main()
