# Core policies

This directory is reserved for pure domain models and deterministic policies that do not reference `cAlgo.API`. Do not add speculative placeholder classes. A policy belongs here only when it has a clear input/output contract and can be tested independently.

First extractions are expected to cover entry decisions, risk budget, stop-price geometry, target-guide calculations, and persisted-state validation. Broker-specific volume conversion, margin estimation, order submission, chart objects, timers and LocalStorage remain in the cTrader adapter layer.
