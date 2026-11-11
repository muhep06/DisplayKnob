# Contributing to DisplayKnob

Thanks for your interest! Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) first: it explains the design and the hardware/Win32 traps that shape the code.

## Build and test

Requirements: Windows 10 22H2+ or Windows 11, the .NET 10 SDK (version pinned in `global.json`). Visual Studio 2026 is optional; the `dotnet` CLI is enough.

```
dotnet build
dotnet test
```

Run the app from `src/DisplayKnob.App` (or start `DisplayKnob.exe` from its `bin` folder). It has no main window; look for the tray icon. Logs go to `%LocalAppData%\DisplayKnob\logs`.

Real DDC/CI behavior can only be verified on real monitors. If your change touches DDC, describe the monitors you tested with in the PR.

## Branches

`main` is always releasable and is only changed through pull requests. There is no `develop` branch.

Work happens on short-lived branches cut from `main`, named after the Conventional Commit type:

| Prefix | Use for | Example |
|---|---|---|
| `feat/` | a new feature | `feat/multi-monitor-volume-group` |
| `fix/` | a bug fix | `fix/ddc-command-spacing` |
| `docs/` | documentation only | `docs/troubleshooting-spacing` |
| `chore/` | CI, dependencies, maintenance | `chore/update-velopack` |

- Use lowercase words separated by hyphens. Delete the branch after it is merged.
- PRs are **squash-merged**. The PR title becomes the commit on `main`, so write it as a Conventional Commit (`fix(ddc): ...`).
- A release is a `vX.Y.Z` tag on a green `main`; the Release workflow builds the installer from it.
- If a released version needs a fix while `main` has moved on, cut `release/X.Y` from its tag, fix there, and tag a patch release. Don't create these branches ahead of time.

## Pull requests

- Keep PRs small and focused. Open an issue first for larger changes.
- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `feat(ddc): ...`, `fix(audio): ...`, `docs: ...`, `chore: ...`.
- `dotnet build` must pass with no warnings in `DisplayKnob.Core` (warnings are errors there) and `dotnet test` must pass.
- Don't swap a part of the tech stack or add a NuGet package without discussing it in an issue first.

## Coding standards

Baseline: the .NET runtime coding style, enforced by `.editorconfig` and analyzers. The rules below add what matters for this project.

### Project settings
- Nullable enabled, `LangVersion` latest, implicit usings (set in `Directory.Build.props`).
- `DisplayKnob.Core`: warnings are errors, `AnalysisLevel=latest-recommended`. Don't suppress a warning without a one-line justification comment.
- File-scoped namespaces. Namespace matches folder (`DisplayKnob.Core.Ddc`).
- One public type per file; file name = type name.

### Naming
- `PascalCase`: types, methods, properties, events, constants, enum members.
- `camelCase`: locals, parameters. `_camelCase`: private fields. `s_camelCase`: private static fields.
- Interfaces start with `I`. Async methods end with `Async`.
- No Hungarian notation, no abbreviations except well-known ones (`Ddc`, `Vcp`, `Edid`, `Id`).

### Style
- `var` when the type is obvious from the right-hand side; explicit type otherwise.
- Always use braces, even for single-line `if`.
- Expression-bodied members only for one-liners.
- Prefer `sealed` classes unless designed for inheritance. Prefer `record` / `readonly record struct` for immutable data.
- Use `is null` / `is not null`, not `== null`.
- Collection expressions (`[]`) and pattern matching where they read clearly.
- Keep methods short; extract when a method needs a comment to explain its sections.

### Nullability and errors
- No `!` (null-forgiving) without a comment explaining why it's safe.
- Validate public API arguments with `ArgumentNullException.ThrowIfNull` / `ArgumentOutOfRangeException.ThrowIf*`.
- Expected failures (DDC timeouts, unsupported VCP codes, missing monitors) are not exceptions: return `bool TryX(out ...)` or a result type. Exceptions are for bugs and truly exceptional states.
- Never swallow exceptions silently. Catch the narrowest type, log it, and say why it's safe to continue.
- Win32 failures: check the return value, capture `Marshal.GetLastPInvokeError()` immediately, log it.

### Async and threading
- No `async void` except event handlers. No `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on UI or hook threads.
- `ConfigureAwait(false)` in `DisplayKnob.Core` (Core is mostly dedicated threads and channels, so this rarely comes up); not needed in `DisplayKnob.App`.
- Pass `CancellationToken` through long-running or I/O methods (`CancellationToken cancellationToken = default` as the last parameter).
- Use `System.Threading.Channels` for producer/consumer queues (hook → DDC workers). Use `Lock` for simple locking.
- UI updates only via `DispatcherQueue.TryEnqueue`.
- The keyboard hook callback must not allocate, log, lock, or await (see the architecture doc).

### Resources and interop
- Use CsWin32-generated APIs (`NativeMethods.txt`); no hand-written `DllImport` unless CsWin32 can't express the call (comment why).
- Wrap native handles in `SafeHandle` or an `IDisposable` owner. Physical monitor handles are released with `DestroyPhysicalMonitors` in `Dispose`.
- `using` declarations for disposables. Implement `IDisposable` correctly (`sealed`, no finalizer unless owning raw handles).
- Prefer `Span<T>` / `stackalloc` for small native buffers in hot paths.

### Logging
- Serilog with message templates, never string interpolation: `logger.Information("Set VCP {Code:X2} = {Value} on {MonitorId}", ...)`.
- Inject Serilog's `ILogger` into Core services; no static `Log` calls in Core.
- Levels: `Debug` for per-keypress detail, `Information` for state changes (monitor added, endpoint changed), `Warning` for recoverable DDC failures, `Error` for broken features.

### Dependencies and design
- Core depends on abstractions (`IMonitorController`, `IVolumeSource`) so it can be unit-tested.
- Time: inject `TimeProvider`; don't call `DateTime.Now` / `Stopwatch` / `Task.Delay` directly in logic that needs testing. Tests use `FakeTimeProvider`.
- Constructor injection; no service locator, no static mutable state.
- Composition with `Microsoft.Extensions.DependencyInjection`, wired only at App startup. Core exposes `AddDisplayKnobCore(this IServiceCollection)`; Core types never resolve services themselves.
- No WinUI / `Microsoft.UI.*` types in `DisplayKnob.Core`.
- Settings types are records serialized with a `JsonSerializerContext` (source generation); no reflection-based serialization.

### Packages
- Central package management: versions only in `Directory.Packages.props`; `.csproj` files use `<PackageReference Include="..." />` without `Version`.
- Prefer the BCL when it covers the need.

### App (WinUI)
- MVVM with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`). Code-behind only for view-specific glue (window styles, interop).
- ViewModels don't reference WinUI controls; they talk to Core through interfaces.

### Tests
- xUnit v3. Test name: `Method_Scenario_ExpectedResult`.
- One behavior per test; arrange/act/assert separated by blank lines.
- No real hardware, timers, or sleeps in unit tests. Use fakes in `tests/DisplayKnob.Core.Tests/Fakes/` and `FakeTimeProvider`.

### Comments and docs
- XML doc comments (`///`) on public types and members in Core (enforced by the build).
- Comments explain *why* (hardware quirks, Win32 gotchas), not *what*. Link the source for non-obvious behavior.
- All code, comments, identifiers, and docs in English.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
