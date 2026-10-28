# Open Questions for the Product Owner

> Answered so far: target user, hardware (all three output types), QLC+ `.qxf` import, music = manual Show Timeline.

> Answers go into `docs/spec/product-requirements.md` (decisions log). Remove a question once answered.

## Product
1. App name and branding (keep "DMX Pro"?), logo, accent color?
3. Languages: English only, or also Hindi/Gujarati/other?
4. Free / commercial? Need licensing / activation?

## Hardware
6. Which fixtures do you own? Please share model + DMX chart (manual page) so we create verified profiles.
7. How many universes do you need (1, 2–4, more)?

## Fixtures / profiles
9. Should v1 stay RGB/RGBW only (per spec), or include moving heads later in v1?

## Channel layouts (added 2026-10-19)
25. Should the layout editor / Add Fixture have a **Delete custom layout** button (`ProfileLibraryService.DeleteUserProfileAsync` exists, no UI yet)? What happens to lights that still use it?
26. After switching lights to a new channel layout, should the app check scene (and other function) values that were set for the old channel order and warn or remap them? (Today: `ProfileUpdateResult.FunctionIssues` are reported, values are not remapped.)

## Music
11. ~~Audio formats~~ **ANSWERED 2026-10-21:** MP3, WAV, M4A, AAC, FLAC (decisions log).
12. Styles of auto-generated shows (e.g., Party, Chill, Wedding, Club)? Allow strobe in auto shows?

## Console / input
13. Touchscreen use for the Virtual Console?
14. External controllers: MIDI (which device?), OSC (phone/tablet apps), keyboard shortcuts only?

## Files
15. ~~Project file / music files~~ **ANSWERED 2026-10-21:** the project links the music files; "Collect music" copies them into a `Music` folder next to the project (decisions log).
16. Need import of QLC+ workspaces (`.qxw`)?

## Virtual Console (added 2026-10-17)
17. A Speed control with no function selected: should it change the speed of **all** running functions (global speed / tap tempo)? (Current behaviour: yes — effective speed = per-function speed × global speed, each 0.25–4×.)
18. Keyboard shortcuts on console widgets: only for the **active page**, or for all pages? (Current assumption: active page only.)

## Functions & shows (added 2026-10-18)
19. ~~Chaser placed in a show~~ answered 2026-10-27 (decisions log): placing a chaser deep-copies the chaser and its owned step functions. (The fade part is void: fades were removed 2026-10-28.)
20. Strobe hardware rate: should profiles carry a value → Hz calibration for strobe channels so the max strobe rate can also limit hardware strobe? (Today only chasers' step speed is rate-limited.)
21. ~~Fast pulses/chasers and the strobe limit~~ answered 2026-10-28 (decisions log): chasers are capped by the max strobe rate.
22. ~~Effects targeting a group~~ void: the effect types were removed 2026-10-28.
23. Chaser / sequence run order "Random": needed? (Today: Loop, Single shot and Ping-pong, each Forward or Backward.)
24. Art-Net per-universe Port-Address override: two nodes that both listen on Art-Net universe 0 are currently blocked (one Art-Net universe per project universe). Allow the same Art-Net universe on two outputs?

## Test & Preview (added 2026-10-19)
27. Should "Output is disabled" in the Test page warnings strip be red (error) instead of amber (warning)? (Today: amber.)
28. Should quick R/G/B/W-only buttons and a safe-intensity colour sequence be added next? (Not in the first Test page version.)

## Virtual Stage (added 2026-10-21)
29. Should lights on the stage have an editable **rotation** (the project file already stores `rotation`, the UI does not edit it)?
30. A **3D stage** view later (truss height, beam cones)? Or stay 2D?
31. ~~**Undo / redo** for stage moves?~~ Answered 2026-10-23: every project edit is undoable, stage moves included (`architecture.md` §10).
