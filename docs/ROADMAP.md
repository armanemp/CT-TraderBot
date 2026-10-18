# Development roadmap

Statuses: **Not started**, **In progress**, **Blocked**, **Verified**. File creation alone is not verification.

## Phase 0 — Repository foundation
**Status: In progress**
- [x] Initialize repository conventions, architecture, and migration rules.
- [x] Register a reference copy of the user-pasted cBot in `legacy/reference/SmartPositionAutoTrader_M5_MicroSwingPrecision_HedgedVolume.cs`.
- [x] Add a pinned `cTrader.Automate 1.0.21` project, solution, Windows build script, artifact naming override, and CI workflows.
- [x] Run the strict splitter and build the generated cBot successfully on a Windows GitHub Actions runner.
- [ ] Compare the reference copy against the user's original local `.cs` file and record SHA-256; byte-for-byte identity has not been independently verified.
- [ ] Record the user's installed cTrader/API version and local build diagnostics after the first local pull.

## Phase 1 — Reproducible baseline (no behavior changes)
**Status: In progress**
- [x] Create the cTrader project/solution with pinned `cTrader.Automate 1.0.21` and .NET 6 target.
- [x] Compile the mechanically decomposed source on Windows CI: 0 warnings, 0 errors; `CTTraderBot.algo` was published and verified.
- [x] Inventory 67 public `[Parameter]` declarations and the Robot's lifecycle/event methods from the source.
- [x] Inventory entry, volume modes, initial SL, BE/trailing, chart-only TP guide, persistence, ownership and chart UI into responsibility files.
- [ ] User to run the first local build with the installed cTrader version and report any environment-specific differences.
- **Note:** the original single-file implementation was not separately compiled before decomposition; the extracted implementation was compiled with method bodies moved verbatim. Keep this distinction in validation records.

## Phase 2 — Mechanical source decomposition (behavior-preserving)
**Status: Verified — mechanical split only**
- [x] Split the source into 11 C# files: one Robot/field/parameter entry file and 10 responsibility-based partial-class files.
- [x] Keep the `[Robot]` attribute and class identity in `SmartPositionAutoTrader.Robot.cs`.
- [x] Move entry, lifecycle, sizing, protection, target guide, state/ownership, runtime helpers and chart UI into separate files.
- [x] Strict source inventory passed: all 74 expected methods/overloads mapped, with no unexpected or missing method names.
- [x] Public parameter declarations/defaults compared against the reference: 67/67 declarations matched exactly.
- [x] Critical trade-label, state-key, owner-key, TP-line/label prefixes and TP-guide-only marker counts matched between reference and split sources.
- [x] Windows CI build passed with 0 warnings and 0 errors; artifact `Documents\\cAlgo\\Sources\\Robots\\CTTraderBot.algo` was verified. See [successful split/build run](https://github.com/armanemp/CT-TraderBot/actions/runs/39083607155) and [decomposition commit](https://github.com/armanemp/CT-TraderBot/commit/4ff8bf0).
- **Exit gate passed for mechanical split.** This does not mean business logic has been extracted into standalone services or validated in a running cTrader instance; those remain future phases.

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
