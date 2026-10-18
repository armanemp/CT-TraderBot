# Tests

Unit tests should verify deterministic rules without a running cTrader process. Test observable policy contracts and edge boundaries rather than every implementation detail. Broker behavior, chart rendering, timers and multi-instance LocalStorage interactions additionally require cTrader demo acceptance tests in `docs/TESTING.md`.

Do not add an empty test project: the first test project should arrive with the first extracted pure policy and real tests, avoiding a misleading quality signal.
