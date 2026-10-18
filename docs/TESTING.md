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

Commit 6516411 passed 34/34 tests locally and in Windows CI. The audit adds 21 test cases. Code commit 0f4b29a passed Windows CI with 55/55 tests and 0 build warnings/errors: https://github.com/armanemp/CT-TraderBot/actions/runs/39085123020. The code follow-on changes after 0f4b include malformed-lease rejection and visible warnings for stop/risk/storage problems; the latter changes are awaiting their own CI/local validation. The user must run the current revision locally. cTrader storage/restart and broker-execution paths remain outside the core unit-test scope.

The build script runs the test suite after compiling the solution and checks the generated CTTraderBot.algo path. Unit tests cover pure rules only; they do not simulate broker fills, LocalStorage failure, stop rejection, multi-instance races or profitability.

## Minimum scenario matrix

- Long/short orders, including a price crossing planned SL between signal and fill.
- ATR/history unavailable, zero/NaN values and insufficient bars.
- Minimum/maximum volume, invalid steps, insufficient free margin and free-margin changes during execution.
- Strict M5 alignment on/off and opposing strong/weak M5 context.
- Fresh intrabar crossing, price already beyond an old level on startup, spread spikes and duplicate callbacks.
- Broker rejects SL; retry remains bounded and warning remains visible.
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
