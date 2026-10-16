# Open Questions for the Product Owner

> Answers go into `docs/spec/product-requirements.md` (decisions log). Remove a question once answered.

## Product
1. App name and branding (keep "DMX Pro"?), logo, accent color?
2. Who is the main user — DJ / mobile entertainer, church/school, club installer, theatre, home hobbyist?
3. Languages: English only, or also Hindi/Gujarati/other?
4. Free / commercial? Need licensing / activation?

## Hardware
5. Which interface do you own for testing? (Enttec DMX USB Pro, Open DMX / cheap FTDI USB, Art-Net node, sACN node, other — model?)
6. Which fixtures do you own? Please share model + DMX chart (manual page) so we create verified profiles.
7. How many universes do you need (1, 2–4, more)?

## Fixtures / profiles
8. Import QLC+ fixture files (`.qxf`) so the large QLC+ library is usable?
9. Should v1 stay RGB/RGBW only (per spec), or include moving heads later in v1?

## Music
10. Music show: offline (upload + auto-generate timeline), live sound-to-light, or both? Which first?
11. Audio formats: MP3 + WAV enough, or FLAC/M4A too?
12. Styles of auto-generated shows (e.g., Party, Chill, Wedding, Club)? Allow strobe in auto shows?

## Console / input
13. Touchscreen use for the Virtual Console?
14. External controllers: MIDI (which device?), OSC (phone/tablet apps), keyboard shortcuts only?

## Files
15. Project file: single `.dmxpro` JSON file, or a package that also embeds the music files?
16. Need import of QLC+ workspaces (`.qxw`)?
