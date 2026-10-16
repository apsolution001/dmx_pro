# Music → Light (Audio Analysis & Auto-Generated Shows)

> Feature requested by the product owner: *upload a music file and make lights follow the music.*

## 0. Primary feature: the Show Timeline (manual, decided 2026-10-16)

The owner builds shows **by hand** on a multi-track timeline (like a video editor / QLC+ "Shows", but easier):

```
 Time ▶   0:00        0:15        0:30        0:45        1:00
 Audio    [███████████ song.mp3 waveform ██████████████████████]
 Beats    |  |  |  |  |  |  |  |  |  |  |  |  |  |  |  |  |  |    ← markers from analysis (snap targets)
 Track 1  [Scene: Blue wash ][Chaser: RGB chase   ][Scene: Red]
 Track 2              [Pulse ×4]        [Flash white]  [Color cycle ...]
```

- **Track types:** Audio (one or more files), Function tracks (any function: scene, chaser, color cycle, pulse…).
- **Add item:** drag an existing function/scene from the library onto a track at any time.
- **Create for a time range:** select a range on a track → "New function here" → pick type (scene, chaser, fade, pulse…)
  → the new function's duration = the selected range; it is saved in the library and reusable.
- **Edit:** move, trim start/end, split, copy/paste, duplicate, delete, per-item fade in/out and intensity.
- **Snap** to beats, bars, grid, or other items' edges. Zoom and scroll; playhead; loop region.
- **Playback** uses the audio clock (see §4); scrubbing recomputes the light state instantly.
- **Overlaps:** items on different tracks run together and merge with HTP/LTP rules; lower track = higher priority for LTP (documented in UI).
- Auto-generation (§3) becomes an optional helper ("Fill selection with beat-synced chase"), not the core.

## 1. Two different modes

| Mode                            | Input                          | How                                                       | Latency |
|---------------------------------|--------------------------------|-----------------------------------------------------------|---------|
| **A. Music Show (offline)**     | MP3/WAV/FLAC file uploaded     | Analyze whole track first → generate an editable **timeline** of cues → play in sync | ~0 (pre-computed, offset-compensated) |
| **B. Live sound-to-light**      | System audio (loopback) or mic | Real-time FFT + beat detection → trigger functions / modulate intensity | 20–80 ms |

Mode A gives the best result ("perfect" sync, editable). Mode B is for DJs/unknown music.

## 2. Analysis pipeline (offline)

```
Decode audio (NAudio / MediaFoundation) → mono float, 44.1 kHz
  → STFT (FFT 2048, hop 512)
  → Band energies: Bass (20–150 Hz), Low-mid (150–500), Mid (500–2k), High (2k–16k)
  → Onset strength (spectral flux, half-wave rectified)
  → Tempo estimate (autocorrelation / comb filter of onset envelope, 60–200 BPM)
  → Beat tracking (dynamic programming, Ellis 2007) → beat times, downbeats (bar = 4 beats)
  → Loudness envelope (RMS, smoothed) → energy level per bar (low / medium / high / peak)
  → Section detection (self-similarity / novelty curve) → intro, verse, chorus, drop, break, outro
  → Store as AnalysisResult (cached next to project, keyed by file hash)
```

## 3. Auto show generation (rule-based, editable)

Inputs: analysis + selected fixtures/groups + palette + **style** (Chill / Party / Club / Wedding …).

| Music event         | Default light reaction                                          |
|---------------------|-----------------------------------------------------------------|
| Each beat           | Step a color chase / pulse intensity on bass groups             |
| Downbeat (bar start)| Change scene / color                                            |
| Energy low          | Slow fades, warm/soft colors, lower intensity                   |
| Energy high         | Faster chases, saturated colors, full intensity                 |
| Drop / peak         | Flash full white (strobe only if user allows), then high-energy chase |
| Break / silence     | Fade to dim ambient / blackout                                  |
| Section change      | Switch palette                                                  |

The result is a normal **Show timeline**: tracks of function cues. The user can move, delete, or replace cues,
and everything saves in the project. **Generated = starting point, never a black box.**

## 4. Playback sync

- Use the **audio playback clock** (NAudio `WaveOutEvent`/WASAPI position) as master; the DMX engine reads it each tick.
- **Latency offset** setting (ms) to compensate audio-device and DMX/network latency.
- Seek/scrub on the timeline must instantly recompute the light state at that time.

## 5. Live mode details

- Capture: `WasapiLoopbackCapture` (whatever the PC plays) or microphone.
- Real-time beat detector: bass-band energy vs. running average with adaptive threshold + refractory period.
- Outputs: beat events, band levels (0–1), BPM. Mappable in Virtual Console (like QLC+ *Audio Triggers*).
- **Tap tempo** button as manual fallback.

## 6. Libraries (.NET 8)

| Need                 | Library                                         |
|----------------------|-------------------------------------------------|
| Decode / play / capture | **NAudio** (MIT)                             |
| FFT                  | NAudio.Dsp `FastFourierTransform` or **MathNet.Numerics** (MIT) / FftSharp (MIT) |
| Waveform UI          | Custom WPF control drawing a downsampled peak array |

## 7. Safety

- Auto-generated shows must respect **max strobe rate** and a global "no strobe" switch (photosensitive epilepsy).
- Respect Grand Master and Blackout at all times.
