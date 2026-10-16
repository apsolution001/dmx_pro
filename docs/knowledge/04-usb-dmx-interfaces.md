# USB-DMX Interfaces

> USB → XLR. The interface converts USB data from the PC into a timed DMX512 signal.

## 1. Two families

| Type                         | Examples                                         | How it works                                             | Reliability |
|------------------------------|--------------------------------------------------|----------------------------------------------------------|-------------|
| **"Intelligent" (has MCU)**  | Enttec **DMX USB Pro / Pro Mk2**, DMXking ultraDMX Pro, many clones | PC sends a framed message; the interface's microcontroller generates DMX timing itself | ✅ Excellent |
| **"Dumb" FTDI (Open DMX)**   | Enttec **Open DMX USB**, cheap FT232R + RS-485 dongles | PC must generate BREAK/MAB and stream bytes itself via the FTDI chip | ⚠️ Jitter under Windows load, may flicker |

Both usually appear as an **FTDI** device (VID `0x0403`, PID `0x6001`). Drivers: FTDI **D2XX** (direct) or **VCP** (virtual COM port).

## 2. Enttec DMX USB Pro protocol (widely cloned)

Message frame:

```
0x7E | Label | Length LSB | Length MSB | Data (Length bytes) | 0xE7
```

| Label | Meaning                               |
|-------|---------------------------------------|
| 3     | Get widget parameters (firmware, timing) |
| 6     | **Output Only Send DMX Packet**: data = start code `0x00` + up to 512 channels |
| 8     | Receive DMX on change (input)         |
| 10    | Get serial number                     |

Example — send 512 channels: `7E 06 01 02 00 <512 bytes> E7` (length = 513 = `0x0201`).

Via VCP: open the COM port (baud rate is ignored by the Pro, use 115200/57600), write frames at ~40 Hz.

## 3. Open DMX USB (dumb FTDI)

Configure the FTDI chip via D2XX:
- Baud **250000**, 8 data bits, **2 stop bits**, no parity, no flow control.
- Each frame: `SetBreakOn` → wait ≥ 100 µs → `SetBreakOff` → wait ≥ 12 µs → write `0x00` + 512 bytes.
- Needs a dedicated high-priority thread; Windows timer resolution makes timing imperfect.

## 4. Recommendation for DMX Pro

1. **Phase 1 output:** Art-Net/sACN (pure software, easy to test with free tools) **and** Enttec DMX USB Pro (serial).
2. **Phase 2:** Open DMX (FTDI D2XX) as "best effort" with a warning.
3. Show device status: *Not found / Connected / Sending (fps) / Error (message)*.
4. Hot-plug: detect USB removal, switch state to *Error*, retry when re-plugged.

## 5. Test tools (no hardware needed)

- **Art-Net/sACN viewers:** e.g., "sACNView" (open source), Art-Net monitors like DMX Workshop — show received universes.
- **QLC+** itself can act as receiver (Art-Net input → Simple Desk) to verify our packets.
- **Wireshark** has Art-Net and sACN dissectors.
- An internal **Loopback / Virtual output** in DMX Pro for unit and UI testing.
