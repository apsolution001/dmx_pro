# DMX512 Fundamentals

> Reference for developers and AI agents working on DMX Pro. Facts here are based on ANSI E1.11 (DMX512-A).
> Always confirm a fixture's behavior against **its own manual / DMX chart**.

## 1. What is DMX?

**DMX512** (Digital Multiplex, 512 channels) is the standard control protocol for stage and architectural lighting.
A controller (our app + an interface) continuously sends a stream of numbers; each light listens to a few of them.

- One **universe** = up to **512 channels** (also called *slots* or *addresses*).
- Every channel carries one **8-bit value: 0–255**.
- The controller sends the **whole universe over and over** (typically 30–44 times per second), even if nothing changed.
- DMX is **one-way**: the controller talks, fixtures listen. (RDM, ANSI E1.20, adds a return path — out of scope for v1.)
- A fixture has a **start address** and uses **N consecutive channels** (its *footprint*).
  A fixture with 8 channels at address 101 occupies **101–108**.

```
Universe 1 (512 channels)
┌─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬───── ... ─────┐
│  1  │  2  │  3  │  4  │  5  │  6  │  7  │  8  │  9  │ 10  │          512   │
└─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴───── ... ─────┘
 └──────── PAR #1 (addr 1, 8ch) ───────┘ └─ PAR #2 (addr 9, 4ch) ┘
```

## 2. Channel meaning comes from the fixture, not from DMX

DMX itself knows nothing about colors. Channel 1 might be *Red* on one light and *Dimmer* on another.
The **fixture profile** (from the manufacturer's DMX chart and selected **DMX mode**) defines what each channel does.

Typical RGBW PAR 8-channel mode (**example only — not universal**):

| Ch | Function       | Values                                 |
|----|----------------|----------------------------------------|
| 1  | Master dimmer  | 0–255 = 0–100 %                        |
| 2  | Red            | 0–255                                  |
| 3  | Green          | 0–255                                  |
| 4  | Blue           | 0–255                                  |
| 5  | White          | 0–255                                  |
| 6  | Strobe         | 0–9 off, 10–255 slow → fast            |
| 7  | Mode / macro   | 0–50 manual, 51–100 color jump, …      |
| 8  | Speed          | 0–255 (for built-in programs)          |

Not every channel is linear brightness: *macro*, *mode*, and *strobe* channels use **ranges** that select behaviors.

## 3. Physical layer (the XLR cable)

| Property            | Value                                                                 |
|---------------------|-----------------------------------------------------------------------|
| Electrical          | **RS-485** differential pair                                          |
| Bit rate            | **250 kbit/s**, 1 start bit, 8 data bits, **2 stop bits**, no parity  |
| Connector (standard)| **5-pin XLR** (pin 1 common, pin 2 Data−, pin 3 Data+, 4/5 spare)     |
| Connector (common)  | **3-pin XLR** on most budget LED fixtures (1 common, 2 Data−, 3 Data+)|
| Cable               | **120 Ω data cable** (not microphone cable — causes reflections/flicker) |
| Topology            | **Daisy chain**: controller → DMX IN → DMX OUT → next fixture …       |
| Termination         | **120 Ω terminator** on the DMX OUT of the last fixture               |
| Max devices / line  | **32 unit loads** per segment (use an opto-splitter for more/branches)|
| Practical length    | ~300 m (1000 ft) with good cable                                      |

**Never "Y-split" DMX with a plain cable.** Use an opto-isolated splitter/buffer.

## 4. Packet timing (one frame)

```
 idle │ BREAK │ MAB │ Start code │ Slot 1 │ Slot 2 │ … │ Slot 512 │ idle
 (hi) │ ≥88µs │≥8µs │   0x00     │ 44 µs  │ 44 µs  │   │  44 µs   │
```

- **BREAK**: line held low ≥ 88 µs (transmitters typically ~100–176 µs).
- **MAB** (Mark After Break): ≥ 8 µs (E1.11 transmitters ≥ 12 µs).
- **Start code** `0x00` = normal dimmer data. Other start codes (e.g., `0xCC` RDM) must be ignored by plain fixtures.
- Each slot = 11 bits × 4 µs = **44 µs**.
- Full 512-slot frame ≈ 22.7 ms → **max ≈ 44 Hz refresh**. Shorter frames can refresh faster.
- Many interfaces send fewer than 512 slots; fixtures only need data up to their highest address.

## 5. What happens when DMX stops?

Fixture-dependent! Many hold the **last received values**; some black out; some go to a built-in program.
This is why the spec separates **Blackout** (we *send* zeros/dark state) from **Output disabled** (we *stop sending*).

## 6. 8-bit vs 16-bit channels

Some parameters use two channels (coarse + fine) for 0–65535 resolution (common on moving heads / pan-tilt,
sometimes on high-end dimmers). v1 is RGB/RGBW, mostly 8-bit — but the profile model should allow `fine` channels.

## 7. Merging rules (when two things control the same channel)

| Rule | Meaning                         | Typical use                         |
|------|---------------------------------|-------------------------------------|
| HTP  | **Highest Takes Precedence**    | Intensity / dimmer channels         |
| LTP  | **Latest Takes Precedence**     | Color, strobe, mode, everything else|

QLC+ and most consoles use HTP for intensity and LTP for the rest. DMX Pro's engine should do the same.

## 8. Universe numbering

- Wired DMX: one physical port = one universe.
- Art-Net counts universes from **0** (Port-Address 0–32767).
- sACN counts universes from **1** (1–63999).
- In the UI, always show **"Universe 1"** as the first universe and map it to the protocol's numbering in the output layer.

## 9. Practical safety notes

- Start with **output disabled**; enable deliberately.
- Test new fixtures at **low intensity**.
- Strobe channels can trigger photosensitive epilepsy — warn and avoid strobe in auto-generated music shows unless allowed.
- Keep a **known safe scene** (blackout / house lights) one click away.
