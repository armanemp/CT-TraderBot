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

## Build

The pure core and unit tests target .NET 6. Run:

```powershell
dotnet test .\tests\CTTraderBot.Core.Tests\CTTraderBot.Core.Tests.csproj
```

The cTrader integration project references the API assembly installed by cTrader. Pass its path explicitly when building:

```powershell
dotnet build .\src\CTTraderBot.CTrader\CTTraderBot.CTrader.csproj /p:CTraderApiPath="C:\Users\<user>\Documents\cAlgo\API\cAlgo.API.dll"
```

The API path is machine-specific; do not commit local absolute paths. Compile and smoke-test the Robot in cTrader Automate as well as with the command line.

## Safety

This repository is software, not financial advice. Trading can result in losses. Use a demo account during development. Neither compilation nor unit tests prove profitability or eliminate execution risk. Never run an unvalidated migration on a live account.

See [Architecture](docs/ARCHITECTURE.md), [Roadmap](docs/ROADMAP.md), and [Testing](docs/TESTING.md).
