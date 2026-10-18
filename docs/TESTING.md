# Validation and testing

## Test layers

1. **Static/source checks:** duplicate-member detection, public parameter/default diffs, no accidental TP submission, no unreviewed debug behavior.
2. **Pure unit tests:** stop-price rounding, risk-budget edges, signal conflict resolution, cooldown, target barriers and state parsing.
3. **Build checks:** core/tests on CI; cTrader integration against the exact installed API assembly.
4. **cTrader demo acceptance:** order submission, SL acceptance/rejection, fill price, sizing modes, restart recovery, chart UI and multiple instances.
5. **Backtest/out-of-sample:** strategy robustness; these do not replace demo execution tests.

## Implemented unit tests

The first extracted policy is `CTTraderBot.Core.Policies.AdaptiveRiskBudgetPolicy`. The current test suite covers the squared quality curve, zero/one boundaries, below/above-range scores, disabled adaptive mode, a positive hard ceiling, a disabled ceiling, invalid bound ordering and non-finite quality input. `CTTraderBot.Core.Policies.FreshPriceCrossPolicy` additionally tests fresh above/below quote crossings, strict current-quote boundaries, a previous quote exactly at the trigger, stale crossings where price was already beyond the level, and absent/non-positive previous quotes.

Run locally:

```powershell
dotnet test .\tests\CTTraderBot.Core.Tests\CTTraderBot.Core.Tests.csproj --configuration Release
```

The cBot build script and Windows CI execute this test project after the solution build. These tests verify the risk-budget calculation only; they do not prove order sizing, broker acceptance, realized loss or strategy profitability.

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
