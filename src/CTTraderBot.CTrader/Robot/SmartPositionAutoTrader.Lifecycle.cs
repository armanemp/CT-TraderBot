using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        protected override void OnStart()
        {
            if (!ValidateParameters())
            {
                Stop();
                return;
            }

            bool looksLikeGold = SymbolName.IndexOf("XAU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 SymbolName.IndexOf("GOLD", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!AllowNonGoldSymbols && !looksLikeGold)
            {
                Diag("ERROR: This profile is tuned for XAU/GOLD symbols. Enable 'Allow non-gold symbols' only if you intentionally accept non-optimized settings.");
                Stop();
                return;
            }

            if (TimeFrame != cAlgo.API.TimeFrame.Minute5)
            {
                Diag("ERROR: M5 profile requires the M5 chart timeframe. cBot will stop to avoid using mismatched ATR/bar timing.");
                Stop();
                return;
            }

            CleanupPreviousTpGuideObjects();

            _lastTickBid = Symbol.Bid;
            _lastTickAsk = Symbol.Ask;
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _entryFastEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EntryFastEmaPeriod);
            _entrySlowEma = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EntrySlowEmaPeriod);
            _lowerBars = MarketData.GetBars(GetSelectedLowerTimeFrame(), SymbolName);
            _lowerFastEma = Indicators.ExponentialMovingAverage(_lowerBars.ClosePrices, EntryFastEmaPeriod);
            _lowerSlowEma = Indicators.ExponentialMovingAverage(_lowerBars.ClosePrices, EntrySlowEmaPeriod);
            _runtimeVolumeMode = VolumeCalculationMode;
            _runtimeManualVolumeLots = VolumeInLots;
            _runtimeMaxFreeMarginUsagePercent = MaximumFreeMarginUsagePercent;
            _lowerBars.BarOpened += OnLowerTimeframeBarOpened;
            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;
            CreateVolumeControlPanel();

            // OnTick reacts immediately; the timer is a back-up retry path if ticks are sparse.
            Timer.Start(TimeSpan.FromMilliseconds(RetryIntervalMilliseconds));

            Diag("SmartPositionAutoTrader M5 MICRO-SWING PRECISION started: {0} / {1}; lower confirmation {2}; instance {3}; priority {4}.",
                SymbolName, TimeFrame, LowerEntryTimeframe, InstanceId, ManagerPriority);
            Diag("M5 supplies context and target structure; {0} lower-TF bars time entries and locate initial stop anchors.", LowerEntryTimeframe);
            Diag("Lower-TF structural SL is attempted immediately. Profit-locking waits for {0} lower-TF candle closure(s); BE threshold and trailing distance adapt to trend strength.",
                CompletedBarsAfterEntryBeforeProfitLock);
            Diag("Risk budget is opportunity-adaptive: {0:F2}% to {1:F2}% equity, hard ceiling {2:F2}%, minimum guide opportunity {3:F2}R.",
                MinimumOpportunityRiskPercent, MaximumOpportunityRiskPercent, AbsoluteRiskCeilingPercent, MinimumOpportunityRewardR);
            Diag("Volume mode starts at {0}; manual lots {1:F2}; auto mode may use up to {2:F1}% of current free margin, additionally bounded by risk and lot caps.",
                _runtimeVolumeMode, _runtimeManualVolumeLots, _runtimeMaxFreeMarginUsagePercent);
            Diag("TP is chart-guide only: this cBot does not send, set, extend, or modify broker take-profit. You decide when to close manually.");
            Diag("Only positions with this exact cBot label on {0} are managed; manual and other-label positions are excluded.", SymbolName);

            if (ProcessOpenPositionsAtStart)
            {
                ManageCurrentSymbolPositions();
            }
            else
            {
                foreach (var position in Positions)
                {
                    if (position.SymbolName == SymbolName)
                        _ignoredExistingPositionIds.Add(position.Id);
                }
            }
        }

        private bool ValidateParameters()
        {
            if (EntryFastEmaPeriod >= EntrySlowEmaPeriod)
            {
                Diag("ERROR: Fast EMA period must be lower than Slow EMA period.");
                return false;
            }
            if (MinimumAutoVolumeLots > MaximumAutoVolumeLots)
            {
                Diag("ERROR: Minimum auto lot size cannot exceed maximum auto lot size.");
                return false;
            }
            if (MaximumFreeMarginUsagePercent < 5.0 || MaximumFreeMarginUsagePercent > 80.0)
            {
                Diag("ERROR: Auto free-margin usage must be between 5% and 80%, preserving a margin reserve.");
                return false;
            }
            if (!IsFinitePositive(VolumeInLots) || !IsFinitePositive(MaximumAutoVolumeLots))
            {
                Diag("ERROR: Manual and maximum auto lot sizes must be positive.");
                return false;
            }
            if (MinimumOpportunityRiskPercent > MaximumOpportunityRiskPercent)
            {
                Diag("ERROR: Minimum risk budget cannot exceed maximum adaptive risk budget.");
                return false;
            }
            if (AbsoluteRiskCeilingPercent > 0 && MinimumOpportunityRiskPercent > AbsoluteRiskCeilingPercent)
            {
                Diag("ERROR: Minimum risk budget exceeds the emergency risk ceiling.");
                return false;
            }
            if (MinimumAdaptiveSuggestedTpR > MaximumSuggestedTpR)
            {
                Diag("ERROR: Minimum adaptive TP R cannot exceed maximum adaptive TP R.");
                return false;
            }
            if (AdaptiveSuggestedTp && SuggestedTakeProfitR > MaximumSuggestedTpR)
            {
                Diag("ERROR: Base suggested TP R cannot exceed the adaptive TP maximum while adaptive TP is enabled.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(AutoTradeLabel))
            {
                Diag("ERROR: Auto-trade label cannot be blank because it is also used to scope position management.");
                return false;
            }
            if (MaximumOpenPositions < 1 || AtrPeriod < 2 || RetryIntervalMilliseconds < 100)
            {
                Diag("ERROR: One or more essential parameters are outside safe operating bounds.");
                return false;
            }
            return true;
        }

        protected override void OnStop()
        {
            Timer.Stop();
            if (_lowerBars != null)
                _lowerBars.BarOpened -= OnLowerTimeframeBarOpened;
            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;

            // Never release leases from a stale cache. If reload fails, leave keys untouched
            // and let the expiry/recovery path handle them.
            bool refreshed = false;
            try
            {
                LocalStorage.Reload(SharedScope);
                _sharedStorageHealthy = true;
                refreshed = true;
            }
            catch (Exception ex)
            {
                _sharedStorageHealthy = false;
                Diag("Warning: could not refresh shared storage during shutdown; leases will expire naturally: {0}", ex.Message);
            }

            bool changed = false;
            if (refreshed)
            {
                foreach (var positionId in _ownedPositionIds)
                {
                    string key = GetOwnerKey(positionId);
                    OwnershipLease lease;
                    if (TryReadLease(key, out lease) && lease.Owner == InstanceId)
                    {
                        try
                        {
                            LocalStorage.Remove(key, SharedScope);
                            changed = true;
                        }
                        catch (Exception ex)
                        {
                            _sharedStorageHealthy = false;
                            Diag("Warning: could not release lease for position {0}: {1}", positionId, ex.Message);
                        }
                    }

                    if (!_sharedStorageHealthy)
                        break;
                }
            }

            if (changed && _sharedStorageHealthy)
            {
                try
                {
                    LocalStorage.Flush(SharedScope);
                }
                catch (Exception ex)
                {
                    Diag("Warning: could not flush released leases during shutdown: {0}", ex.Message);
                }
            }

            foreach (var positionId in new List<long>(_tpGuidePositionIds))
                RemoveSuggestedTpGuide(positionId);

            if (_volumePanel != null)
            {
                Chart.RemoveControl(_volumePanel);
                _volumePanel = null;
            }
        }

        protected override void OnBar()
        {
            // M5 bars supply trend context and larger structural barriers.
            // Lower-timeframe closed-bar signals and fresh tick crossings determine entry timing.
        }

        private TimeFrame GetSelectedLowerTimeFrame()
        {
            switch (LowerEntryTimeframe)
            {
                case HybridLowerEntryTimeframe.M2:
                    return TimeFrame.Minute2;
                case HybridLowerEntryTimeframe.M3:
                    return TimeFrame.Minute3;
                default:
                    return TimeFrame.Minute;
            }
        }

        private void OnLowerTimeframeBarOpened(BarOpenedEventArgs args)
        {
            if (args == null || args.Bars == null || _lowerBars == null || !ReferenceEquals(args.Bars, _lowerBars))
                return;
            if (_lowerBars.Count < 3)
                return;

            // BarOpened means Last(1) has just completed. Process it once.
            DateTime closedBarOpenTime = _lowerBars.Last(1).OpenTime;
            if (closedBarOpenTime <= _lastProcessedLowerBarOpenTime)
                return;
            _lastProcessedLowerBarOpenTime = closedBarOpenTime;

            // Closed-bar signal is the fallback path; live price-breaks are checked on ticks.
            TryOpenHybridPosition(false);
        }

        protected override void OnTick()
        {
            ManageCurrentSymbolPositions();
            // Live micro-swing breakouts can enter before another lower-TF candle closes.
            // The prior quote is kept for true cross detection; level breaks aren't inferred from stale prices.
            TryOpenHybridPosition(true);
            _lastTickBid = Symbol.Bid;
            _lastTickAsk = Symbol.Ask;
        }

        protected override void OnTimer()
        {
            // Always read fresh account values; Auto sizing itself is also recalculated just before every entry.
            UpdateVolumeControlPanelStatus();
            ManageCurrentSymbolPositions();
        }

        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            var position = args.Position;
            if (ShouldManage(position))
                ManagePosition(position);
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;
            // This cBot never cleans up, changes, or claims a manual position or another cBot's position.
            if (!IsThisBotPosition(position))
                return;

            _states.Remove(position.Id);
            _lastWarningTime.Remove(position.Id);
            _ignoredExistingPositionIds.Remove(position.Id);
            _ownedPositionIds.Remove(position.Id);
            _pendingEmergencyCloseIds.Remove(position.Id);
            _lastEmergencyCloseAttemptTime.Remove(position.Id);
            RemoveSuggestedTpGuide(position.Id);

            try
            {
                LocalStorage.Remove(GetStateKey(position.Id), SharedScope);
                LocalStorage.Remove(GetOwnerKey(position.Id), SharedScope);
                LocalStorage.Flush(SharedScope);
            }
            catch (Exception ex)
            {
                Diag("Warning: position closed, but saved state/lease cleanup failed for position {0}: {1}", position.Id, ex.Message);
                SetVolumeNotice("Position closed, but saved state cleanup failed. A stale lease will expire; review diagnostics.", true);
            }
        }
    }
}
