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
- [x] User's local PowerShell build succeeded after pull: 0 warnings, 0 errors; output published to `Documents\\cAlgo\\Sources\\Robots\\CTTraderBot.algo`.
- **Exit gate passed for mechanical split.** This does not mean business logic has been extracted into standalone services or validated in a running cTrader instance; those remain future phases.

## Phase 2.1 — First pure policy extraction and regression harness
**Status: Verified**
- [x] Extract the existing adaptive risk-budget formula into the platform-independent core.
- [x] Keep the cBot as adapter and preserve the valid-input calculation.
- [x] Add .NET 6 NUnit boundary tests for score curve, limits, hard ceiling and non-finite configuration.
- [x] Integrate the test project into the solution, Windows CI and local build script.
- [x] Local validation at commit 6516411: 0 build warnings/errors, CTTraderBot.algo published, and all 34 NUnit tests passed.
- [x] Windows CI at commit 6516411 passed: https://github.com/armanemp/CT-TraderBot/actions/runs/39084630747.
- **Scope:** the pure risk-budget function is not a complete test of broker sizing or realized trade risk.

## Phase 3 — Entry policy isolation
- [x] Extract the fresh above/below quote-cross predicate and cover equality, stale crossings, missing prior quotes and non-finite inputs.
- [x] Extract M5 EMA/closed-price direction into a pure policy; verified locally/CI at commit 6516411.
- [x] Fix the strict M5 alignment contract: when enabled, neutral or conflicting M5 bias must reject the signal; a lower-timeframe structure break may only override the soft-context path.
- [x] Add deterministic policy tests for strict matching, opposite direction, neutral bias, and soft mode.
- [x] Separate entry reward/risk screening from the adaptive chart-only TP guide; screening now uses the configured base R and caps it at a confirmed forward structural barrier rather than borrowing an aspirational guide extension.
- [x] Verify entry-screening policy in Windows CI: latest validation at commit `14d2711` passed 87/87 tests with 0 warnings/errors; the user's local build for this newest revision remains pending.
- [x] Windows CI at code commit 0f4b29a passed the complete 55-test suite with 0 build warnings/errors: https://github.com/armanemp/CT-TraderBot/actions/runs/39085123020.
- [x] Follow-on code commit 5004b86 also passed all Windows CI steps, including build and unit tests: https://github.com/armanemp/CT-TraderBot/actions/runs/39085481978.
- [ ] User to run the current repository revision locally; broker storage, restart and position-management acceptance still require cTrader demo tests.
- [ ] Separate remaining M5 trend strength, closed-bar setup and intrabar crossing decisions.
- [ ] Test lower-timeframe swing lookback and cooldown boundaries.
- **Exit gate:** both-direction tests, conflict/fresh-cross/cooldown coverage, CI and local build evidence.

## Phase 4 — Risk and volume
- [ ] Reconcile pre-trade risk with the actual fill and the post-fill absolute structural SL; define/demo-test a response when expected loss exceeds the approved budget.
- [ ] Include commission in pre-trade estimates where the broker's commission model can be measured reliably; otherwise label its omission explicitly.
- [ ] Separate the risk budget from broker margin calculations.
- [ ] Validate manual volume, auto minimum/maximum volume and broker volume steps.
- [ ] Define commission/slippage reserves and never imply estimated loss is exact.
- [ ] Reconcile pre-trade and post-fill risk and define behavior when approved limits are exceeded.
- [ ] Add account-wide open-risk controls only after a reliable current-SL risk source is defined.
- **Exit gate:** sizing boundaries pass; demo quantities match symbol rules.

## Phase 5 — Initial protection
- [ ] Isolate stop-anchor calculation from broker submission.
- [x] Use the configured fixed-pip fallback when both lower-TF and M5 ATR are unavailable; apply `M5FallbackAtrMultiplier` in ATR recovery. Windows CI passed at `226d7bb`; the latest complete suite passed at `14d2711`.
- [ ] Validate broker distance, tick size, side, and quote side consistently.
- [x] Make SL rejection/unprotected status visible on the volume panel, throttled to avoid repeated warning floods; preserve bounded retry behavior. Verified by Windows CI at `8d519b2` and revalidated at `226d7bb`.
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
- [x] Filter position management and entry-capacity counts by exact cBot label and symbol, avoiding repeated account-wide scans per tick; use a filtered ID lookup because the pinned API's FindById expects int while persisted position IDs are long. Verified by Windows CI at `226d7bb`.
- [ ] Measure CPU/latency with realistic tick and position counts.
- [ ] Run tests, compile, static review, backtest and demo acceptance scenarios.
- [ ] Record known limitations and release checklist.
- **Exit gate:** checks have recorded evidence; no live-readiness claim without demo acceptance.

## Working agreement
Keep each phase small enough to review. Do not combine behavior redesign with mechanical extraction. Record tests actually run—not tests merely planned.


## Audit addendum — entry veto visibility
- [x] Add a separate rate-limited entry-status line so users can distinguish common veto reasons without overwriting protection warnings. Windows CI passed at `8d519b2` and was revalidated after module splitting at `226d7bb` and stop-policy extraction at `14d2711`.
- [x] Pause new entries while position-state or ownership storage is known unhealthy; runtime recovery/storage scenarios remain required.


## Audit addendum — module boundaries
- [x] Split entry execution, signal, structure and scoring methods into separate partial-class modules without changing method bodies. Windows CI passed at `226d7bb` with 69/69 tests; the subsequent stop-policy extraction was validated at `14d2711` with 87/87 tests and 0 warnings/errors.
- [x] Split initial protection, BE/trailing and stop-price geometry into separate partial-class modules; same CI result.
- [ ] Run `scripts/build-cbot.ps1` locally on the current head and perform cTrader demo acceptance for broker/storage behavior.


## Audit addendum — protective stop geometry
- [x] Extract stop-side validation, minimum broker-distance conversion, tick normalization and monotonic stop selection into `ProtectiveStopPolicy`; CI passed at `14d2711` with 87/87 tests, 0 warnings and 0 errors. Local build and broker acceptance pending.


## Audit addendum — verified protective-stop policy
- [x] Latest Windows CI at `14d2711` passed: Release build, 0 warnings/errors, 87/87 NUnit tests. Run: https://github.com/armanemp/CT-TraderBot/actions/runs/39087296809.
- [ ] User to pull and run the current build script locally; broker-side SL acceptance/rejection and restart tests still require cTrader demo validation.


## Audit addendum — source contract gate
- [x] Add scripts/verify-cbot-contract.ps1 to compare legacy/current public parameter contracts and protect the chart-only TP order contract.
- [ ] Validate the new gate in CI and in the user's next local build.
