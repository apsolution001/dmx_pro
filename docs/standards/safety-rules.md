# Safety Rules (non-negotiable)

These come from spec §1 and §12. Code reviews must reject changes that violate them.

1. **Output starts disabled** on every app start and every project open. The enabled state is never persisted.
2. Output cannot be enabled without a valid output configuration.
3. **Blackout ≠ Output disabled.** Blackout sends a dark state; disabling stops transmission. Keep them separate in code and UI.
4. **Release All** stops all functions deterministically and returns channels to profile defaults.
5. Flash/momentary actions must release on button-up, input disconnect, focus loss, and application close.
6. On application close: stop functions → send one safe frame (configurable: blackout or defaults) → stop output.
7. Never present an unknown/unverified channel meaning as confirmed.
8. Strobe: respect the global "allow strobe" setting and max strobe rate; auto-generated music shows default to no strobe.
9. Values are always clamped to 0–255 before reaching any output.
10. Hardware/network errors are shown visibly (status pill + message), never only logged.
