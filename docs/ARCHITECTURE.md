# Architecture

## Architectural rule

Use a **modular monolith** first. Keep one deployable cTrader Robot, but separate policy from platform effects. Do not introduce microservices, dependency-injection frameworks, or third-party runtime packages without a concrete need.

The reference baseline is stored at `legacy/reference/SmartPositionAutoTrader_M5_MicroSwingPrecision_HedgedVolume.cs`. The first mechanical pass split it into 11 C# files and was compiled in Windows CI and locally by the user. The source-to-local-reference byte-for-byte identity is still unverified. The split partial classes are an intermediate state; the target remains independently testable policies and thin cTrader adapters.

## Target dependency direction

```text
cTrader Robot entry point
  ├─ Entry orchestration
  │   ├─ M5 context policy
  │   ├─ closed-bar setup policy
  │   └─ intrabar breakout policy
  ├─ risk and sizing policy + cTrader symbol/account adapter
  ├─ position protection
  │   ├─ initial SL planning/submission
  │   └─ break-even and trailing policy
  ├─ suggested TP guide (chart-only)
  ├─ position-state repository
  ├─ management-ownership coordinator
  └─ chart UI adapter
```

Domain/policy code must not call `Robot`, `Symbol`, `Account`, `Positions`, `Chart`, `Timer`, or `LocalStorage` directly. Platform adapters translate cTrader state into explicit inputs and translate decisions into broker/chart actions. Avoid abstractions that merely wrap one API call without improving testability or safety.

## Responsibility boundaries

- **Entry:** derive a typed candidate signal from closed-bar or live-crossing inputs; keep hard M5 alignment distinct from a soft context filter.
- **Risk and sizing:** validate risk configuration; calculate budget, cost reserve and candidate volume; distinguish estimated loss from guaranteed loss.
- **Protection:** compute a planned structural stop independently from submitting it; validate side, tick precision and broker minimum distance before modification.
- **Position lifecycle:** own idempotent handling of opened/closed/recovered positions.
- **Target guide:** calculate and render a visual objective only. It must never accidentally become a broker TP.
- **Persistence:** version serialized state, validate it on load, and make failures observable.
- **Multi-instance coordination:** document guarantee level. Device LocalStorage has no compare-and-swap transaction, so a lease built on it is not an atomic lock.
- **UI:** parse and validate user input at the boundary; business logic must not depend on controls.
- **Robot entry point:** wire events, sequence use-cases and translate API results. It must not contain the complete strategy.

## Migration rules

1. Capture the exact original source in `legacy/reference/` unchanged and record its checksum.
2. Establish a baseline compile before behavior changes.
3. Move methods mechanically before redesigning them; preserve bodies and parameter defaults during that pass.
4. Extract pure policies and add tests before changing trade behavior.
5. For each intentional behavior change, add a regression test and decision note.
6. Do not keep duplicate production methods, abandoned variants, patch fragments or version residue.
7. Keep API-dependent code isolated so the core can be tested without cTrader installed.
8. Do not mark a phase complete until acceptance evidence is recorded.

## Naming and contracts

Use names that describe trading intent rather than historical versions. Use explicit result types for expected failure modes. A policy must not claim an order was accepted until the broker result confirms it.

## Safety invariants

- Never silently widen a planned stop to disguise a breached structural level.
- Do not report a position as protected until an accepted broker SL is observed.
- Stop modifications must be monotonic in the protective direction.
- Entry sizing uses symbol volume units normalized to broker rules.
- Distinguish estimated risk from realized/confirmed risk.
- Keep target-guide state separate from broker TP state.
- Scope management by symbol, strategy identity and position identity.
- When state/ownership cannot be trusted, choose and document a fail-safe behavior rather than guessing.
