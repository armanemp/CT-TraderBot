# CT-TraderBot

A maintainable cTrader Automate cBot project for the SmartPosition M5 Micro-Swing strategy.

## Project status

**Architecture foundation — migration in progress.** The original single-file cBot is the behavioral baseline. It must be decomposed incrementally while preserving the existing entry, risk, protection, suggested-target, state-recovery, chart UI, and ownership behavior. Do not claim parity or production readiness until the extracted implementation compiles against the installed cTrader API and passes the acceptance suite.

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
  CTTraderBot.Core.Tests/    Fast deterministic unit tests
docs/
  ARCHITECTURE.md
  ROADMAP.md
  TESTING.md
```

## Source and compiled cBot locations

Keep this Git repository where it was cloned (for example `%USERPROFILE%\\Desktop\\CT-TraderBot`). **Do not move the source repository into cTrader's Robots folder.**

The only generated artifact intended for cTrader's local Robots directory is the compiled `.algo` file:

`%USERPROFILE%\\Documents\\cAlgo\\Sources\\Robots\\`

The build configuration will use the official `cTrader.Automate` build/publish behavior so a successful cBot build generates the `.algo` artifact and publishes it to the appropriate Windows cTrader folder. Generated `.algo` files are ignored by Git and must not be committed to the source repository. If building without that package's publish behavior, copy only the final `.algo` artifact to the Robots directory.

## Build

The cTrader build is configured through the pinned `cTrader.Automate` package. After the baseline-split GitHub Actions workflow succeeds and the generated Robot files are present, run from PowerShell:

```powershell
cd "$HOME\Desktop\CT-TraderBot"
git pull --ff-only origin main
.\scripts\build-cbot.ps1
```

The build script restores and builds `CT-TraderBot.sln`, then verifies that `CTTraderBot.algo` was published to `%USERPROFILE%\Documents\cAlgo\Sources\Robots\`. The source repository remains on Desktop; only the generated `.algo` artifact is published to cTrader. The `.algo` output is ignored by Git.

To compile only the current project scaffold before the Robot source is integrated, run:

```powershell
.\scripts\build-cbot.ps1 -AllowScaffoldBuild
```

That command explicitly reports a scaffold-only compile and does **not** produce a tradable bot. The unit-test project will be added with the first tested pure policy extraction; until then there is no meaningful `dotnet test` target.

## Safety

This repository is software, not financial advice. Trading can result in losses. Use a demo account during development. Neither compilation nor unit tests prove profitability or eliminate execution risk. Never run an unvalidated migration on a live account.

See [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Testing](docs/TESTING.md).
