# From IP to XLR — Art-Net, sACN, and DMX Nodes

> How a PC sends DMX over Ethernet/Wi-Fi and a box turns it into the XLR signal that lights understand.

## 1. The chain

```
 ┌─────────────┐  UDP/IP   ┌──────────────────────┐  RS-485 / XLR  ┌────────┐   ┌────────┐
 │  DMX Pro    │──────────▶│  DMX node / gateway   │──────────────▶│ PAR #1 │──▶│ PAR #2 │──▶ [120Ω]
 │ (Windows PC)│ Ethernet  │ (Art-Net or sACN in,  │   DMX512      └────────┘   └────────┘
 └─────────────┘  / Wi-Fi  │  1–N XLR ports out)   │
                           └──────────────────────┘
```

A **node** (also called gateway, Art-Net box, Ethernet-DMX converter) has a network port and one or more XLR outputs.
Each XLR port is configured to listen to one universe. The node:
1. Receives UDP packets containing 512 channel values for a universe.
2. Buffers the latest values.
3. Continuously re-transmits them as a DMX512 frame (BREAK, MAB, start code, slots) on the XLR port.

**Alternative:** a **USB-DMX interface** does the same from USB instead of network (see `04-usb-dmx-interfaces.md`).

## 2. Art-Net (Artistic Licence, free to use)

| Item              | Value                                                                  |
|-------------------|------------------------------------------------------------------------|
| Transport         | UDP, **port 6454** (both directions)                                   |
| Versions          | Art-Net 4 is current; packets are backwards compatible                 |
| Universe address  | 15-bit **Port-Address** = Net (7 bit) · Sub-Net (4 bit) · Universe (4 bit) → 0–32767 |
| Typical IP ranges | `2.x.x.x` or `10.x.x.x` (classic), any LAN subnet in practice          |
| Delivery          | Unicast to discovered nodes (preferred) or subnet broadcast            |
| Discovery         | `ArtPoll` (OpCode `0x2000`) → nodes reply `ArtPollReply` (`0x2100`)    |

### ArtDmx packet (OpCode `0x5000`)

| Offset | Size | Field        | Notes                                               |
|--------|------|--------------|-----------------------------------------------------|
| 0      | 8    | ID           | ASCII `"Art-Net"` + `0x00`                          |
| 8      | 2    | OpCode       | `0x5000`, **little-endian** → bytes `00 50`          |
| 10     | 2    | ProtVer      | 14, **big-endian** → bytes `00 0E`                   |
| 12     | 1    | Sequence     | 1–255 rolling, 0 = disabled                         |
| 13     | 1    | Physical     | Input port (informational)                          |
| 14     | 1    | SubUni       | Low byte of Port-Address (Sub-Net << 4 | Universe)  |
| 15     | 1    | Net          | High 7 bits of Port-Address                         |
| 16     | 2    | Length       | Data length, **big-endian**, even, 2–512            |
| 18     | N    | Data         | Channel values (channel 1 at offset 18)             |

Rules: re-send data continuously (refresh) even if unchanged; don't exceed ~44 packets/s per universe.

## 3. sACN — Streaming ACN (ANSI E1.31)

| Item              | Value                                                                      |
|-------------------|----------------------------------------------------------------------------|
| Transport         | UDP, **port 5568**                                                         |
| Universe range    | **1–63999**                                                                |
| Delivery          | **Multicast** to `239.255.{universe high byte}.{universe low byte}` or unicast |
| Priority          | 0–200 per source (default 100); receivers take the highest-priority source |
| Source ID         | CID = UUID (stable per app install), plus a human-readable source name      |
| Packet            | Root layer → Framing layer → DMP layer; 638 bytes for a full universe       |
| Stop              | Send *Stream_Terminated* option bit (3 packets) when output is disabled     |

sACN is preferred in larger/professional networks (multicast, priorities, merging between consoles).

## 4. Art-Net vs sACN — which to use?

| Question                                 | Art-Net                 | sACN                       |
|------------------------------------------|-------------------------|----------------------------|
| Cheap Chinese nodes / most budget boxes  | ✅ almost always         | ✅ usually                  |
| Node discovery built-in                  | ✅ ArtPoll               | ❌ (needs E1.33 / manual)   |
| Multiple consoles with priority          | ⚠️ merge only            | ✅ priority                 |
| Wi-Fi friendliness                       | Unicast works well      | Multicast can be unreliable on Wi-Fi |

**DMX Pro v1 plan:** Art-Net unicast/broadcast + sACN unicast/multicast; each implemented and tested end-to-end.

## 5. Network setup tips (for docs / help screen)

- Put PC and node on the same subnet (e.g., PC `192.168.1.10`, node `192.168.1.50`, mask `255.255.255.0`).
- Prefer **wired Ethernet**; Wi-Fi adds jitter and drops multicast.
- Windows Firewall may block UDP 6454/5568 — the app should detect and explain this.
- Let the user choose the **network adapter** to send from (laptops often have several).
- Show packets/second and last error in the I/O tab.

## 6. Implementation notes for .NET

- `System.Net.Sockets.UdpClient` / `Socket` with `SendTo`; enable broadcast for Art-Net broadcast.
- For multicast (sACN), set `MulticastInterface` to the selected adapter and TTL.
- Build packets into pre-allocated `byte[]` buffers — no allocations per frame.
- Send on a **dedicated output thread** at a fixed rate (e.g., 40 Hz) using a high-resolution timer, not the UI thread.
