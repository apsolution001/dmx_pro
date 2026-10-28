# Safety Rules (non-negotiable)

These come from spec §1 and §12. Code reviews must reject changes that violate them.

1. **Output starts disabled** on every app start and every project open. The enabled state is never persisted.
2. Output cannot be enabled without a valid output configuration.
3. **Blackout ≠ Output disabled.** Blackout sends a dark state; disabling stops transmission. Keep them separate in code and UI.
4. **Release All** stops all functions deterministically and returns channels to profile defaults.
5. Flash/momentary actions must release on button-up, input disconnect, focus moving to another control or page inside
   the app, and application close. Switching to another program does **not** release them: every light, held flashes
   included, stays as it is (owner decision 2026-10-22).
6. On application close: stop functions → send one safe frame (configurable: blackout or defaults) → stop output.
7. Never present an unknown channel meaning as confirmed: channels without a known meaning (`Generic`) are shown as
   raw channels only. Checking a profile against the real light is the owner's job (done in QLC+ before patching), so
   the app shows no "unverified profile" warnings (decision 2026-10-22).
8. Strobe: respect the global "allow strobe" setting and max strobe rate (chasers are capped too: step changes never faster than 1/(2 x max rate), and at most 3 changes per second while strobe is not allowed —
   the photosensitive-seizure threshold; enforced at play time by `ChaserSpeedLimit`, stored times are unchanged); auto-generated music shows default to no strobe.
9. Values are always clamped to 0–255 before reaching any output.
10. Hardware/network errors are shown visibly (status pill + message), never only logged.
