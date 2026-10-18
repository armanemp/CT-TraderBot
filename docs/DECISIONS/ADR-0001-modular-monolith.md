# ADR-0001: Modular monolith before service extraction

- **Status:** Accepted
- **Context:** The supplied cBot mixes signal generation, sizing, broker execution, protection, chart rendering, persistence and multi-instance ownership in one class. The repository was initially empty.
- **Decision:** Keep one deployable cTrader Robot. First capture the exact source and compile baseline; then split methods into responsibility-based source files without changing behavior; only then extract pure policies and platform adapters incrementally.
- **Why:** A wholesale rewrite before parity is defined makes omissions and regressions hard to find. Mechanical moves are easier to validate, while tested service extraction can follow in smaller changes.
- **Consequences:** Partial classes may be a temporary mechanical-decomposition technique, not the final architecture. New strategy logic must not be appended to the old monolith. Each extraction requires compile and member-inventory checks.
- **Rejected:** Microservices, broad framework adoption, and a simultaneous rewrite of all entry/risk/protection logic.
