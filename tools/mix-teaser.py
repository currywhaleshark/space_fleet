"""Edit the existing game SFX into an original teaser sound design; no new model-generated audio."""
import argparse
import json
import math
import wave
from pathlib import Path

import numpy as np

RATE = 48000


def read_wave(path):
    with wave.open(str(path), "rb") as source:
        if source.getsampwidth() != 2:
            raise ValueError(f"Expected 16-bit PCM: {path}")
        data = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2").astype(np.float64) / 32768
        data = data.reshape(-1, source.getnchannels())
        if data.shape[1] == 1:
            data = np.repeat(data, 2, axis=1)
        rate = source.getframerate()
    return data, rate


def resample(data, ratio):
    x = np.arange(int(len(data) / ratio)) * ratio
    return np.column_stack([np.interp(x, np.arange(len(data)), data[:, c]) for c in range(2)])


def band(data, low, high):
    f = np.fft.rfftfreq(len(data), 1 / RATE)
    weight = np.minimum(1, (f / max(low, 1)) ** 4) / (1 + (f / high) ** 6)
    weight[f < 18] = 0
    return np.fft.irfft(np.fft.rfft(data, axis=0) * weight[:, None], n=len(data), axis=0)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cues", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    plan = json.loads(args.cues.read_text(encoding="utf-8"))
    duration = plan["duration"]
    mix = np.zeros((int(duration * RATE), 2), np.float64)
    sources = {}
    for path in args.assets.glob("*.wav"):
        data, rate = read_wave(path)
        sources[path.stem] = resample(data, rate / RATE)

    def put(data, time, gain=1, pan=0):
        first = round(time * RATE)
        if first >= len(mix):
            return
        length = min(len(data), len(mix) - first)
        stereo = np.array([min(1, 1 - pan), min(1, 1 + pan)])
        mix[first:first + length] += data[:length] * gain * stereo

    # Long, low mechanical texture, made from the existing shield resonance.
    texture = band(resample(sources["shield_impact"], .16), 35, 360)
    texture /= max(np.max(np.abs(texture)), .001)
    texture *= np.sin(np.linspace(0, np.pi, len(texture)))[:, None] ** 2
    for i, time in enumerate(np.arange(0, duration, max(1, len(texture) / RATE * .5))):
        put(texture[:, ::-1] if i % 2 else texture, float(time), .045)

    # Sparse mechanical pulse increases through the approach, then leaves space for the final hit.
    pulse = band(resample(sources["armor_block"], .45), 40, 480)
    pulse /= max(np.max(np.abs(pulse)), .001)
    hit_time = next((c["time"] for c in plan["cues"] if c["sound"] == "critical_impact"), 27)
    for time in np.arange(1.0, hit_time, .75):
        gain = .055 if time < 11 else .085 if time < 22 else .11
        put(pulse, float(time), gain, .12 * math.sin(time))

    # Swells are reversed, filtered game impacts, not a generated music track.
    swell = band(resample(sources["hull_penetration"], .5), 100, 3500)[::-1].copy()
    swell *= np.linspace(0, 1, len(swell))[:, None] ** 2
    cuts = np.cumsum(plan.get("durations", [6, 5, 6, 5, 5, 5, 6]))[:-1]
    for cut in cuts:
        put(swell, max(0, cut - len(swell) / RATE), .2)
    # Build into actual hull contact while leaving the continuous tracking shot uncut.
    put(swell, max(0, hit_time - len(swell) / RATE), .3)

    for cue in plan["cues"]:
        data = resample(sources[cue["sound"]], cue["pitch"])
        data[:min(240, len(data))] *= np.linspace(0, 1, min(240, len(data)))[:, None]
        put(data, cue["time"], cue["gain"] * .8, cue["pan"])
        # Quiet stereo echoes preserve a dry main transient.
        put(data, cue["time"] + .19, cue["gain"] * .07, -cue["pan"])
        put(data, cue["time"] + .37, cue["gain"] * .025, cue["pan"])
    put(resample(sources["critical_impact"], .48), 32, .6)

    mix = np.tanh(mix * 1.12)
    fade = int(RATE * 1.1)
    mix[:fade] *= np.linspace(0, 1, fade)[:, None]
    mix[-fade:] *= np.linspace(1, 0, fade)[:, None]
    peak = float(np.max(np.abs(mix)))
    mix *= .82 / max(peak, .001)
    if not np.isfinite(mix).all():
        raise ValueError("Non-finite audio samples")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(args.output), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes((mix * 32767).astype("<i2").tobytes())
    report = {"duration": duration, "sample_rate": RATE, "channels": 2,
              "peak_dbfs": float(20 * np.log10(np.max(np.abs(mix)))),
              "rms_dbfs": float(20 * np.log10(np.sqrt(np.mean(mix ** 2)))),
              "sound_cues": len(plan["cues"]), "source": "Edited existing game SFX; no new AI audio generation"}
    args.output.with_suffix(".json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
