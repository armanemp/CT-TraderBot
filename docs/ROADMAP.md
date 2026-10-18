# Development roadmap

Statuses: **Not started**, **In progress**, **Blocked**, **Verified**. File creation alone is not verification.

## Phase 0 — Repository foundation
**Status: In progress**
- [x] Initialize repository conventions, architecture, and migration rules.
- [ ] Add the exact original `.cs` file under `legacy/reference/` unchanged.
- [ ] Record SHA-256, line count, cTrader version/API assembly version, and compiler diagnostics.

## Phase 1 — Reproducible baseline (no behavior changes)
- [ ] Create the cTrader project/solution using the API target actually installed.
- [ ] Compile the untouched baseline and record every diagnostic.
- [ ] Inventory all public parameters/defaults and runtime events.
- [ ] Inventory entry, volume modes, SL, BE/trailing, TP guide, persistence, ownership, and UI.
- **Exit gate:** baseline compiles or each existing blocker is documented precisely.

## Phase 2 — Mechanical source decomposition (behavior-preserving)
- [ ] Split the source by responsibility; a partial-class pass may be used only as a temporary mechanical move.
- [ ] Keep the Robot attribute/class identity and lifecycle orchestration in one entry-point file.
- [ ] Move entry, sizing, protection, target guide, state/ownership and UI code into separate files.
- [ ] Verify every original member exists exactly once; no missing or duplicate methods.
- [ ] Diff public parameters/defaults, trade labels, storage keys and chart object names.
- **Exit gate:** compile succeeds and a member inventory proves no omissions or duplicates.

## Phase 3 — Entry policy isolation
- [ ] Separate M5 trend context, closed-bar setup and intrabar crossing policies.
- [ ] Make strict M5 alignment a true hard filter when enabled; retain soft-filter behavior only when explicitly selected.
- [ ] Evaluate the actual live breakout event, not only the previous closed candle.
- [ ] Test lower-timeframe swing lookback and cooldown boundaries.
- **Exit gate:** deterministic tests cover both directions, conflicting signals, fresh crossings and cooldown edges.

## Phase 4 — Risk and volume
- [ ] Separate the risk budget from broker margin calculations.
- [ ] Validate manual volume, auto minimum/maximum volume and broker volume steps.
- [ ] Define commission/slippage reserves and never imply estimated loss is exact.
- [ ] Reconcile pre-trade and post-fill risk and define behavior when approved limits are exceeded.
- [ ] Add account-wide open-risk controls only after a reliable current-SL risk source is defined.
- **Exit gate:** sizing boundaries pass; demo quantities match symbol rules.

## Phase 5 — Initial protection
- [ ] Isolate stop-anchor calculation from broker submission.
- [ ] Provide a genuine fixed-pip fallback when structure/ATR is unavailable, or fail safely before entry.
- [ ] Validate broker distance, tick size, side, and quote side consistently.
- [ ] Make SL rejection/unprotected status visible with bounded retry/backoff.
- **Exit gate:** long/short geometry tests pass and SL rejection is demonstrated on demo.

## Phase 6 — Break-even and trailing
- [ ] Extract BE and ATR/structure trailing policies.
- [ ] Prove monotonicity: a candidate never weakens the existing SL.
- [ ] Test commission cushion, bar delay, missing ATR, structure and broker-distance constraints.
- **Exit gate:** deterministic tests and bid/ask-aware demo checks pass.

## Phase 7 — Suggested TP guide
- [ ] Extract target calculation separately from chart rendering.
- [ ] Keep the target chart-only; no broker TP action is allowed.
- [ ] Test barriers, tick normalization, target extension and weakening momentum.
- **Exit gate:** calculations are independently testable and rendering is manually verified.

## Phase 8 — State and multi-instance management
- [ ] Version persisted state and validate identity before restoring it.
- [ ] Define behavior when local-storage reload/flush fails.
- [ ] Document that device-scoped LocalStorage leases are not atomic mutual exclusion.
- [ ] Prevent stale-state cleanup from deleting a possibly renewed lease.
- [ ] Separate entry de-duplication from position-manager leases.
- **Exit gate:** restart/recovery and feasible multi-instance race scenarios are documented and tested.

## Phase 9 — Chart controls and lifecycle
- [ ] Move panel construction/input validation behind a UI adapter.
- [ ] Detach event handlers and remove controls on stop.
- [ ] Show active mode, risk/margin limits and safety warnings truthfully.
- **Exit gate:** valid/invalid input, toggles and shutdown cleanup are checked in cTrader.

## Phase 10 — Performance and acceptance
- [ ] Remove redundant full-position-set scans from tick/timer hot paths.
- [ ] Measure CPU/latency with realistic tick and position counts.
- [ ] Run tests, compile, static review, backtest and demo acceptance scenarios.
- [ ] Record known limitations and release checklist.
- **Exit gate:** checks have recorded evidence; no live-readiness claim without demo acceptance.

## Working agreement
Keep each phase small enough to review. Do not combine behavior redesign with mechanical extraction. Record tests actually run—not tests merely planned.
