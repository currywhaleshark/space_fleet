"""Trim generated PCM16 one-shots, apply fades and record reproducible asset checks.

Usage: python tools/prepare-audio.py
Original model outputs in assets/audio/source are never changed.
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import sys
import wave


def read_pcm(path):
    with wave.open(str(path), "rb") as sound:
        channels, rate, width = sound.getnchannels(), sound.getframerate(), sound.getsampwidth()
        frames = sound.getnframes()
        if width != 2 or channels not in (1, 2) or rate <= 0:
            raise ValueError(f"Expected mono/stereo PCM16 WAV: {path}")
        raw = sound.readframes(frames)
    if len(raw) != frames * channels * width or frames == 0:
        raise ValueError(f"Empty/truncated WAV: {path}")
    samples = array("h", raw)
    if sys.byteorder != "little": samples.byteswap()
    peak = max(abs(sample) for sample in samples)
    if peak < 16: raise ValueError(f"Silent or almost silent source: {path}")
    block = max(1, rate // 100) * channels
    energies = [math.sqrt(sum(sample*sample for sample in samples[i:i+block]) / len(samples[i:i+block])) for i in range(0, len(samples), block)]
    threshold = max(energies) * .15
    onset = next(i for i, energy in enumerate(energies) if energy >= threshold) * (block // channels)
    onset = max(0, onset - round(rate * .012))
    return samples, rate, channels, onset, {
        "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "sample_rate": rate, "channels": channels,
        "duration_seconds": frames / rate, "peak": peak / 32768,
        "clipped_samples": sum(sample in (-32768, 32767) for sample in samples), "onset_seconds": onset / rate,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sources", type=Path, default=Path("assets/audio/source"))
    parser.add_argument("--output", type=Path, default=Path("assets/audio"))
    args = parser.parse_args()
    specs = {"armor_impact": ("armor_impact", 1.10), "shield_impact": ("shield_impact", 1.35),
             "railgun_fire": ("railgun_fire", 1.20), "hit_confirm": ("armor_impact", .24)}
    outputs = [args.output / (name + ".wav") for name in specs] + [args.output / "manifest.json"]
    if any(path.exists() for path in outputs):
        raise FileExistsError("Runtime outputs already exist; choose a fresh --output directory")
    sources = {name: read_pcm(args.sources / (name + ".wav")) for name in set(source for source, seconds in specs.values())}
    args.output.mkdir(parents=True, exist_ok=True)
    report = {"generator": "Agent Audio / Stable Audio 3 Medium", "backend": "TFLite CPU",
        "agent_audio_revision": "975d804594f515e81cdb8858587af5469cf78ebf",
        "runtime_revision": "779434a908193105335fd8d833418603625b2859",
        "model_revision": "da6edc54ddba10bfd79a077102ded687f80e882b",
        "sources": {name: data[4] for name, data in sources.items()}, "assets": {},
        "quality_scope": "PCM metadata, signal levels and trimming checked; artistic quality requires listening."}
    for name, (source, seconds) in specs.items():
        samples, rate, channels, onset, metadata = sources[source]
        start = onset * channels
        count = min(round(rate * seconds), len(samples) // channels - onset)
        selected = samples[start:start+count*channels]
        scale = (32767 * 10**(-3/20)) / max(abs(sample) for sample in selected)
        fade_in, fade_out = max(1, round(rate*.002)), max(1, round(rate*.04))
        for frame in range(count):
            envelope = min(1, frame/fade_in, (count-1-frame)/fade_out)
            for channel in range(channels):
                i = frame*channels + channel
                selected[i] = round(selected[i] * scale * envelope)
        peak = max(abs(sample) for sample in selected) / 32768
        rms = math.sqrt(sum(sample*sample for sample in selected) / len(selected)) / 32768
        destination = args.output / (name + ".wav")
        raw = array("h", selected)
        if sys.byteorder != "little": raw.byteswap()
        with wave.open(str(destination), "wb") as sound:
            sound.setparams((channels, 2, rate, 0, "NONE", "not compressed")); sound.writeframes(raw.tobytes())
        report["assets"][name] = {"source": source, "duration_seconds": count/rate, "sample_rate": rate,
            "channels": channels, "peak": peak, "rms": rms, "clipped_samples": 0,
            "sha256": hashlib.sha256(destination.read_bytes()).hexdigest()}
    (args.output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report["assets"], indent=2))


if __name__ == "__main__": main()
