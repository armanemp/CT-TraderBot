# Deep code and strategy audit — 2026-10-18

## Executive result

This is a source-level review of the current repository: all 11 split cBot source files, the platform-independent core policies and tests, build/CI automation, position management, protection, chart guide, state recovery, lease-based multi-instance coordination, UI, and the legacy reference-file inventory. This is not a historical tick backtest or a cTrader demo acceptance run.

### Evidence before the current audit patch

- Commit 6516411 built locally with zero warnings and zero errors.
- The user-local NUnit suite passed 34 tests, with zero failures/skips.
- CTTraderBot.algo was published to the cTrader Robots directory.
- Windows CI passed for that same baseline: https://github.com/armanemp/CT-TraderBot/actions/runs/39084630747.

The code-audit commit 0f4b29a passed Windows CI with a successful Release build, zero warnings/errors, and 55/55 NUnit cases: https://github.com/armanemp/CT-TraderBot/actions/runs/39085123020. The entry-diagnostics and entry/protection module split at commit 226d7bb passed Windows CI with a Release build, zero warnings/errors, and 69/69 NUnit cases: https://github.com/armanemp/CT-TraderBot/actions/runs/39087069153. The latest stop-geometry policy at code commit 14d2711 passed with zero warnings/errors and 87/87 NUnit tests: https://github.com/armanemp/CT-TraderBot/actions/runs/39087296809. Windows CI at commit `56805a2` additionally verified the 67-parameter source contract and chart-only TP invariant, then passed Release build with 0 warnings/errors and 87/87 tests. A local build of the current head and runtime/demo acceptance remain pending. A successful build or unit test is not evidence of profitability.

## What the cBot does

### 1. Runtime and intended market

The product is one cTrader Automate cBot, intended for an M5 chart. It rejects non-XAU/GOLD symbol names by default; this is only a naming guard, not proof that every broker's gold contract, pip/tick values or trading hours are equivalent. Entry confirmation defaults to M1, with M2 and M3 alternatives.

On start it validates selected parameters, confirms the chart timeframe, initializes M5 and lower-timeframe EMA/ATR-related state, listens for lower-timeframe bar-open and position open/close events, starts a timer and creates a chart-side volume panel.

Only positions with both the current symbol and exact configured auto-trade label are managed. Entry execution uses market orders, not pending orders.

### 2. Entry logic

There are two entry-evaluation paths:

1. Closed lower-timeframe candle: rejection, engulfing, momentum, structural-break and pullback/reclaim rules. EMA alignment on the lower timeframe is an optional filter.
2. Live micro-swing breakout: looks for an actual fresh Ask crossing above a completed-bar swing-high trigger or Bid crossing below a swing-low trigger. The intended safeguard is to avoid treating price already beyond a level at robot attachment as a new cross.

The candidate is then evaluated against M5 directional context/strength; absolute and ATR-relative spread; distance from the M5 fast EMA; availability of valid history/ATR; a structure-derived stop; maximum initial stop distance in lower-TF ATR; estimated reward/risk; and a composite setup-quality score. That quality score is a weighted heuristic, not a calibrated win probability.

Soft M5 mode intentionally allows selected countertrend lower-timeframe breaks. The strict M5 parameter is different: when true, the M5 direction must match the entry direction and a neutral bias must block entry. The pre-audit implementation had a confirmed configuration-contract defect: a strong lower-timeframe structure break could override the strict M5 option. The audit patch adds a separately testable hard gate.

### 3. Risk budget and volume

The adaptive budget is a percentage of equity; the baseline defaults are 0.50% minimum and 1.50% maximum. In adaptive mode the curve is minimum + quality squared × (maximum − minimum). A positive absolute ceiling caps the result; zero disables this separate ceiling.

Auto mode limits candidate size using the configured maximum lots, broker volume range/step, estimated margin/free-margin budget and estimated currency loss for the planned stop plus a slippage-pip reserve. The baseline auto margin cap is 40% of current free margin.

Manual mode uses the selected volume without silently scaling it down. It rejects broker-invalid size, insufficient margin, or estimated stop loss above the manual risk ceiling. The manual path is deliberately not scaled to the adaptive setup score; it uses the configured maximum budget/hard ceiling. The pre-trade loss remains an estimate, and the current sizing formula does not yet model the commission schedule as a verified pre-entry cost.

### 4. Initial stop and order execution

The intended initial stop is placed beyond a lower-timeframe structural swing/wick using a spread/tick/ATR buffer. M5 structure is a fallback when lower-timeframe context is not ready. The code rejects a stop on the wrong side of entry and skips a new entry when the required structural distance exceeds the configured lower-timeframe ATR limit.

The market order requests a stop-loss distance in pips and explicitly does not submit a take-profit. After fill the bot attempts to align the broker stop to the planned absolute structural level. It refuses to widen a stop to conceal a level that price has already breached; a configurable emergency-close path can be attempted instead.

A residual risk remains: fill drift and broker minimum stop distance can cause the final absolute structural stop to differ from the pre-trade stop distance. Current post-fill logic reports some estimates above budget but does not yet have a fully defined, broker-tested correction policy. It is unsafe to call the configured risk percentage an exact maximum loss.

### 5. Position protection and target guide

For managed positions, the bot attempts an initial protective SL if none exists. If the intended level has already been crossed, it can try an emergency close and retry rather than fabricate a wider stop.

Break-even is based on profit distance expressed relative to initial risk (R), an M5 directional-strength heuristic, an estimated round-turn commission based on the observed position commission field and configurable pips cushions. Trailing combines an ATR distance with recent M5 structural extrema. A submitted update must improve the protective direction by at least the configured minimum step: long stops advance upward, short stops advance downward.

The adaptive TP is visual-only. It is not sent to the broker, does not autonomously close a position, and may extend after the displayed objective is reached if completed-bar momentum stays strong. With weak momentum, the chart label asks the trader to review the exit. There is no dedicated opposite-signal auto-exit policy today, so broker stop management and the trader's separate exit actions are responsible for leaving an open position.

### 6. Panel, persistence and multi-instance management

The chart panel switches between Manual Lots and Auto Margin. Applying values validates lot/percentage input and affects the running instance; it does not rewrite the saved cBot parameters.

The bot stores initial-risk distance and identity fields in device-scoped LocalStorage. If state cannot be restored, it estimates R from the current stop or ATR/fallback values. The old four-field stored record has no explicit estimate flag; the prior parser treated it as confirmed. The current audit patch classifies that format as estimated, rejects impossible sub-tick risk distances and upgrades a valid legacy record to the newer flag-bearing form.

A lease key attempts to decide which instance manages a position. Device LocalStorage does not provide atomic compare-and-swap; simultaneous acquisitions cannot be ruled out. The audit patch makes refresh/read/write failures fail closed and avoids deleting leases from a stale cache during shutdown. This improves failure handling but is not a true mutual-exclusion lock. A storage outage can also pause a manager; if any open position lacks a broker SL in that condition, it requires explicit manual attention. This implementation is for local desktop cTrader only: official cTrader documentation states that LocalStorage.Reload and LocalStorage.Flush do nothing and report no error in Cloud. Exception-based health checks therefore cannot detect lack of persistence in Cloud. Do not assume restart recovery or cross-instance coordination works there until a supported storage design and acceptance tests exist. References: https://help.ctrader.com/ctrader-algo/references/Localstorage/LocalStorage/Reload/ and https://help.ctrader.com/ctrader-algo/references/Localstorage/LocalStorage/Flush/.

## Findings and changes

| Finding | Severity | Action/status |
|---|---|---|
| Entry screening could inherit an aspirational adaptive TP-guide multiple (up to 30R) even when no forward structural barrier was found. | High; the reward/risk gate could overstate available opportunity | Replaced screening logic with a pure base-R/nearest-barrier policy; adaptive chart guide remains separate. Latest CI/local validation required. |
| The strict M5 alignment option could still admit an opposing confirmed lower-TF structure break. | High; option contract violated | Fixed in commit e8e525b by adding M5TrendAlignmentPolicy; covered by regression tests and included in the successful 55-test CI run at 0f4b29a. Local validation pending. |
| Non-finite market/indicator/configuration inputs were not uniformly rejected in core policy functions. | Medium; corrupted values could produce misleading decisions | Added finite checks to risk-budget, fresh-cross and M5-bias policies in commit e288317; those changes passed as part of the 55-test CI run at 0f4b29a. |
| Four-field legacy position state could make recovered initial R look confirmed when its original stop was no longer known. | Medium/high; misleading recovery confidence | Fixed in commit 0f4b29a: treat it as estimated, reject sub-tick risk, and upgrade the record. Restart acceptance pending. |
| Shared-storage refresh/read/write failures or malformed ownership-lease strings could be treated as if no owner existed. | High for multi-instance safety | The code rejects malformed records and fails closed on refresh/read/write failures; shutdown avoids releasing leases from a stale cache. Basic code compiled in CI, but the malformed-record follow-on and actual storage/concurrent-instance behavior require latest CI plus demo validation. The underlying lease is still non-atomic. |
| The position manager and entry capacity repeatedly scanned all account positions on every market tick. | Medium; avoidable hot-path work scales with unrelated positions | This patch uses label/symbol-filtered `FindAll` and a filtered ID lookup; the initial `FindById(long)` attempt failed against this pinned cTrader API and was removed. Revalidation pending. |
| README status still said that no unit-test project existed, and the roadmap validation count lagged the user's local results. | Low, documentation drift | Updated in this audit. |
| `StructuralWickLookbackBars` and `M5FallbackAtrMultiplier` were declared but unused; the fixed-pip fallback could be skipped when ATR was unavailable. | Medium/high; UI settings did not match runtime behavior and recovery could wait indefinitely without ATR | This patch uses the configured multi-bar M5 wick lookback, applies the ATR multiplier, and uses the fixed-pip fallback as the final recovery path. CI/local validation pending. |
| Post-fill absolute structural alignment may increase nominal SL loss beyond the pre-trade risk budget; current behavior does not automatically reconcile every overrun. | High residual trading risk | Not silently changed without selecting and demo-testing a safe policy. Next risk phase item. |
| Stop failures could be invisible when verbose diagnostics were disabled; post-fill risk overruns were diagnostic-log-only. | High; protection/over-budget state could be missed by a user | Follow-on commit 5004b86 routes throttled position warnings to the panel and makes startup errors visible even with verbose diagnostics off; Windows CI passed. Latest local validation and demo acceptance pending. |
| State/ownership paths depend on cTrader LocalStorage and broker APIs that core unit tests cannot exercise. | High residual runtime risk | Requires restart, failed-storage, rejected-SL and multi-instance demo scenarios. |
| Strategy profitability is not established. | Critical limitation, not a code defect | Requires realistic backtest, out-of-sample and demo data including spread, commission, slippage and rejected/partial execution scenarios. |

## What to add to improve precision without over-filtering

Do not change several thresholds together. First lock down semantics and safety; then measure each strategy change independently.

### Priority 1 — execution/risk safety
- Define and test what happens if post-fill absolute SL alignment makes estimated risk exceed the approved budget.
- Include commission in the pre-trade estimate where the broker's commission model is known; otherwise clearly expose that it is omitted.
- Demo-test failed stop submission, delayed ticks, gap through planned SL, invalid/stale persisted state, restart recovery, and two same-label instances.
- Add an explicit warning/telemetry path if position management is paused because shared storage cannot be trusted.

### Priority 2 — entry correctness
- Extract closed-bar rejection/engulf/momentum/BOS/pullback into deterministic policies with long/short test matrices.
- Cover missing bars/ATR, conflicting signals, tick equality, first tick after attach, duplicate event callbacks, cooldown and repeated broker rejection.
- Keep strict M5 and soft M5 modes independently testable. Do not raise thresholds merely because a more selective backtest looks better on one interval.

### Priority 3 — evidence-based profitability work
- Create a trade journal containing signal features, each veto reason, session, spread, planned and filled price, planned/actual SL, slippage, fees, MFE/MAE and realized R.
- Compare the existing baseline with one change at a time over high-quality tick data. Include broker spread, commission, slippage and realistic execution.
- Separate tuning, validation and untouched out-of-sample periods; report trade count, net expectancy after costs, profit factor, maximum drawdown, average R, MAE/MFE, time-in-trade and breakdown by session/volatility regime.
- Prefer a broad, stable performance plateau across adjacent parameters instead of a single best point. Do not optimize raw net profit alone or treat the hand-built quality score as a probability.

Possible later candidates, only if independent tests show improvement: volatility/regime classification; session/liquidity filters; persistent cooldown; an optional opposite-signal exit; account-wide open-risk aggregation; and score calibration against realized outcomes. None should be advertised as profit-improving before measured out-of-sample evidence.

## Release gate

Do not use a live account based only on a successful build or unit tests. Require a clean latest local build, green CI, correct CTTraderBot.algo output, proof that every live position has an accepted broker SL, restart/storage/ownership acceptance, and documented out-of-sample plus demo evidence. The strict M5 option must reject neutral or opposing M5 direction by design.


## Audit addendum — entry veto visibility

Code commit `8aecbef` adds a separate, rate-limited entry-status line for common entry filters, preserving the existing panel notice used for protection warnings. It also gates new entries when known position-state or ownership storage failures occur. This is a visibility and fail-closed change rather than a strategy threshold change. CI/local validation is pending; no profitability claim is implied.


## Current source layout validation

The entry and protection responsibilities are now split across named partial-class modules (execution, signal, structure, quality, entry diagnostics, initial protection, BE/trailing, and stop-price rules). CI at commit `226d7bb` passed with 69/69 tests and no warnings/errors. This is not proof of trading performance; local build, demo execution and out-of-sample evidence remain required.


## Audit addendum — protective stop geometry

A new core policy isolates the stop-side predicate, broker minimum-distance calculation, price tick normalization and monotonic more-protective-stop selection. It now has deterministic numeric tests for both directions and invalid inputs. CI/local validation pending. This does not simulate broker-side SL acceptance/rejection.


## Audit addendum — stop-price policy

The pure `CTTraderBot.Core.Policies.ProtectiveStopPolicy` now covers the loss-side check, minimum broker-distance conversion, tick-size normalization and more-protective-stop selection. Its long/short boundary tests are included in the successful 87-test CI run at commit `14d2711`. Broker acceptance/rejection and live quote races remain platform-level test requirements.


## Audit addendum — persistent source contract

A PowerShell source-contract verifier compares current parameter names/types/attributes against the legacy reference and checks the chart-only TP order contract. It is wired into the local build and Windows CI to catch accidental parameter-default drift or an unreviewed broker-TP path. The new gate passed in Windows CI at commit `56805a2`: all 67 reference parameter declarations (name, type and full attributes) match, no additional current parameters were found, the reviewed market-entry path keeps broker TP null, the Release build had 0 warnings/errors, and 87/87 NUnit tests passed. Local build and broker/demo acceptance remain pending.
