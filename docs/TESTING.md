# Validation and testing

## Test layers

1. **Static/source checks:** duplicate-member detection, public parameter/default diffs, no accidental TP submission, no unreviewed debug behavior.
2. **Pure unit tests:** stop-price rounding, risk-budget edges, signal conflict resolution, cooldown, target barriers and state parsing.
3. **Build checks:** core/tests on CI; cTrader integration against the exact installed API assembly.
4. **cTrader demo acceptance:** order submission, SL acceptance/rejection, fill price, sizing modes, restart recovery, chart UI and multiple instances.
5. **Backtest/out-of-sample:** strategy robustness; these do not replace demo execution tests.

## Implemented unit tests

The platform-independent tests cover:
- Adaptive risk-budget boundaries, squared quality curve, adaptive-off behavior, hard ceiling and invalid non-finite configuration.
- Fresh quote crossings in each direction, strict trigger edges, stale levels, missing prior quotes and non-finite quote/trigger inputs.
- M5 fast/slow EMA and closed-price direction rules, including neutral/conflicting and non-finite values.
- Required M5 alignment: matching direction allowed; opposite/neutral signals blocked; soft mode delegates to the separate context decision.
- Entry reward/risk screening uses the configured base R and is capped by the nearest valid forward barrier; it is deliberately independent from adaptive TP-guide extension.
- Post-fill risk assessment returns WithinBudget, OverBudget, or Unknown; invalid/missing estimates never count as safe.
- The cBot persists each new entry's approved loss budget in versioned v3 position state while still reading v2 and legacy 4-/5-field formats; malformed or unreadable saved risk state remains locked.
- After fill, the risk estimate includes adverse entry-to-SL distance, the configured slippage reserve, and a round-turn commission estimate when available. If the SL is unusable, the estimate is unknown, or estimated downside risk is more than 5% above the approved budget, a risk-review flag is persisted and new entries are paused. The bot keeps the existing broker SL and does not force-close solely from an estimate.

Commit 6516411 passed 34/34 tests locally and in Windows CI. The audit adds 21 test cases. Code commit 0f4b29a passed Windows CI with 55/55 tests and 0 build warnings/errors: https://github.com/armanemp/CT-TraderBot/actions/runs/39085123020. The follow-on code at commit 5004b86, including malformed-lease rejection, panel-visible safety warnings, and NaN margin-usage validation, also passed all Windows CI steps: https://github.com/armanemp/CT-TraderBot/actions/runs/39085481978. The module-split code at commit `226d7bb` passed Windows CI with a Release build, 0 warnings/errors, and 69/69 tests: https://github.com/armanemp/CT-TraderBot/actions/runs/39087069153. The latest `ProtectiveStopPolicy` at commit `14d2711` passed with 0 warnings/errors and 87/87 tests: https://github.com/armanemp/CT-TraderBot/actions/runs/39087296809. The user must still run the current revision locally. cTrader storage/restart and broker-execution paths remain outside the core unit-test scope.

The latest adapter patch also rejects NaN/infinity for every configurable numeric parameter, honours the configured multi-bar M5 wick fallback and fixed-pip fallback when ATR is unavailable, and reduces hot-path scans to the cBot label/current symbol (without using the pinned API's `FindById(int)` with a `long` ID). These adapter changes require CI and local compile verification; unit tests do not simulate broker position or storage behavior.

The build script runs the test suite after compiling the solution and checks the generated CTTraderBot.algo path. Unit tests cover pure rules only; they do not simulate broker fills, LocalStorage failure, stop rejection, multi-instance races or profitability.

## Minimum scenario matrix

- Long/short orders, including a price crossing planned SL between signal and fill.
- ATR/history unavailable, zero/NaN values and insufficient bars.
- Minimum/maximum volume, invalid steps, insufficient free margin and free-margin changes during execution.
- Strict M5 alignment on/off and opposing strong/weak M5 context.
- Fresh intrabar crossing, price already beyond an old level on startup, spread spikes and duplicate callbacks.
- Broker rejects SL; retry remains bounded and warning remains visible; no fresh entries occur while protection remains unconfirmed.
- Post-fill absolute structural SL widens risk: flag persists across restart, new entries remain paused, and the guard clears only when an updated downside estimate is within 105% of the approved budget or the position closes.
- If market price crosses the intended structural stop and an emergency close is pending/rejected, entry lock remains active on every management retry until closure is confirmed.
- BE/trailing never loosens current protective SL.
- Target guide reached with strong/weak momentum; no broker TP is accidentally attached.
- Restart with valid, stale, malformed and missing state.
- Two instances see the same position; lease reload/flush failures and expiry edges.
- Chart input validation and shutdown cleanup.
- Hedged versus netted account and exact-label isolation.

## Evidence template

Record commit SHA; cTrader and API assembly versions; symbol/account/chart/lower timeframe; demo or backtest environment; non-default parameters; scenario, expected/observed result, logs/screenshots and pass/fail. Do not put secrets or account identifiers in public logs.

## Release rule

A successful unit test or compile is not proof a broker accepted a protective stop, that a risk estimate is exact, or that a strategy is profitable. Do not deploy live until required demo scenarios pass and residual risks are explicitly accepted.


## Entry-veto diagnostics

The chart now has a separate, rate-limited entry-status line for common veto reasons (history, ATR, spread, M5 strength, EMA extension, structural stop, reward/risk, setup quality, risk sizing and storage health). It does not replace position-protection warnings. New entries are paused while position-state or ownership storage is known unhealthy. Code commit `8aecbef` requires CI and local compile verification; core tests do not simulate broker or storage failure.


## Current module boundaries

Entry execution, signal detection, structure, quality scoring, initial protection, BE/trailing and stop-price rules are now in separate partial-class files. This is a code-organization change; CI revalidated the unchanged 69-test suite at commit `226d7bb`. It does not replace demo acceptance.


## Protective stop geometry tests

`CTTraderBot.Core.Policies.ProtectiveStopPolicy` covers long/short loss-side checks, initial vs tightening tick rounding, broker min-distance conversion from pips/percentage, safety-tick handling, and monotonic stop selection. These tests validate pure numeric rules only; broker acceptance still requires cTrader demo tests.


## Latest CI evidence

At code commit `56805a2`, Windows CI passed source-contract verification, the Release build with 0 warnings/errors, and all 87 NUnit tests. This is source-contract, pure-policy and compile evidence; no broker execution or profitability claim follows from it. A local build and cTrader demo acceptance for the current revision remain pending.


## Persistent source-contract gate

scripts/verify-cbot-contract.ps1 compares every legacy public parameter's name, type and full Parameter attribute against the current cBot, flags duplicate/missing/changed declarations, and checks that the reviewed market-entry path keeps broker TP explicitly null. It is called from both the local build script and Windows CI. The new gate passed in Windows CI at commit `56805a2`: 67/67 legacy parameter contracts preserved, no extra current parameters, broker TP remains null, Release build succeeded with 0 warnings/errors, and 87/87 NUnit tests passed. Local execution and demo broker acceptance remain required.
