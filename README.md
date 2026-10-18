# CT-TraderBot

A maintainable cTrader Automate cBot project for the SmartPosition M5 Micro-Swing strategy.

## Project status

**Migration status:** the original single-file source is captured under `legacy/reference/`; the first behavior-preserving pass split its 74 methods into 11 C# files and compiled successfully on Windows CI. Standalone service extraction, a formal unit-test suite, local cTrader validation and demo acceptance remain in progress. The original source is the behavioral baseline.

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

## Source and compiled cBot locations

Keep this Git repository where it was cloned (for example `%USERPROFILE%\\Desktop\\CT-TraderBot`). **Do not move the source repository into cTrader's Robots folder.**

The only generated artifact intended for cTrader's local Robots directory is the compiled `.algo` file:

`%USERPROFILE%\\Documents\\cAlgo\\Sources\\Robots\\`

The pinned `cTrader.Automate` package builds and publishes the cBot. `Directory.Build.targets` sets the bundle filename and metadata paths immediately before the package extracts metadata, preventing the nested project layout from producing a misleading `src.algo` artifact name. A successful build must publish `CTTraderBot.algo` to the Robots directory; the build script verifies that exact file. Generated `.algo` files are ignored by Git and are never committed to the source repository.

## Build

The cTrader build is configured through the pinned `cTrader.Automate` package. After the baseline-split GitHub Actions workflow succeeds and the generated Robot files are present, run from PowerShell:

```powershell
cd "$HOME\Desktop\CT-TraderBot"
git pull --ff-only origin main
.\scripts\build-cbot.ps1
```

The build script restores and builds `CT-TraderBot.sln`, then verifies that `CTTraderBot.algo` was published to `%USERPROFILE%\Documents\cAlgo\Sources\Robots\`. The source repository remains on Desktop; only the generated `.algo` artifact is published to cTrader. The `.algo` output is ignored by Git.

There is not yet a standalone unit-test project. It will be added alongside the first extracted pure policy and its real tests; an empty test project will not be used as a placeholder.

## Safety

This repository is software, not financial advice. Trading can result in losses. Use a demo account during development. Neither compilation nor unit tests prove profitability or eliminate execution risk. Never run an unvalidated migration on a live account.

See [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Testing](docs/TESTING.md).
