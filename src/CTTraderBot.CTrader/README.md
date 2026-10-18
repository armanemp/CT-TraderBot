# cTrader integration

This project will contain the `[Robot]` entry point and adapters for symbol/account data, positions/orders, chart objects, timers/events and LocalStorage. It should reference `CTTraderBot.Core`; the core must not reference this project.

Do not add a dummy Robot that appears runnable but has no strategy. Integrate the exact original implementation after its source and baseline compile have been captured. Keep the installed API path a local build property named `CTraderApiPath`; never commit a machine-specific DLL path.
