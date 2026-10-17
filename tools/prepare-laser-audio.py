"""Crossfade a generated steady laser source into a bounded, click-free game loop."""
from pathlib import Path
import hashlib
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
source = ROOT / 'assets/audio/laser_emitter_source.wav'
output = ROOT / 'assets/audio/laser_emitter_loop.wav'
with wave.open(str(source)) as w:
    rate, channels = w.getframerate(), w.getnchannels()
    assert w.getsampwidth() == 2
    data = np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').reshape(-1, channels).astype(np.float64) / 32768
assert np.isfinite(data).all() and np.max(np.abs(data)) > .001
# Remove generator attack/tail, DC and form a circular 100 ms equal-gain overlap.
data = data[int(rate*.35):int(rate*2.7)]
data -= np.mean(data, axis=0)
fade = int(rate*.1)
t = np.linspace(0, 1, fade)[:, None]
joined = data[-fade:]*(1-t) + data[:fade]*t
data = np.concatenate((data[fade:-fade], joined))
data *= .65 / np.max(np.abs(data))
pcm = np.rint(data*32767).astype('<i2')
with wave.open(str(output), 'wb') as w:
    w.setnchannels(channels); w.setsampwidth(2); w.setframerate(rate); w.writeframes(pcm.tobytes())
report = {'source':'Agent Audio / Stable Audio 3 Medium; portable tflite backend',
    'prompt':'Continuous sustained high energy ship laser emitter machinery. Dense electrical hum, focused sizzling high frequency beam with a restrained low mechanical resonance, steady intensity, dry close onboard structure vibration. No voices, no music, no rhythmic pulses, no explosions. Three seconds steady sound suitable for looping.',
    'sourcePath':str(source.relative_to(ROOT)), 'outputPath':str(output.relative_to(ROOT)),
    'seconds':len(data)/rate, 'sampleRate':rate, 'channels':channels,
    'peak':float(np.max(np.abs(data))), 'rms':float(np.sqrt(np.mean(data**2))),
    'boundaryStep':float(np.max(np.abs(data[0]-data[-1]))),
    'clippedSamples':int(np.count_nonzero(np.abs(pcm.astype(np.int32))>=32767)),
    'sha256':hashlib.sha256(output.read_bytes()).hexdigest(),
    'verification':'Numerical waveform/loop checks; subjective listening not performed.'}
(ROOT/'assets/audio/laser_manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
