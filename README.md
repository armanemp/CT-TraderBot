# CT-TraderBot

A maintainable cTrader Automate cBot project for the SmartPosition M5 Micro-Swing strategy.

## Project status

**Migration status:** the original single-file source is captured under legacy/reference; the 74-method behavior-preserving split has been compiled in Windows CI and locally. Platform-independent policies cover adaptive risk budgeting, fresh price crossings, M5 direction/alignment, entry reward/risk screening and protective-stop geometry, with an NUnit regression harness. The audit is still in progress: broker/runtime acceptance, full entry/protection policy extraction, and evidence-based strategy evaluation remain open. The legacy source remains the behavior baseline.

## Goals

- Separate strategy decisions from cTrader API calls and chart controls.
- Keep risk sizing, initial protection, break-even/trailing, target-guide, persistence, and multi-instance coordination independently reviewable.
- Preserve current behavior unless a change is explicitly documented as a bug fix.
- Fail visibly and safely when protection cannot be established.
- Add regression tests for pure logic and maintain a reproducible demo-validation checklist.
- Keep the original source as a reference during migration; do not continue development by appending patches to a monolithic file.

## Repository layout

```text
src/
  CTTraderBot.Core/          Platform-independent policies and domain models
  CTTraderBot.CTrader/       cTrader Robot entry point and platform adapters
tests/
  CTTraderBot.Core.Tests/    NUnit tests for platform-independent policies
docs/
  ARCHITECTURE.md
  ROADMAP.md
  TESTING.md
```


## Current cBot module map

- `SmartPositionAutoTrader.Robot.cs`: cBot identity, parameters and runtime state.
- `SmartPositionAutoTrader.Lifecycle.cs`: startup validation, events, timer and shutdown.
- `SmartPositionAutoTrader.EntryExecution.cs`: entry orchestration, sizing checks and order submission.
- `SmartPositionAutoTrader.EntrySignals.cs`: closed-bar patterns, live breakout and M5 direction.
- `SmartPositionAutoTrader.EntryStructure.cs`: swing anchors, lower-timeframe ATR and forward barriers.
- `SmartPositionAutoTrader.EntryQuality.cs`: setup scoring, M5 directional strength and entry-screening R.
- `SmartPositionAutoTrader.EntryDiagnostics.cs`: rate-limited explanation of entry vetoes, separate from protection warnings.
- `SmartPositionAutoTrader.RiskAndSizing.cs`: margin estimation, volume constraints, risk budget and commission estimate.
- `SmartPositionAutoTrader.InitialProtection.cs`, `SmartPositionAutoTrader.BreakEvenTrailing.cs`, `SmartPositionAutoTrader.StopPriceRules.cs`: initial SL, profit-lock/trailing and cTrader-side stop-geometry adapter.
- `CTTraderBot.Core.Policies.ProtectiveStopPolicy`: pure loss-side, minimum-distance, tick-normalization and monotonic stop-selection rules covered by NUnit tests.
- `SmartPositionAutoTrader.PositionManagement.cs`, `SmartPositionAutoTrader.PositionStateStore.cs`, `SmartPositionAutoTrader.OwnershipCoordinator.cs`: exact-label management, persisted risk state and lease-based coordination.
- `SmartPositionAutoTrader.SuggestedTargetGuide.cs`: chart-only target guide; it never submits a broker TP.
- `SmartPositionAutoTrader.VolumeControlPanel.cs` and `SmartPositionAutoTrader.RuntimeHelpers.cs`: chart controls, status notices and small runtime helpers.

## Source and compiled cBot locations

Keep this Git repository where it was cloned (for this checkout: `%USERPROFILE%\\Desktop\\GPT\\CT-TraderBot`). **Do not move the source repository into cTrader's Robots folder.**

The only generated artifact intended for cTrader's local Robots directory is the compiled `.algo` file:

`%USERPROFILE%\\Documents\\cAlgo\\Sources\\Robots\\`

The pinned `cTrader.Automate` package builds and publishes the cBot. `Directory.Build.targets` sets the bundle filename and metadata paths immediately before the package extracts metadata, preventing the nested project layout from producing a misleading `src.algo` artifact name. A successful build must publish `CTTraderBot.algo` to the Robots directory; the build script verifies that exact file. Generated `.algo` files are ignored by Git and are never committed to the source repository.

## Build

The cTrader build is configured through the pinned `cTrader.Automate` package. After the baseline-split GitHub Actions workflow succeeds and the generated Robot files are present, run from PowerShell:

```powershell
cd "$HOME\Desktop\GPT\CT-TraderBot"
git pull --ff-only origin main
.\scripts\build-cbot.ps1
```

The build script restores and builds `CT-TraderBot.sln`, then verifies that `CTTraderBot.algo` was published to `%USERPROFILE%\Documents\cAlgo\Sources\Robots\`. The source repository remains on Desktop; only the generated `.algo` artifact is published to cTrader. The `.algo` output is ignored by Git.

The .NET 6 NUnit project tests platform-independent policies. Run the build-and-test gate locally with .\scripts\build-cbot.ps1; it builds the solution, runs the unit tests, and verifies that CTTraderBot.algo was published to the Robots directory. Passing tests do not replace cTrader demo acceptance. The current state/lease design is for local desktop cTrader: official cTrader documentation says LocalStorage Reload/Flush are unsupported no-ops in Cloud, without an error. Cloud deployment must remain unsupported until the persistence/ownership design is replaced.

## Safety

This repository is software, not financial advice. Trading can result in losses. Use a demo account during development. Neither compilation nor unit tests prove profitability or eliminate execution risk. Never run an unvalidated migration on a live account.

See [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Testing](docs/TESTING.md).
