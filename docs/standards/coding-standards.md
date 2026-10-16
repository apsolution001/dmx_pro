# Coding Standards (C# / .NET 8)

## Language & project settings

- `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<LangVersion>latest</LangVersion>`. Common settings live in `Directory.Build.props`.
- Package versions centralized in `Directory.Packages.props` (Central Package Management).
- File-scoped namespaces; one public type per file; file name = type name.

## Naming

| Element                    | Style                 | Example                     |
|----------------------------|-----------------------|-----------------------------|
| Types, methods, properties | PascalCase            | `FixtureProfile`, `Start()` |
| Interfaces                 | `I` + PascalCase      | `IDmxOutput`                |
| Private fields             | `_camelCase`          | `_universeBuffer`           |
| Locals / parameters        | camelCase             | `startAddress`              |
| Constants                  | PascalCase            | `MaxChannels`               |
| Async methods              | suffix `Async`        | `LoadProjectAsync`          |

## Domain rules (DMX-specific)

- DMX addresses in the domain/UI are **1-based (1–512)**; array indexes are 0-based — convert only at the buffer boundary,
  in one helper (`DmxAddress.ToIndex`). Never sprinkle `- 1` around.
- DMX values are `byte`. Intensity math uses `float` 0..1 internally and converts with rounding + clamping once.
- **Never hardcode channel offsets** for colors/dimmer/strobe. Resolve via the profile (`mode.FindChannel(ChannelType.Red)`).
- Universes in the UI start at 1; protocol mapping (Art-Net 0-based, sACN 1-based) lives only in the Output layer.
- Time in the engine: `TimeSpan` or `double` seconds from a monotonic clock (`Stopwatch`), never `DateTime.Now`.

## Architecture rules

- `Core`, `Engine`, `Output` **must not reference WPF** (`PresentationFramework`, `System.Windows`).
- The engine owns DMX buffers. UI interacts via commands/services only.
- No allocations in the per-frame hot path (engine tick, output send): reuse buffers, avoid LINQ there.
- Output implementations never throw into the engine; they report status via events.
- All I/O is async; never block the UI thread (`.Result` / `.Wait()` forbidden in App).
- Use DI (constructor injection). No service locator, no static mutable state.

## Error handling & logging

- Validate at boundaries (file load, user input, hardware). Use result types/validation lists for user-facing errors.
- Log with `ILogger<T>` and structured templates: `_logger.LogWarning("Universe {Universe} send failed: {Error}", u, ex.Message)`.
- Never swallow exceptions silently.

## Tests

- xUnit. Test name: `Method_State_ExpectedResult`.
- Every bug fix gets a regression test. Engine/Output/profile logic requires tests.
- Output protocol tests compare **exact bytes** of packets.
- Audio analysis tests use synthetic signals (click track at known BPM).

## Comments & docs

- XML docs on public APIs of Core/Engine/Output.
- Comments explain *why*, not *what*.
- When behavior differs from the spec, note it in `docs/spec/product-requirements.md` decisions log.
