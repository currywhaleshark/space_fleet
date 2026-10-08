"""Derive defense/penetration/critical cues from existing generated WAVs.

No new synthesis or model generation: trim, filter, resample and layer the
committed sounds. Originals stay untouched. Outputs are never overwritten.
"""
from array import array
import hashlib
import json
import math
from pathlib import Path
import sys
import wave


ROOT = Path(__file__).resolve().parents[1] / "assets/audio"


def read(name):
    path = ROOT / (name + ".wav")
    with wave.open(str(path), "rb") as wav:
        assert (wav.getnchannels(), wav.getsampwidth(), wav.getframerate()) == (2, 2, 44100)
        pcm = array("h", wav.readframes(wav.getnframes()))
    if sys.byteorder != "little": pcm.byteswap()
    return [v / 32768 for v in pcm]


def layer(dst, source, speed, gain, delay=0, lowpass=None, highpass=None):
    state = [0., 0.]
    cutoff = lowpass or highpass
    alpha = 1 - math.exp(-2 * math.pi * cutoff / 44100) if cutoff else 1
    for frame in range(len(dst) // 2):
        t = (frame / 44100 - delay) * 44100 * speed
        if t < 0 or t >= len(source) // 2 - 1: continue
        a, fraction = int(t), t % 1
        for channel in range(2):
            value = source[2*a+channel] * (1-fraction) + source[2*(a+1)+channel] * fraction
            state[channel] += alpha * (value - state[channel])
            value = state[channel] if lowpass else value-state[channel] if highpass else value
            dst[frame*2+channel] += value * gain


def main():
    names = ["armor_block", "hull_penetration", "critical_impact"]
    if any((ROOT / (name + ".wav")).exists() for name in names) or (ROOT / "impact_manifest.json").exists():
        raise FileExistsError("Impact outputs already exist; preserve them before regenerating")
    armor, rail, shield = read("armor_impact"), read("railgun_fire"), read("shield_impact")
    specs = {
        "armor_block": (.28, [(armor, 1.18, 1, 0, None, 250)]),
        "hull_penetration": (1.2, [(armor, .88, .75, 0, None, None), (rail, .65, .8, .012, 650, None)]),
        "critical_impact": (1.65, [(armor, .72, .8, 0, None, None), (rail, .58, 1, .01, 850, None),
                                   (shield, .75, .28, .05, 1800, None)]),
    }
    report = {"method": "Derived from existing Agent Audio WAVs by trimming/filtering/resampling/layering; no new generation.",
              "sources": {name: hashlib.sha256((ROOT/(name+".wav")).read_bytes()).hexdigest()
                          for name in ["armor_impact", "railgun_fire", "shield_impact"]}, "assets": {},
              "quality_scope": "Signal and runtime mixer checked; artistic quality requires listening."}
    for name, (seconds, layers) in specs.items():
        count = round(seconds * 44100)
        dst = [0.] * (count * 2)
        for args in layers: layer(dst, *args)
        scale = 10**(-3/20) / max(abs(v) for v in dst)
        pcm = array("h")
        for frame in range(count):
            fade = min(1, frame / 88, (count-1-frame) / 2205)
            for channel in range(2): pcm.append(round(dst[frame*2+channel] * scale * fade * 32767))
        peak = max(abs(v) for v in pcm) / 32768
        rms = math.sqrt(sum(v*v for v in pcm) / len(pcm)) / 32768
        assert .01 < peak < .71 and rms > .001
        clipped = sum(v in (-32768, 32767) for v in pcm)
        if sys.byteorder != "little": pcm.byteswap()
        path = ROOT/(name+".wav")
        with wave.open(str(path), "wb") as wav:
            wav.setparams((2, 2, 44100, 0, "NONE", "not compressed")); wav.writeframes(pcm.tobytes())
        report["assets"][name] = {"seconds": seconds, "sample_rate": 44100, "channels": 2, "peak": peak,
            "rms": rms, "clipped_samples": clipped, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
    (ROOT/"impact_manifest.json").write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(report["assets"], indent=2))


if __name__ == "__main__": main()
