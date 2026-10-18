// SmartPositionAutoTrader_M5_MicroSwingPrecision_HedgedVolume.cs
// cTrader Automate cBot: automatic entry + protective-stop management. Uses closed-bar reversal/momentum signals; test on demo.
// Hybrid automatic trader: M5 defines the trend/structure; closed M1/M2/M3 bars confirm entry timing.
// Hedged-volume revision: adds chart-side Manual/Auto sizing controls, live free-margin sizing, and exact robot-label isolation for independently managed Hedged-account positions.
// Sends a protective broker SL, but NEVER sends or modifies a broker TP. The adaptive TP is a chart-only guide for manual decisions.
// Dedicated XAUUSD M5 chart profile; attach only to an M5 chart. Routine logs and entry markers are off by default; test on demo before live use.

using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public enum HybridLowerEntryTimeframe
    {
        M1,
        M2,
        M3
    }

    public enum MicroSwingPrecisionVolumeMode
    {
        ManualLots,
        AutoFreeMargin
    }

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume : Robot
    {
        [Parameter("Enable automatic entries", Group = "Auto entry", DefaultValue = true)]
        public bool EnableAutomaticEntries { get; set; }

        [Parameter("Lower-TF entry confirmation", Group = "Hybrid entry", DefaultValue = HybridLowerEntryTimeframe.M1)]
        public HybridLowerEntryTimeframe LowerEntryTimeframe { get; set; }

        [Parameter("Require lower-TF EMA alignment for momentum", Group = "Hybrid entry", DefaultValue = false)]
        public bool RequireLowerEmaAlignment { get; set; }

        [Parameter("M5 trend alignment required (otherwise soft context filter)", Group = "Hybrid entry", DefaultValue = false)]
        public bool RequireM5TrendAlignment { get; set; }

        [Parameter("Use live micro-swing breakout entry", Group = "Hybrid entry", DefaultValue = true)]
        public bool UseIntrabarMicroSwingBreakout { get; set; }

        [Parameter("Micro-swing breakout buffer (lower-TF ATR)", Group = "Hybrid entry", DefaultValue = 0.02, MinValue = 0.0, MaxValue = 0.3)]
        public double MicroSwingBreakoutBufferAtr { get; set; }

        [Parameter("Auto trade label (dedicated to this cBot)", Group = "Auto entry", DefaultValue = "SPG-MICRO-M5-PRECISION-AUTO")]
        public string AutoTradeLabel { get; set; }

        [Parameter("Initial volume mode", Group = "Auto entry / volume", DefaultValue = MicroSwingPrecisionVolumeMode.AutoFreeMargin)]
        public MicroSwingPrecisionVolumeMode VolumeCalculationMode { get; set; }

        [Parameter("Manual volume (lots; editable on chart)", Group = "Auto entry / volume", DefaultValue = 1.0, MinValue = 0.01, MaxValue = 100)]
        public double VolumeInLots { get; set; }

        [Parameter("Maximum auto volume (lots)", Group = "Auto entry / volume", DefaultValue = 10.0, MinValue = 0.01, MaxValue = 100)]
        public double MaximumAutoVolumeLots { get; set; }

        [Parameter("Minimum auto volume (lots)", Group = "Auto entry / volume", DefaultValue = 0.01, MinValue = 0.01, MaxValue = 100)]
        public double MinimumAutoVolumeLots { get; set; }

        [Parameter("Maximum free-margin usage in Auto mode (%)", Group = "Auto entry / volume", DefaultValue = 40.0, MinValue = 5.0, MaxValue = 80.0)]
        public double MaximumFreeMarginUsagePercent { get; set; }

        [Parameter("Maximum open positions", Group = "Auto entry", DefaultValue = 1, MinValue = 1, MaxValue = 10)]
        public int MaximumOpenPositions { get; set; }

        [Parameter("Lower-TF micro-structure lookback bars", Group = "Auto entry", DefaultValue = 6, MinValue = 3, MaxValue = 12)]
        public int EntryStructureLookbackBars { get; set; }

        [Parameter("Fast EMA", Group = "Auto entry", DefaultValue = 9, MinValue = 2, MaxValue = 50)]
        public int EntryFastEmaPeriod { get; set; }

        [Parameter("Slow EMA", Group = "Auto entry", DefaultValue = 21, MinValue = 3, MaxValue = 100)]
        public int EntrySlowEmaPeriod { get; set; }

        [Parameter("Maximum spread (pips; 0 disables)", Group = "Auto entry", DefaultValue = 60, MinValue = 0, MaxValue = 10000)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Maximum initial risk (lower-TF ATR)", Group = "Auto entry", DefaultValue = 4.5, MinValue = 0.5, MaxValue = 10)]
        public double MaximumEntryRiskAtr { get; set; }

        [Parameter("Enable opportunity-adaptive risk budget", Group = "Auto entry / risk", DefaultValue = true)]
        public bool EnableAdaptiveRiskBudget { get; set; }

        [Parameter("Minimum estimated structural reward/risk (R)", Group = "Auto entry / risk", DefaultValue = 1.20, MinValue = 0.5, MaxValue = 10.0)]
        public double MinimumOpportunityRewardR { get; set; }

        [Parameter("Minimum setup risk budget (% equity)", Group = "Auto entry / risk", DefaultValue = 0.50, MinValue = 0.0, MaxValue = 20.0)]
        public double MinimumOpportunityRiskPercent { get; set; }

        [Parameter("Maximum setup risk budget (% equity)", Group = "Auto entry / risk", DefaultValue = 1.50, MinValue = 0.0, MaxValue = 20.0)]
        public double MaximumOpportunityRiskPercent { get; set; }

        [Parameter("Hard risk ceiling (% equity; 0 disables)", Group = "Auto entry / risk", DefaultValue = 1.50, MinValue = 0.0, MaxValue = 30.0)]
        public double AbsoluteRiskCeilingPercent { get; set; }

        [Parameter("Minimum composite setup quality (0-1)", Group = "Auto entry / risk", DefaultValue = 0.43, MinValue = 0.0, MaxValue = 1.0)]
        public double MinimumEntryQualityScore { get; set; }

        [Parameter("Minimum M5 directional strength (0-1)", Group = "Auto entry / risk", DefaultValue = 0.20, MinValue = 0.0, MaxValue = 1.0)]
        public double MinimumM5TrendStrength { get; set; }

        [Parameter("Spread-to-M5-ATR maximum ratio (0 disables)", Group = "Auto entry", DefaultValue = 0.18, MinValue = 0.0, MaxValue = 1.0)]
        public double MaximumSpreadToAtrRatio { get; set; }

        [Parameter("Maximum entry distance from M5 fast EMA (ATR; 0 disables)", Group = "Auto entry", DefaultValue = 1.75, MinValue = 0.0, MaxValue = 10.0)]
        public double MaximumEntryExtensionAtr { get; set; }

        [Parameter("Extra sizing allowance for slippage (pips)", Group = "Auto entry / risk", DefaultValue = 2.0, MinValue = 0.0, MaxValue = 1000.0)]
        public double SizingSlippageReservePips { get; set; }

        [Parameter("Entry cooldown lower-TF bars", Group = "Auto entry", DefaultValue = 1, MinValue = 0, MaxValue = 100)]
        public int EntryCooldownBars { get; set; }

        [Parameter("Draw entry signal marker", Group = "Auto entry", DefaultValue = false)]
        public bool DrawEntrySignalMarker { get; set; }

        [Parameter("lower-TF spread buffer multiplier", Group = "lower-TF wick stop", DefaultValue = 1.1, MinValue = 1.0, MaxValue = 3.0)]
        public double M5SpreadBufferMultiplier { get; set; }

        [Parameter("lower-TF ATR buffer fraction", Group = "lower-TF wick stop", DefaultValue = 0.12, MinValue = 0.0, MaxValue = 0.5)]
        public double M5AtrBufferFraction { get; set; }

        [Parameter("lower-TF fallback ATR multiplier", Group = "lower-TF wick stop", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0)]
        public double M5FallbackAtrMultiplier { get; set; }

        [Parameter("Warn if initial SL exceeds (ATR)", Group = "Initial protection", DefaultValue = 3.5, MinValue = 0.5, MaxValue = 10.0)]
        public double MaximumInitialStopAtr { get; set; }

        [Parameter("Show suggested TP guide (not a real TP)", Group = "Suggested TP guide", DefaultValue = true)]
        public bool ShowSuggestedTpGuide { get; set; }

        [Parameter("Adaptive TP by trend strength", Group = "Suggested TP guide", DefaultValue = true)]
        public bool AdaptiveSuggestedTp { get; set; }

        [Parameter("Minimum adaptive TP target (R)", Group = "Suggested TP guide", DefaultValue = 0.75, MinValue = 0.25, MaxValue = 8.0)]
        public double MinimumAdaptiveSuggestedTpR { get; set; }

        [Parameter("Maximum adaptive TP target (R)", Group = "Suggested TP guide", DefaultValue = 30.0, MinValue = 1.0, MaxValue = 30.0)]
        public double MaximumSuggestedTpR { get; set; }

        [Parameter("Base TP R reference for entry screening", Group = "Suggested TP guide", DefaultValue = 2.0, MinValue = 0.5, MaxValue = 8.0)]
        public double SuggestedTakeProfitR { get; set; }

        [Parameter("TP structure lookback bars", Group = "Suggested TP guide", DefaultValue = 12, MinValue = 3, MaxValue = 50)]
        public int SuggestedTpStructureLookbackBars { get; set; }

        [Parameter("TP structure buffer (ATR fraction)", Group = "Suggested TP guide", DefaultValue = 0.10, MinValue = 0.0, MaxValue = 1.0)]
        public double SuggestedTpStructureBufferAtr { get; set; }

        [Parameter("TP guide length (bars)", Group = "Suggested TP guide", DefaultValue = 8, MinValue = 1, MaxValue = 20)]
        public int SuggestedTpLineLengthBars { get; set; }

        [Parameter("Allow non-gold symbols (not optimized)", Group = "General / safety", DefaultValue = false)]
        public bool AllowNonGoldSymbols { get; set; }

        [Parameter("Process existing positions at start", Group = "General", DefaultValue = true)]
        public bool ProcessOpenPositionsAtStart { get; set; }

        [Parameter("Manager priority (higher wins)", Group = "General / Multi-instance", DefaultValue = 0, MinValue = -100, MaxValue = 100)]
        public int ManagerPriority { get; set; }

        [Parameter("Ownership lease (seconds)", Group = "General / Multi-instance", DefaultValue = 8, MinValue = 3, MaxValue = 60)]
        public int OwnershipLeaseSeconds { get; set; }

        [Parameter("ATR period", Group = "Initial protection", DefaultValue = 14, MinValue = 2, MaxValue = 100)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR multiplier for R recovery after restart", Group = "State recovery", DefaultValue = 1.5, MinValue = 0.2, MaxValue = 10.0)]
        public double InitialStopAtrMultiplier { get; set; }

        [Parameter("Structural wick lookback bars", Group = "Initial protection", DefaultValue = 3, MinValue = 1, MaxValue = 8)]
        public int StructuralWickLookbackBars { get; set; }

        [Parameter("Fallback SL if ATR unavailable (pips)", Group = "Initial protection", DefaultValue = 100, MinValue = 1, MaxValue = 100000)]
        public double FallbackStopPips { get; set; }

        [Parameter("Extra broker distance safety (ticks)", Group = "Execution", DefaultValue = 1, MinValue = 0, MaxValue = 20)]
        public double BrokerDistanceSafetyTicks { get; set; }

        [Parameter("Completed lower-TF candle closures before profit-lock", Group = "Break-even / timing", DefaultValue = 1, MinValue = 0, MaxValue = 10)]
        public int CompletedBarsAfterEntryBeforeProfitLock { get; set; }

        [Parameter("Base break-even trigger (R; used if adaptive is off)", Group = "Break-even / timing", DefaultValue = 0.75, MinValue = 0.1, MaxValue = 5.0)]
        public double BreakEvenTriggerR { get; set; }

        [Parameter("Adaptive BE timing by trend strength", Group = "Break-even / timing", DefaultValue = true)]
        public bool AdaptiveBreakEvenTiming { get; set; }

        [Parameter("Early BE trigger when momentum weak (R)", Group = "Break-even / timing", DefaultValue = 0.55, MinValue = 0.1, MaxValue = 5.0)]
        public double EarlyBreakEvenTriggerR { get; set; }

        [Parameter("BE trigger when trend is strong (R)", Group = "Break-even / timing", DefaultValue = 0.95, MinValue = 0.1, MaxValue = 5.0)]
        public double StrongTrendBreakEvenTriggerR { get; set; }

        [Parameter("Minimum net BE cushion (pips)", Group = "Break-even / timing", DefaultValue = 1.5, MinValue = 0.0, MaxValue = 1000.0)]
        public double BreakEvenBufferPips { get; set; }

        [Parameter("Extra BE execution cushion (pips)", Group = "Break-even / timing", DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1000.0)]
        public double BreakEvenExtraSafetyPips { get; set; }

        [Parameter("Trailing start (R; adaptive)", Group = "Smart trailing", DefaultValue = 1.10, MinValue = 0.2, MaxValue = 10.0)]
        public double TrailingStartR { get; set; }

        [Parameter("Trailing distance (ATR; adaptive)", Group = "Smart trailing", DefaultValue = 1.10, MinValue = 0.2, MaxValue = 10.0)]
        public double TrailingAtrMultiplier { get; set; }

        [Parameter("Trailing minimum profit lock (R)", Group = "Smart trailing", DefaultValue = 0.1, MinValue = 0.0, MaxValue = 5.0)]
        public double MinimumProfitLockR { get; set; }

        [Parameter("Structure lookback bars for trailing", Group = "Smart trailing", DefaultValue = 3, MinValue = 1, MaxValue = 8)]
        public int TrailingStructureLookbackBars { get; set; }

        [Parameter("Structure trailing buffer (ATR fraction)", Group = "Smart trailing", DefaultValue = 0.10, MinValue = 0.0, MaxValue = 1.0)]
        public double TrailingStructureAtrBufferFraction { get; set; }

        [Parameter("Minimum SL update step (pips)", Group = "Smart trailing", DefaultValue = 2.0, MinValue = 0.0, MaxValue = 1000.0)]
        public double MinimumStopUpdateStepPips { get; set; }

        [Parameter("Retry interval (milliseconds)", Group = "Execution", DefaultValue = 500, MinValue = 100, MaxValue = 5000)]
        public int RetryIntervalMilliseconds { get; set; }

        [Parameter("Close if intended initial SL was crossed", Group = "Execution / emergency", DefaultValue = true)]
        public bool CloseIfInitialStopAlreadyCrossed { get; set; }

        [Parameter("Enable diagnostic log output", Group = "General / diagnostics", DefaultValue = false)]
        public bool EnableDiagnosticLogging { get; set; }

        private AverageTrueRange _atr;
        private ExponentialMovingAverage _entryFastEma;
        private ExponentialMovingAverage _entrySlowEma;
        private Bars _lowerBars;
        private ExponentialMovingAverage _lowerFastEma;
        private ExponentialMovingAverage _lowerSlowEma;
        private DateTime _lastProcessedLowerBarOpenTime = DateTime.MinValue;
        private int _lastEntryBarIndex = -1000000;
        private DateTime _lastEntryAttemptLowerBarOpenTime = DateTime.MinValue;
        private double _lastTickBid;
        private double _lastTickAsk;

        private readonly Dictionary<long, PositionState> _states = new Dictionary<long, PositionState>();
        private readonly Dictionary<long, DateTime> _lastWarningTime = new Dictionary<long, DateTime>();
        private readonly HashSet<long> _ignoredExistingPositionIds = new HashSet<long>();
        private readonly HashSet<long> _ownedPositionIds = new HashSet<long>();
        private readonly HashSet<long> _pendingEmergencyCloseIds = new HashSet<long>();
        private readonly Dictionary<long, DateTime> _lastEmergencyCloseAttemptTime = new Dictionary<long, DateTime>();
        private readonly HashSet<long> _tpGuidePositionIds = new HashSet<long>();
        private DateTime _lastSharedStorageReloadUtc = DateTime.MinValue;

        // Runtime volume settings can be switched from the on-chart panel without restarting the cBot.
        private MicroSwingPrecisionVolumeMode _runtimeVolumeMode;
        private double _runtimeManualVolumeLots;
        private double _runtimeMaxFreeMarginUsagePercent;
        private StackPanel _volumePanel;
        private TextBox _manualVolumeTextBox;
        private TextBox _freeMarginUsageTextBox;
        private Button _manualModeButton;
        private Button _autoModeButton;
        private TextBlock _volumeModeStatusText;
        private TextBlock _volumeAccountStatusText;
        private TextBlock _volumeNoticeText;

        private const LocalStorageScope SharedScope = LocalStorageScope.Device;

        // Silent by default: the cBot runs autonomously without routine logs/popups.
        // Enable diagnostics only when investigating a specific issue.
        private void Diag(string format, params object[] args)
        {
            if (EnableDiagnosticLogging)
                Print(format, args);
        }

        private sealed class PositionState
        {
            public double InitialRiskDistance;
            public bool RiskDistanceEstimated;
            public double LastSuggestedTpPrice;
            public bool LastSuggestedTpReached;
            public double LastTrendScore;
            public int LastTpDrawBarIndex = -1;
        }

        private sealed class OwnershipLease
        {
            public string Owner;
            public int Priority;
            public long ExpiryTicks;
        }

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

            // Refresh device-scoped storage before lease release so a stale cache cannot erase another owner's lease.
            try
            {
                LocalStorage.Reload(SharedScope);
            }
            catch (Exception ex)
            {
                Diag("Warning: could not refresh shared storage during shutdown: {0}", ex.Message);
            }

            // Release only leases still owned by this instance; leave initial-risk state intact.
            bool changed = false;
            foreach (var positionId in _ownedPositionIds)
            {
                string key = GetOwnerKey(positionId);
                OwnershipLease lease;
                if (TryReadLease(key, out lease) && lease.Owner == InstanceId)
                {
                    LocalStorage.Remove(key, SharedScope);
                    changed = true;
                }
            }

            if (changed)
                LocalStorage.Flush(SharedScope);

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

        private void TryOpenHybridPosition(bool allowIntrabarBreakout)
        {
            if (!EnableAutomaticEntries || Bars == null || _lowerBars == null ||
                Bars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10))
                return;

            DateTime activeLowerBarTime = _lowerBars.LastBar.OpenTime;
            if (activeLowerBarTime <= _lastEntryAttemptLowerBarOpenTime)
                return;

            // Cooldown is expressed in LOWER-timeframe bars, not M5 bars.
            if (EntryCooldownBars > 0 && _lowerBars.Count - _lastEntryBarIndex < EntryCooldownBars)
                return;

            int openCount = 0;
            foreach (var position in Positions)
            {
                if (position.SymbolName == SymbolName &&
                    string.Equals(position.Label, AutoTradeLabel, StringComparison.Ordinal))
                    openCount++;
            }
            if (openCount >= MaximumOpenPositions)
                return;

            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            if (MaximumSpreadPips > 0 && spreadPips > MaximumSpreadPips)
                return;

            TradeType? signal = GetHybridEntrySignal(allowIntrabarBreakout && UseIntrabarMicroSwingBreakout);
            if (!signal.HasValue)
                return;

            // Run remaining checks before consuming this lower-TF candle. If a closed-bar pattern
            // fails sizing/quality, a later fresh micro-swing cross in the same candle may still qualify.
            double atr = GetAtrValue();
            double lowerAtr = GetLowerAtrValue();
            if (atr <= 0 || lowerAtr <= 0)
                return;

            double spread = Math.Max(0, Symbol.Ask - Symbol.Bid);
            double spreadAtrRatio = atr > 0 ? spread / atr : double.MaxValue;
            if (MaximumSpreadToAtrRatio > 0 && spreadAtrRatio > MaximumSpreadToAtrRatio)
                return;

            double trendStrengthBeforeEntry = GetM5DirectionalStrengthScore(signal.Value, atr);
            // This is a soft quality check. A confirmed lower-TF structure break can override it,
            // so an early reversal is not rejected only because M5 has not turned yet.
            bool confirmedLowerShift = IsConfirmedLowerStructureBreak(signal.Value);
            if (trendStrengthBeforeEntry < MinimumM5TrendStrength && !confirmedLowerShift)
                return;

            double entryReference = signal.Value == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double fastEmaReference = _entryFastEma.Result.Last(1);
            double entryExtension = Math.Abs(entryReference - fastEmaReference) / atr;
            if (MaximumEntryExtensionAtr > 0 && entryExtension > MaximumEntryExtensionAtr)
                return;

            // The first stop anchor is LOWER-timeframe market structure, not a wide M5 candle range.
            double buffer = Math.Max(2 * Symbol.TickSize,
                Math.Max(spread * Math.Max(1.0, M5SpreadBufferMultiplier), lowerAtr * 0.10));
            int lookback = Math.Max(3, Math.Min(EntryStructureLookbackBars, _lowerBars.Count - 2));
            double structuralExtreme = GetLowerStructuralStopAnchor(signal.Value, lookback);
            if (!IsFinitePositive(structuralExtreme))
                return;

            double plannedStop = signal.Value == TradeType.Buy ? structuralExtreme - buffer : structuralExtreme + buffer;
            bool correctSide = signal.Value == TradeType.Buy
                ? plannedStop < entryReference - Symbol.TickSize
                : plannedStop > entryReference + Symbol.TickSize;
            if (!correctSide)
                return; // Never fabricate an artificially tight stop if local structure is invalid.

            double minDistance = GetMinimumStopDistance(signal.Value == TradeType.Buy ? Symbol.Bid : Symbol.Ask);
            if (signal.Value == TradeType.Buy)
                plannedStop = Math.Min(plannedStop, Symbol.Bid - minDistance);
            else
                plannedStop = Math.Max(plannedStop, Symbol.Ask + minDistance);

            double stopDistance = Math.Abs(entryReference - plannedStop);
            if (stopDistance <= Symbol.TickSize || stopDistance > lowerAtr * MaximumEntryRiskAtr)
                return;

            double stopPips = stopDistance / Symbol.PipSize;
            double targetR = GetEntryTargetR(signal.Value, atr, entryReference, stopDistance); // visual guide only
            if (targetR < MinimumOpportunityRewardR)
                return;

            double setupQuality = GetOpportunityQualityScore(signal.Value, atr, targetR);
            // A lower-TF structural break can create an excellent early entry even before M5 strength peaks.
            if (setupQuality < MinimumEntryQualityScore)
                return;

            double allowedRiskPercent = GetAdaptiveAllowedRiskPercent(setupQuality);
            double equity = Account.Equity;
            if (!IsFinitePositive(equity) || allowedRiskPercent <= 0)
                return;

            // Risk budget is recalculated from current equity on every potential entry.
            double maximumAllowedLoss = equity * allowedRiskPercent / 100.0;
            double riskSizingPips = stopPips + Math.Max(0.0, SizingSlippageReservePips);
            if (!IsFinitePositive(maximumAllowedLoss) || !IsFinitePositive(riskSizingPips))
                return;

            double volume;
            double freeMarginNow = Math.Max(0.0, Account.FreeMargin);
            if (_runtimeVolumeMode == MicroSwingPrecisionVolumeMode.AutoFreeMargin)
            {
                double maxLots = Math.Min(MaximumAutoVolumeLots,
                    Symbol.VolumeInUnitsMax > 0 ? Symbol.VolumeInUnitsToQuantity(Symbol.VolumeInUnitsMax) : MaximumAutoVolumeLots);
                double maxUnits = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(maxLots), RoundingMode.Down);
                double marginBudget = freeMarginNow * _runtimeMaxFreeMarginUsagePercent / 100.0;
                double marginLimitedUnits = GetMaximumVolumeWithinMargin(signal.Value, maxUnits, marginBudget);
                if (marginLimitedUnits < Symbol.VolumeInUnitsMin)
                {
                    SetVolumeNotice("Auto mode: free margin is insufficient for the minimum permitted volume.", true);
                    return;
                }

                double lossAtMarginLimit = Symbol.AmountRisked(marginLimitedUnits, riskSizingPips);
                if (!IsFinitePositive(lossAtMarginLimit))
                    return;

                double riskLimitedUnits = marginLimitedUnits * Math.Min(1.0, maximumAllowedLoss / lossAtMarginLimit);
                volume = Symbol.NormalizeVolumeInUnits(riskLimitedUnits, RoundingMode.Down);
                double minAutoUnits = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MinimumAutoVolumeLots), RoundingMode.Up);
                if (volume < Math.Max(Symbol.VolumeInUnitsMin, minAutoUnits))
                {
                    SetVolumeNotice("Auto mode: stop distance and risk limit do not support the configured minimum volume.", true);
                    return;
                }
            }
            else
            {
                // Manual keeps the user-selected lots, but applies the hard risk ceiling (not the
                // lower setup-adaptive auto budget). If the chosen size exceeds that ceiling, skip
                // the entry instead of silently reducing the user's requested volume.
                double manualRiskCeilingPercent = Math.Max(0.0, MaximumOpportunityRiskPercent);
                if (AbsoluteRiskCeilingPercent > 0.0)
                    manualRiskCeilingPercent = Math.Min(manualRiskCeilingPercent, AbsoluteRiskCeilingPercent);
                maximumAllowedLoss = equity * manualRiskCeilingPercent / 100.0;
                double requestedUnits = Symbol.QuantityToVolumeInUnits(_runtimeManualVolumeLots);
                volume = Symbol.NormalizeVolumeInUnits(requestedUnits, RoundingMode.Down);
                if (volume < Symbol.VolumeInUnitsMin || volume > Symbol.VolumeInUnitsMax ||
                    Math.Abs(volume - requestedUnits) > Math.Max(0.000001, Symbol.VolumeInUnitsStep * 0.000001))
                {
                    SetVolumeNotice("Manual mode: selected lot size is outside this symbol's allowed volume range/step.", true);
                    return;
                }
            }

            double estimatedLoss = Symbol.AmountRisked(volume, riskSizingPips);
            if (!IsFinitePositive(estimatedLoss))
                return;
            if (estimatedLoss > maximumAllowedLoss * 1.001)
            {
                SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                    "Entry skipped: selected/calculated volume risks about {0:F2} {1}, above this setup's {2:F2} risk budget.",
                    estimatedLoss, Account.Asset.Name, maximumAllowedLoss), true);
                return;
            }

            // Re-check live margin immediately before sending. Manual uses the requested volume
            // with a 10% free-margin reserve; auto keeps the user-configured reserve.
            double requiredMargin = GetEstimatedMarginSafe(signal.Value, volume);
            double finalFreeMargin = Math.Max(0.0, Account.FreeMargin);
            double finalMarginBudget = _runtimeVolumeMode == MicroSwingPrecisionVolumeMode.AutoFreeMargin
                ? finalFreeMargin * _runtimeMaxFreeMarginUsagePercent / 100.0
                : finalFreeMargin * 0.90;
            if (!IsFinitePositive(requiredMargin) || requiredMargin > finalMarginBudget)
            {
                SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                    "Entry skipped: estimated margin {0:F2} exceeds the permitted current margin budget {1:F2}.",
                    requiredMargin, finalMarginBudget), true);
                return;
            }

            SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                "Ready: {0:F2} lots | estimated SL risk {1:F2} {2} | required margin {3:F2}.",
                Symbol.VolumeInUnitsToQuantity(volume), estimatedLoss, Account.Asset.Name, requiredMargin), false);

            string comment = "M5+" + LowerEntryTimeframe.ToString() + " precision | " + _runtimeVolumeMode.ToString() + " | TP guide only";
            // Consume the candle only when sending the order; filter failures do not block a later fresh trigger.
            _lastEntryAttemptLowerBarOpenTime = activeLowerBarTime;
            // Real broker SL is sent; TP is explicitly null because the target is a chart-only guide.
            var result = ExecuteMarketOrder(signal.Value, SymbolName, volume, AutoTradeLabel, stopPips, null, comment);
            if (!result.IsSuccessful)
            {
                SetVolumeNotice("Broker rejected the order: " + result.Error.ToString(), true);
                return;
            }

            _lastEntryBarIndex = _lowerBars.Count;
            SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                "Opened {0:F2} lots. Initial SL is attached; TP remains guide-only.",
                Symbol.VolumeInUnitsToQuantity(volume)), false);

            if (result.Position != null)
            {
                AlignFilledPositionStopToStructure(result.Position, plannedStop);
                if (!IsPositionStillOpen(result.Position.Id) || _pendingEmergencyCloseIds.Contains(result.Position.Id))
                    return;

                bool hasValidActualStop = result.Position.StopLoss.HasValue && IsStopOnRiskSide(result.Position);
                double actualRiskDistance = hasValidActualStop
                    ? Math.Abs(result.Position.EntryPrice - result.Position.StopLoss.Value)
                    : stopDistance;
                if (hasValidActualStop)
                {
                    double postFillRiskPips = actualRiskDistance / Symbol.PipSize + Math.Max(0.0, SizingSlippageReservePips);
                    double postFillEstimatedLoss = Symbol.AmountRisked(volume, postFillRiskPips);
                    if (IsFinitePositive(postFillEstimatedLoss) && postFillEstimatedLoss > maximumAllowedLoss * 1.05)
                    {
                        // AmountRisked is an estimate and the trade is already live. Never liquidate
                        // solely because an estimated post-fill risk changed; keep the real broker SL.
                        // Use the diagnostic switch to inspect exceptional slippage/broker-distance cases.
                        Diag("Post-fill stop geometry increased estimated risk to {0:F2} {1} ({2:F2}% equity) versus planned budget {3:F2}%.",
                            postFillEstimatedLoss, Account.Asset.Name, postFillEstimatedLoss / equity * 100.0, allowedRiskPercent);
                    }
                }
                var entryState = new PositionState
                {
                    InitialRiskDistance = actualRiskDistance,
                    RiskDistanceEstimated = !hasValidActualStop,
                    LastSuggestedTpPrice = 0,
                    LastTpDrawBarIndex = -1
                };
                _states[result.Position.Id] = entryState;
                SaveState(result.Position, entryState);
            }

            if (DrawEntrySignalMarker)
            {
                var signalBar = _lowerBars.Last(1);
                string markerName = "SPG M5 MicroSwing HV Entry " + signalBar.OpenTime.Ticks.ToString(CultureInfo.InvariantCulture);
                double markerPrice = signal.Value == TradeType.Buy ? signalBar.Low - buffer : signalBar.High + buffer;
                var markerText = Chart.DrawText(markerName,
                    (signal.Value == TradeType.Buy ? "BUY" : "SELL") + " · " + LowerEntryTimeframe.ToString(),
                    signalBar.OpenTime, markerPrice, signal.Value == TradeType.Buy ? Color.Lime : Color.OrangeRed);
                markerText.FontSize = 9;
                markerText.IsInteractive = false;
            }
        }

        private void AlignFilledPositionStopToStructure(Position position, double plannedStop)
        {
            if (position == null || !IsFinitePositive(plannedStop))
                return;

            double desiredStop = NormalizeStopPrice(plannedStop, position.TradeType, false);
            bool onCorrectLossSide = position.TradeType == TradeType.Buy
                ? desiredStop < position.EntryPrice - Symbol.TickSize * 0.5
                : desiredStop > position.EntryPrice + Symbol.TickSize * 0.5;
            if (!onCorrectLossSide)
            {
                WarnThrottled(position, "Post-fill structural SL was not aligned because the planned price is no longer on the loss side of the actual fill. Existing broker SL is retained.");
                return;
            }

            bool plannedLevelAlreadyCrossed = position.TradeType == TradeType.Buy
                ? Symbol.Bid <= desiredStop
                : Symbol.Ask >= desiredStop;
            if (plannedLevelAlreadyCrossed)
            {
                WarnThrottled(position, "Price crossed the intended absolute structural SL during/just after entry. The bot will not move the stop farther away to disguise the breach.");
                if (CloseIfInitialStopAlreadyCrossed)
                {
                    _pendingEmergencyCloseIds.Add(position.Id);
                    TryCloseBreachedPosition(position);
                }
                return;
            }

            double minDistance = GetMinimumStopDistance(position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask);
            bool brokerDistanceValid = position.TradeType == TradeType.Buy
                ? desiredStop <= Symbol.Bid - minDistance
                : desiredStop >= Symbol.Ask + minDistance;
            if (!brokerDistanceValid)
            {
                WarnThrottled(position, "Broker minimum stop distance prevented exact post-fill structural alignment. The attached broker SL is retained; risk may differ from the planned wick level.");
                return;
            }

            if (position.StopLoss.HasValue && Math.Abs(position.StopLoss.Value - desiredStop) <= Symbol.TickSize * 0.5)
                return;

            var result = position.ModifyStopLossPrice(desiredStop);
            if (!result.IsSuccessful)
            {
                WarnThrottled(position, "Broker rejected post-fill absolute structural SL alignment. The existing attached SL is retained if present; check the position immediately.");
                return;
            }

            Diag("Post-fill SL aligned to absolute lower-timeframe structure | position {0} | SL {1}",
                position.Id, desiredStop.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture));
        }

        private TradeType? GetHybridEntrySignal(bool allowIntrabarBreakout)
        {
            TradeType? m5Bias = GetM5TrendBias();
            if (RequireM5TrendAlignment && !m5Bias.HasValue)
                return null;

            TradeType? lowerSignal = allowIntrabarBreakout
                ? GetLowerMicroSwingBreakoutSignal()
                : GetLowerTimeframeSignal();
            if (!lowerSignal.HasValue)
                return null;

            // Keep strong M5 trends as the dominant context, but allow lower-TF structure shifts
            // against a weak/transitioning M5 bias so the cBot does not become excessively late.
            if (m5Bias.HasValue && lowerSignal.Value != m5Bias.Value)
            {
                double atr = GetAtrValue();
                double opposingM5Strength = atr > 0 ? GetM5DirectionalStrengthScore(m5Bias.Value, atr) : 0.0;
                bool confirmedLowerShift = IsConfirmedLowerStructureBreak(lowerSignal.Value);
                double lowerScore = GetLowerSignalQualityScore(lowerSignal.Value);
                if (opposingM5Strength >= 0.62 && !confirmedLowerShift)
                    return null;
                if (opposingM5Strength >= 0.45 && !confirmedLowerShift && lowerScore < 0.78)
                    return null;
            }
            return lowerSignal;
        }

        private TradeType? GetM5TrendBias()
        {
            if (Bars == null || Bars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _entryFastEma == null || _entrySlowEma == null)
                return null;

            double fast = _entryFastEma.Result.Last(1);
            double slow = _entrySlowEma.Result.Last(1);
            double priorFast = _entryFastEma.Result.Last(2);
            double close = Bars.Last(1).Close;

            bool bullish = fast > slow && close >= slow && fast >= priorFast;
            bool bearish = fast < slow && close <= slow && fast <= priorFast;
            if (bullish == bearish)
                return null;
            return bullish ? TradeType.Buy : TradeType.Sell;
        }

        private TradeType? GetLowerTimeframeSignal()
        {
            if (_lowerBars == null || _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _lowerFastEma == null || _lowerSlowEma == null)
                return null;

            var c = _lowerBars.Last(1);
            var p = _lowerBars.Last(2);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double body = Math.Max(Symbol.TickSize, Math.Abs(c.Close - c.Open));
            double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
            double upperWick = c.High - Math.Max(c.Open, c.Close);
            double closeLocation = (c.Close - c.Low) / range;
            double bodyRatio = body / range;
            double lowerAtr = GetLowerAtrValue();
            if (lowerAtr <= 0)
                return null;

            bool bullishRejection = c.Close > c.Open && lowerWick >= body * 0.75 &&
                                    closeLocation >= 0.58 && c.Close >= p.Close;
            bool bearishRejection = c.Close < c.Open && upperWick >= body * 0.75 &&
                                    closeLocation <= 0.42 && c.Close <= p.Close;
            bool bullishEngulf = c.Close > c.Open && p.Close < p.Open && c.Close >= p.Open && c.Open <= p.Close;
            bool bearishEngulf = c.Close < c.Open && p.Close > p.Open && c.Close <= p.Open && c.Open >= p.Close;
            bool bullishMomentum = c.Close > c.Open && c.Close > p.High && bodyRatio >= 0.30 && closeLocation >= 0.62;
            bool bearishMomentum = c.Close < c.Open && c.Close < p.Low && bodyRatio >= 0.30 && closeLocation <= 0.38;

            double fast = _lowerFastEma.Result.Last(1);
            double slow = _lowerSlowEma.Result.Last(1);
            bool bullishEma = fast >= slow && c.Close >= fast;
            bool bearishEma = fast <= slow && c.Close <= fast;

            double previousSwingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars, true);
            double previousSwingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars, true);
            double breakBuffer = Math.Max(Symbol.TickSize, lowerAtr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            bool bullishStructureBreak = previousSwingHigh > 0 && c.Close > previousSwingHigh + breakBuffer &&
                                         c.Close > c.Open && closeLocation >= 0.60 && bodyRatio >= 0.25;
            bool bearishStructureBreak = previousSwingLow > 0 && c.Close < previousSwingLow - breakBuffer &&
                                         c.Close < c.Open && closeLocation <= 0.40 && bodyRatio >= 0.25;

            // Pullback/reclaim is an additional early trigger; tolerance scales with lower-TF volatility.
            bool bullishPullbackReclaim = c.Close > c.Open && c.Close >= fast &&
                c.Low <= fast + lowerAtr * 0.20 && c.Close >= p.Close;
            bool bearishPullbackReclaim = c.Close < c.Open && c.Close <= fast &&
                c.High >= fast - lowerAtr * 0.20 && c.Close <= p.Close;

            bool buySetup = bullishRejection || bullishEngulf || bullishMomentum || bullishStructureBreak || bullishPullbackReclaim;
            bool sellSetup = bearishRejection || bearishEngulf || bearishMomentum || bearishStructureBreak || bearishPullbackReclaim;

            if (RequireLowerEmaAlignment)
            {
                // Structure breaks are exempted from the EMA alignment gate: a confirmed BOS can lead the EMA.
                buySetup = bullishStructureBreak || (buySetup && bullishEma);
                sellSetup = bearishStructureBreak || (sellSetup && bearishEma);
            }

            if (buySetup == sellSetup)
                return null;
            return buySetup ? TradeType.Buy : TradeType.Sell;
        }

        private TradeType? GetLowerMicroSwingBreakoutSignal()
        {
            if (_lowerBars == null || _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10))
                return null;

            double lowerAtr = GetLowerAtrValue();
            if (lowerAtr <= 0)
                return null;

            double triggerBuffer = Math.Max(Symbol.TickSize, lowerAtr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            double swingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars);
            double swingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars);
            if (swingHigh <= 0 || swingLow <= 0)
                return null;

            // Detect a FRESH price crossing, not simply a price that was already beyond an old swing
            // when the cBot started. This prevents stale-level entry on the first tick after attach.
            double buyTrigger = swingHigh + triggerBuffer;
            double sellTrigger = swingLow - triggerBuffer;
            bool buyBreak = _lastTickAsk > 0 && _lastTickAsk <= buyTrigger && Symbol.Ask > buyTrigger;
            bool sellBreak = _lastTickBid > 0 && _lastTickBid >= sellTrigger && Symbol.Bid < sellTrigger;
            if (buyBreak == sellBreak)
                return null;
            return buyBreak ? TradeType.Buy : TradeType.Sell;
        }

        private bool IsConfirmedLowerStructureBreak(TradeType tradeType)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return false;
            var c = _lowerBars.Last(1);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double bodyRatio = Math.Abs(c.Close - c.Open) / range;
            double closeLocation = (c.Close - c.Low) / range;
            double swingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars, true);
            double swingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars, true);
            double atr = GetLowerAtrValue();
            double buffer = Math.Max(Symbol.TickSize, atr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            if (tradeType == TradeType.Buy)
                return c.Close > c.Open && swingHigh > 0 && c.Close > swingHigh + buffer && bodyRatio >= 0.30 && closeLocation >= 0.65;
            return c.Close < c.Open && swingLow > 0 && c.Close < swingLow - buffer && bodyRatio >= 0.30 && closeLocation <= 0.35;
        }

        private double GetLowerStructuralStopAnchor(TradeType tradeType, int lookback)
        {
            if (_lowerBars == null || _lowerBars.Count < lookback + 3)
                return 0.0;

            // Prefer the nearest confirmed local pivot behind price. The pivot must have a newer
            // and an older closed neighbour, preventing a forming candle from moving the stop anchor.
            int maxIndex = Math.Min(Math.Max(3, lookback + 1), _lowerBars.Count - 2);
            for (int i = 2; i <= maxIndex; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (tradeType == TradeType.Buy)
                {
                    bool swingLow = pivot.Low <= newer.Low && pivot.Low < older.Low;
                    if (swingLow && pivot.Low < Symbol.Ask)
                        return pivot.Low;
                }
                else
                {
                    bool swingHigh = pivot.High >= newer.High && pivot.High > older.High;
                    if (swingHigh && pivot.High > Symbol.Bid)
                        return pivot.High;
                }
            }

            // Fallback is the recent completed-bar extreme, never the current forming bar.
            double extreme = tradeType == TradeType.Buy ? double.MaxValue : double.MinValue;
            int fallbackLookback = Math.Min(Math.Max(2, lookback), _lowerBars.Count - 1);
            for (int i = 1; i <= fallbackLookback; i++)
            {
                if (tradeType == TradeType.Buy)
                    extreme = Math.Min(extreme, _lowerBars.Last(i).Low);
                else
                    extreme = Math.Max(extreme, _lowerBars.Last(i).High);
            }
            return tradeType == TradeType.Buy ? (extreme < double.MaxValue ? extreme : 0.0)
                                               : (extreme > double.MinValue ? extreme : 0.0);
        }

        private double GetPriorLowerSwingLevel(bool wantHigh, int lookback, bool excludeMostRecentClosedBar = false)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return 0.0;

            // For a closed-candle signal, exclude Last(1) from its own reference range to avoid look-ahead.
            int firstIndex = excludeMostRecentClosedBar ? 3 : 2;
            int maxIndex = Math.Min(Math.Max(firstIndex + 1, lookback + (excludeMostRecentClosedBar ? 2 : 1)), _lowerBars.Count - 2);
            for (int i = firstIndex; i <= maxIndex; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (wantHigh)
                {
                    if (pivot.High >= newer.High && pivot.High > older.High)
                        return pivot.High;
                }
                else
                {
                    if (pivot.Low <= newer.Low && pivot.Low < older.Low)
                        return pivot.Low;
                }
            }

            // If no confirmed pivot exists, fall back to a local range boundary. In closed-bar tests,
            // start at Last(2) so the current signal candle can never define the level it supposedly broke.
            int fallbackStart = excludeMostRecentClosedBar ? 2 : 1;
            int fallbackCount = Math.Min(Math.Max(3, lookback), _lowerBars.Count - fallbackStart);
            double boundary = wantHigh ? double.MinValue : double.MaxValue;
            for (int offset = 0; offset < fallbackCount; offset++)
            {
                int i = fallbackStart + offset;
                if (wantHigh)
                    boundary = Math.Max(boundary, _lowerBars.Last(i).High);
                else
                    boundary = Math.Min(boundary, _lowerBars.Last(i).Low);
            }
            if (wantHigh && boundary > double.MinValue) return boundary;
            if (!wantHigh && boundary < double.MaxValue) return boundary;
            return 0.0;
        }

        private double GetLowerAtrValue()
        {
            if (_lowerBars == null || _lowerBars.Count < AtrPeriod + 2)
                return 0.0;

            int count = Math.Max(2, Math.Min(AtrPeriod, _lowerBars.Count - 2));
            double totalTrueRange = 0.0;
            for (int i = 1; i <= count; i++)
            {
                var bar = _lowerBars.Last(i);
                double previousClose = _lowerBars.Last(i + 1).Close;
                double trueRange = Math.Max(bar.High - bar.Low,
                    Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
                totalTrueRange += trueRange;
            }
            return totalTrueRange / count;
        }

        private double GetEstimatedMarginSafe(TradeType tradeType, double volumeInUnits)
        {
            if (!IsFinitePositive(volumeInUnits))
                return double.NaN;

            try
            {
                double margin = Symbol.GetEstimatedMargin(tradeType, volumeInUnits);
                return IsFinitePositive(margin) ? margin : double.NaN;
            }
            catch (Exception ex)
            {
                Diag("Could not estimate required margin for {0} units: {1}", volumeInUnits, ex.Message);
                return double.NaN;
            }
        }

        private double GetMaximumVolumeWithinMargin(TradeType tradeType, double maximumUnits, double marginBudget)
        {
            if (!IsFinitePositive(maximumUnits) || !IsFinitePositive(marginBudget))
                return 0.0;

            maximumUnits = Symbol.NormalizeVolumeInUnits(Math.Min(maximumUnits, Symbol.VolumeInUnitsMax), RoundingMode.Down);
            double minimumUnits = Symbol.VolumeInUnitsMin;
            if (maximumUnits < minimumUnits)
                return 0.0;

            double marginAtMaximum = GetEstimatedMarginSafe(tradeType, maximumUnits);
            if (!IsFinitePositive(marginAtMaximum))
                return 0.0;
            if (marginAtMaximum <= marginBudget)
                return maximumUnits;

            double marginAtMinimum = GetEstimatedMarginSafe(tradeType, minimumUnits);
            if (!IsFinitePositive(marginAtMinimum) || marginAtMinimum > marginBudget)
                return 0.0;

            // Binary search is used instead of simple proportional scaling because brokers may
            // apply dynamic leverage tiers and volume steps to metal symbols.
            double low = minimumUnits;
            double high = maximumUnits;
            for (int i = 0; i < 48; i++)
            {
                double middle = Symbol.NormalizeVolumeInUnits((low + high) * 0.5, RoundingMode.Down);
                if (middle <= low || middle >= high)
                    break;

                double margin = GetEstimatedMarginSafe(tradeType, middle);
                if (!IsFinitePositive(margin))
                    return 0.0;

                if (margin <= marginBudget)
                    low = middle;
                else
                    high = middle;
            }

            return Symbol.NormalizeVolumeInUnits(low, RoundingMode.Down);
        }

        private void CreateVolumeControlPanel()
        {
            _manualVolumeTextBox = new TextBox
            {
                Text = _runtimeManualVolumeLots.ToString("0.##", CultureInfo.InvariantCulture),
                Width = 74,
                Height = 24,
                FontSize = 11,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#232A36"),
                Margin = new Thickness(3)
            };

            _freeMarginUsageTextBox = new TextBox
            {
                Text = _runtimeMaxFreeMarginUsagePercent.ToString("0.#", CultureInfo.InvariantCulture),
                Width = 58,
                Height = 24,
                FontSize = 11,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#232A36"),
                Margin = new Thickness(3)
            };

            _manualModeButton = new Button
            {
                Text = "MANUAL LOTS",
                Width = 112,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#3A4658"),
                Margin = new Thickness(3)
            };
            _manualModeButton.Click += args =>
            {
                _runtimeVolumeMode = MicroSwingPrecisionVolumeMode.ManualLots;
                UpdateVolumeControlPanelStatus();
                SetVolumeNotice("Manual mode selected. The selected lot size is used as-is or the entry is skipped if risk/margin limits would be exceeded.", false);
            };

            _autoModeButton = new Button
            {
                Text = "AUTO MARGIN",
                Width = 105,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#3A4658"),
                Margin = new Thickness(3)
            };
            _autoModeButton.Click += args =>
            {
                _runtimeVolumeMode = MicroSwingPrecisionVolumeMode.AutoFreeMargin;
                UpdateVolumeControlPanelStatus();
                SetVolumeNotice("Auto mode selected. Volume is recalculated on every entry from live free margin, stop distance, setup risk budget and symbol limits.", false);
            };

            var header = new TextBlock
            {
                Text = "MICRO-SWING | VOLUME",
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                ForegroundColor = Color.FromHex("#E5EDF7"),
                Margin = new Thickness(4)
            };

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
            modeRow.AddChild(_manualModeButton);
            modeRow.AddChild(_autoModeButton);

            var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            manualRow.AddChild(new TextBlock
            {
                Text = "Manual lots",
                Width = 92,
                FontSize = 11,
                ForegroundColor = Color.FromHex("#C7D0DD"),
                VerticalAlignment = VerticalAlignment.Center
            });
            manualRow.AddChild(_manualVolumeTextBox);

            var marginRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
            marginRow.AddChild(new TextBlock
            {
                Text = "Auto free margin %",
                Width = 125,
                FontSize = 11,
                ForegroundColor = Color.FromHex("#C7D0DD"),
                VerticalAlignment = VerticalAlignment.Center
            });
            marginRow.AddChild(_freeMarginUsageTextBox);

            var applyButton = new Button
            {
                Text = "APPLY VALUES",
                Width = 216,
                Height = 26,
                FontSize = 10,
                ForegroundColor = Color.White,
                BackgroundColor = Color.FromHex("#245A7A"),
                Margin = new Thickness(3)
            };
            applyButton.Click += args => ApplyVolumePanelValues();

            _volumeModeStatusText = new TextBlock
            {
                FontSize = 11,
                ForegroundColor = Color.FromHex("#9FE3C2"),
                Margin = new Thickness(4)
            };
            _volumeAccountStatusText = new TextBlock
            {
                FontSize = 10,
                ForegroundColor = Color.FromHex("#CAD2DE"),
                Margin = new Thickness(4)
            };
            _volumeNoticeText = new TextBlock
            {
                Text = "Ready. TP remains a chart-only guide.",
                FontSize = 10,
                ForegroundColor = Color.FromHex("#B8C2D0"),
                Margin = new Thickness(4)
            };

            _volumePanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Width = 238,
                BackgroundColor = Color.FromHex("#141922"),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8),
                Opacity = 0.96
            };
            _volumePanel.AddChild(header);
            _volumePanel.AddChild(modeRow);
            _volumePanel.AddChild(manualRow);
            _volumePanel.AddChild(marginRow);
            _volumePanel.AddChild(applyButton);
            _volumePanel.AddChild(_volumeModeStatusText);
            _volumePanel.AddChild(_volumeAccountStatusText);
            _volumePanel.AddChild(_volumeNoticeText);
            Chart.AddControl(_volumePanel);
            UpdateVolumeControlPanelStatus();
            SetVolumeNotice("Manual uses your entered lots; Auto recalculates from live free margin and risk before every entry.", false);
        }

        private void ApplyVolumePanelValues()
        {
            double manualLots;
            double marginUsage;
            if (!TryParsePanelNumber(_manualVolumeTextBox == null ? string.Empty : _manualVolumeTextBox.Text, out manualLots) ||
                !IsFinitePositive(manualLots) || manualLots < 0.01 || manualLots > 100.0)
            {
                SetVolumeNotice("Invalid manual lot size. Enter a value from 0.01 to 100 lots.", true);
                return;
            }

            if (!TryParsePanelNumber(_freeMarginUsageTextBox == null ? string.Empty : _freeMarginUsageTextBox.Text, out marginUsage) ||
                marginUsage < 5.0 || marginUsage > 80.0)
            {
                SetVolumeNotice("Invalid Auto margin usage. Enter a percentage from 5 to 80 to keep free-margin reserve.", true);
                return;
            }

            double rawManualUnits = Symbol.QuantityToVolumeInUnits(manualLots);
            double manualUnits = Symbol.NormalizeVolumeInUnits(rawManualUnits, RoundingMode.Down);
            if (manualUnits < Symbol.VolumeInUnitsMin || manualUnits > Symbol.VolumeInUnitsMax ||
                Math.Abs(manualUnits - rawManualUnits) > Math.Max(0.000001, Symbol.VolumeInUnitsStep * 0.000001))
            {
                SetVolumeNotice("Manual lots are outside the broker's allowed range or volume step. Adjust the value; it will not be silently rounded down.", true);
                return;
            }

            _runtimeManualVolumeLots = Symbol.VolumeInUnitsToQuantity(manualUnits);
            _runtimeMaxFreeMarginUsagePercent = marginUsage;
            _manualVolumeTextBox.Text = _runtimeManualVolumeLots.ToString("0.##", CultureInfo.InvariantCulture);
            _freeMarginUsageTextBox.Text = _runtimeMaxFreeMarginUsagePercent.ToString("0.#", CultureInfo.InvariantCulture);
            UpdateVolumeControlPanelStatus();
            SetVolumeNotice("Values applied for this running cBot instance. For restart defaults, also update the cBot parameters.", false);
        }

        private bool TryParsePanelNumber(string raw, out double value)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private void UpdateVolumeControlPanelStatus()
        {
            if (_volumeModeStatusText == null || _volumeAccountStatusText == null)
                return;

            bool manual = _runtimeVolumeMode == MicroSwingPrecisionVolumeMode.ManualLots;
            _manualModeButton.Text = manual ? "MANUAL ✓" : "MANUAL LOTS";
            _autoModeButton.Text = manual ? "AUTO MARGIN" : "AUTO ✓";
            _volumeModeStatusText.Text = manual
                ? String.Format(CultureInfo.InvariantCulture, "MODE: MANUAL | requested {0:0.##} lots", _runtimeManualVolumeLots)
                : String.Format(CultureInfo.InvariantCulture, "MODE: AUTO | cap {0:0.##} lots | {1:0.#}% free margin", MaximumAutoVolumeLots, _runtimeMaxFreeMarginUsagePercent);

            double freeMargin = Math.Max(0.0, Account.FreeMargin);
            double autoBudget = freeMargin * _runtimeMaxFreeMarginUsagePercent / 100.0;
            _volumeAccountStatusText.Text = String.Format(CultureInfo.InvariantCulture,
                "Equity: {0:N2} {1}\nFree margin: {2:N2}\nAuto margin budget: {3:N2}\nRisk ceiling: {4:0.##}% equity",
                Account.Equity, Account.Asset.Name, freeMargin, autoBudget,
                AbsoluteRiskCeilingPercent > 0 ? AbsoluteRiskCeilingPercent : MaximumOpportunityRiskPercent);
        }

        private void SetVolumeNotice(string message, bool isWarning)
        {
            if (_volumeNoticeText == null)
                return;
            _volumeNoticeText.Text = message ?? string.Empty;
            _volumeNoticeText.ForegroundColor = isWarning ? Color.FromHex("#FFB4A8") : Color.FromHex("#B8C2D0");
        }

        private double GetAdaptiveAllowedRiskPercent(double qualityScore)
        {
            double minimum = Math.Max(0.0, MinimumOpportunityRiskPercent);
            double maximum = Math.Max(minimum, MaximumOpportunityRiskPercent);
            double allowed;

            if (EnableAdaptiveRiskBudget)
            {
                // Convex sizing keeps middling setups closer to the minimum budget; only strong
                // composite scores approach the maximum. This score is a rule-based quality index,
                // not a statistically calibrated win probability.
                double quality = Clamp01(qualityScore);
                double riskCurve = Math.Pow(quality, 2.0);
                allowed = minimum + riskCurve * (maximum - minimum);
            }
            else
                allowed = maximum;

            // This is a disaster-control ceiling, not the normal sizing rule. The active budget
            // remains opportunity-dependent below the ceiling.
            if (AbsoluteRiskCeilingPercent > 0)
                allowed = Math.Min(allowed, AbsoluteRiskCeilingPercent);

            return Math.Max(0.0, allowed);
        }

        private double GetOpportunityQualityScore(TradeType tradeType, double atr, double targetR)
        {
            double trendScore = GetM5DirectionalStrengthScore(tradeType, atr);
            double lowerSignalScore = GetLowerSignalQualityScore(tradeType);
            double structuralRoomScore = targetR > 0 ? Clamp01(targetR / (targetR + 1.5)) : 0.0;
            bool confirmedStructureShift = IsConfirmedLowerStructureBreak(tradeType);
            // A confirmed lower-TF break is an independent reason to consider an early shift even
            // when M5's slower trend score still points the other way. This remains a heuristic,
            // not a measured probability of profit.
            return Clamp01(0.40 * trendScore + 0.36 * lowerSignalScore +
                           0.16 * structuralRoomScore + (confirmedStructureShift ? 0.08 : 0.0));
        }

        private double GetLowerSignalQualityScore(TradeType tradeType)
        {
            if (_lowerBars == null || _lowerBars.Count < 3 || _lowerFastEma == null || _lowerSlowEma == null)
                return 0.0;

            var c = _lowerBars.Last(1);
            var p = _lowerBars.Last(2);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double body = Math.Abs(c.Close - c.Open);
            double bodyRatio = Clamp01(body / range);
            double closeLocation = tradeType == TradeType.Buy
                ? Clamp01((c.Close - c.Low) / range)
                : Clamp01((c.High - c.Close) / range);
            double wick = tradeType == TradeType.Buy
                ? Math.Max(0.0, Math.Min(c.Open, c.Close) - c.Low)
                : Math.Max(0.0, c.High - Math.Max(c.Open, c.Close));
            double wickRatio = Clamp01(wick / range);
            bool directionCandle = tradeType == TradeType.Buy ? c.Close > c.Open : c.Close < c.Open;
            bool emaAligned = tradeType == TradeType.Buy
                ? _lowerFastEma.Result.Last(1) >= _lowerSlowEma.Result.Last(1) && c.Close >= _lowerFastEma.Result.Last(1)
                : _lowerFastEma.Result.Last(1) <= _lowerSlowEma.Result.Last(1) && c.Close <= _lowerFastEma.Result.Last(1);
            bool engulf = tradeType == TradeType.Buy
                ? c.Close > c.Open && p.Close < p.Open && c.Close >= p.Open && c.Open <= p.Close
                : c.Close < c.Open && p.Close > p.Open && c.Close <= p.Open && c.Open >= p.Close;
            bool momentum = tradeType == TradeType.Buy
                ? c.Close > c.Open && c.Close > p.High && bodyRatio >= 0.30
                : c.Close < c.Open && c.Close < p.Low && bodyRatio >= 0.30;
            bool structuralBreak = IsConfirmedLowerStructureBreak(tradeType);
            double lowerAtr = GetLowerAtrValue();
            bool pullbackReclaim = lowerAtr > 0 && (tradeType == TradeType.Buy
                ? c.Close > c.Open && c.Close >= _lowerFastEma.Result.Last(1) && c.Low <= _lowerFastEma.Result.Last(1) + lowerAtr * 0.20
                : c.Close < c.Open && c.Close <= _lowerFastEma.Result.Last(1) && c.High >= _lowerFastEma.Result.Last(1) - lowerAtr * 0.20);
            double patternScore = structuralBreak ? 0.98 : momentum ? 0.94 : engulf ? 0.88 : pullbackReclaim ? 0.84 : (directionCandle && wickRatio >= 0.25 && closeLocation >= 0.58) ? 0.79 : 0.60;
            double bodyQuality = Clamp01(bodyRatio / 0.60);
            double emaScore = emaAligned ? 1.0 : 0.35;
            double followThrough = tradeType == TradeType.Buy
                ? (c.Close >= p.Close ? 1.0 : 0.25)
                : (c.Close <= p.Close ? 1.0 : 0.25);
            return Clamp01(0.34 * patternScore + 0.24 * closeLocation + 0.16 * bodyQuality + 0.14 * emaScore + 0.12 * followThrough);
        }

        private double GetM5DirectionalStrengthScore(TradeType tradeType, double atr)
        {
            if (Bars == null || Bars.Count < 7 || atr <= 0)
                return 0.35;

            double closeNow = Bars.Last(1).Close;
            double closeThen = Bars.Last(6).Close;
            double signedMove = tradeType == TradeType.Buy ? closeNow - closeThen : closeThen - closeNow;
            double displacementScore = Clamp01(signedMove / (atr * 2.0));

            double bodySum = 0.0;
            double rangeSum = 0.0;
            double alignedBodySum = 0.0;
            for (int i = 1; i <= 3; i++)
            {
                var candle = Bars.Last(i);
                double body = Math.Abs(candle.Close - candle.Open);
                double range = Math.Max(Symbol.TickSize, candle.High - candle.Low);
                bodySum += body;
                rangeSum += range;
                bool aligned = tradeType == TradeType.Buy ? candle.Close > candle.Open : candle.Close < candle.Open;
                if (aligned)
                    alignedBodySum += body;
            }

            double bodyRatio = rangeSum > 0 ? Clamp01(bodySum / rangeSum) : 0.0;
            double alignmentRatio = bodySum > 0 ? Clamp01(alignedBodySum / bodySum) : 0.0;
            return Clamp01(0.45 * displacementScore + 0.30 * bodyRatio + 0.25 * alignmentRatio);
        }

        private double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private double GetEntryTargetR(TradeType tradeType, double atr, double entryReference, double stopDistance)
        {
            if (atr <= 0 || stopDistance <= Symbol.TickSize)
                return 0.0;

            double baseR = Math.Max(0.5, SuggestedTakeProfitR);
            double requestedR = baseR;
            if (AdaptiveSuggestedTp && Bars != null && Bars.Count >= 7)
            {
                double signedMove = tradeType == TradeType.Buy
                    ? Bars.Last(1).Close - Bars.Last(6).Close
                    : Bars.Last(6).Close - Bars.Last(1).Close;
                double directional = Clamp01(signedMove / (atr * 2.5));
                double bodySum = 0.0;
                double rangeSum = 0.0;
                double alignedBody = 0.0;
                for (int i = 1; i <= 3; i++)
                {
                    var candle = Bars.Last(i);
                    double body = Math.Abs(candle.Close - candle.Open);
                    double range = Math.Max(Symbol.TickSize, candle.High - candle.Low);
                    bodySum += body;
                    rangeSum += range;
                    bool aligned = tradeType == TradeType.Buy ? candle.Close > candle.Open : candle.Close < candle.Open;
                    if (aligned)
                        alignedBody += body;
                }
                double alignedBodyRatio = rangeSum > 0 ? Clamp01(alignedBody / rangeSum) : 0.0;
                double alignmentRatio = bodySum > 0 ? Clamp01(alignedBody / bodySum) : 0.0;
                double strength = Clamp01(0.55 * directional + 0.25 * alignedBodyRatio + 0.20 * alignmentRatio);
                requestedR = Math.Max(baseR, Math.Min(MaximumSuggestedTpR,
                    baseR + strength * Math.Max(0.0, MaximumSuggestedTpR - baseR)));
            }

            double direction = tradeType == TradeType.Buy ? 1.0 : -1.0;
            double target = entryReference + direction * stopDistance * requestedR;
            double m5Barrier = GetNearestStructuralBarrier(tradeType, entryReference, atr);
            double lowerBarrier = GetNearestLowerStructuralBarrier(tradeType, entryReference);
            double barrier = GetNearestForwardBarrier(tradeType, entryReference, m5Barrier, lowerBarrier);
            if (barrier > 0)
            {
                double buffer = Math.Max(Symbol.TickSize, atr * Math.Max(0.0, SuggestedTpStructureBufferAtr));
                double structuralCandidate = barrier - direction * buffer;
                bool candidateInProfit = tradeType == TradeType.Buy
                    ? structuralCandidate > entryReference + Symbol.TickSize
                    : structuralCandidate < entryReference - Symbol.TickSize;
                bool barrierAhead = tradeType == TradeType.Buy
                    ? barrier > entryReference + Symbol.TickSize
                    : barrier < entryReference - Symbol.TickSize;
                bool barrierLimitsTarget = tradeType == TradeType.Buy ? barrier < target : barrier > target;
                if (barrierAhead && barrierLimitsTarget)
                    target = candidateInProfit ? structuralCandidate : barrier;
            }

            double realisticR = Math.Abs(target - entryReference) / stopDistance;
            return IsFinitePositive(realisticR) ? realisticR : 0.0;
        }

        private double GetNearestLowerStructuralBarrier(TradeType tradeType, double currentPrice)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return 0.0;
            int lookback = Math.Min(Math.Max(4, EntryStructureLookbackBars + 2), _lowerBars.Count - 2);
            double nearest = 0.0;
            double bestDistance = double.MaxValue;
            for (int i = 2; i <= lookback; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (tradeType == TradeType.Buy)
                {
                    bool swingHigh = pivot.High >= newer.High && pivot.High > older.High && pivot.High > currentPrice;
                    double distance = pivot.High - currentPrice;
                    if (swingHigh && distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = pivot.High;
                    }
                }
                else
                {
                    bool swingLow = pivot.Low <= newer.Low && pivot.Low < older.Low && pivot.Low < currentPrice;
                    double distance = currentPrice - pivot.Low;
                    if (swingLow && distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = pivot.Low;
                    }
                }
            }
            return nearest;
        }

        private double GetNearestForwardBarrier(TradeType tradeType, double currentPrice, double first, double second)
        {
            bool firstValid = first > 0 && (tradeType == TradeType.Buy ? first > currentPrice : first < currentPrice);
            bool secondValid = second > 0 && (tradeType == TradeType.Buy ? second > currentPrice : second < currentPrice);
            if (!firstValid) return secondValid ? second : 0.0;
            if (!secondValid) return first;
            return tradeType == TradeType.Buy ? Math.Min(first, second) : Math.Max(first, second);
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

            LocalStorage.Remove(GetStateKey(position.Id), SharedScope);
            LocalStorage.Remove(GetOwnerKey(position.Id), SharedScope);
            LocalStorage.Flush(SharedScope);
        }

        private void ManageCurrentSymbolPositions()
        {
            // Copy first: an emergency ClosePosition call can remove a position while management is running.
            var snapshot = new List<Position>();
            foreach (var position in Positions)
                snapshot.Add(position);

            foreach (var position in snapshot)
            {
                if (IsPositionStillOpen(position.Id) && ShouldManage(position))
                    ManagePosition(position);
            }
        }

        private bool IsThisBotPosition(Position position)
        {
            return position != null &&
                   string.Equals(position.SymbolName, SymbolName, StringComparison.Ordinal) &&
                   string.Equals(position.Label, AutoTradeLabel, StringComparison.Ordinal);
        }

        private bool ShouldManage(Position position)
        {
            // Exact label + exact symbol: never manage manual positions or different cBots.
            return IsThisBotPosition(position) && !_ignoredExistingPositionIds.Contains(position.Id);
        }

        private void ManagePosition(Position position)
        {
            if (position == null || !TryAcquireManagementLease(position))
                return;

            // If an intended initial SL was crossed before installation and the emergency close
            // failed, keep retrying the close rather than silently recalculating a wider stop.
            if (_pendingEmergencyCloseIds.Contains(position.Id))
            {
                TryCloseBreachedPosition(position);
                return;
            }

            // The SL attempt is deliberately immediate; the candle delay applies only to tightening
            // a stop into profit, not to the initial protection.
            if (!position.StopLoss.HasValue && !TrySetInitialStop(position))
                return;

            var state = GetOrCreateState(position);
            if (state == null || state.InitialRiskDistance <= 0)
                return;

            UpdateSuggestedTpGuide(position, state);

            if (!HasRequiredCompletedBarsAfterEntry(position))
                return;

            ManageBreakEvenAndTrailing(position, state);
        }

        private bool TrySetInitialStop(Position position)
        {
            // Primary fallback: lower-timeframe confirmed swing/wick structure, matching the entry model.
            // M5 candle structure is used only if lower-timeframe history/ATR is not ready.
            double lowerAtr = GetLowerAtrValue();
            double m5Atr = GetAtrValue();
            double spread = Math.Max(0, Symbol.Ask - Symbol.Bid);
            double buffer = Math.Max(2.0 * Symbol.TickSize,
                Math.Max(spread * Math.Max(1.0, M5SpreadBufferMultiplier), lowerAtr > 0 ? lowerAtr * 0.10 : 0));
            double plannedStop = 0.0;
            bool usedFallback = false;
            string method = "lower-TF swing / wick";

            if (_lowerBars != null && _lowerBars.Count >= Math.Max(5, EntryStructureLookbackBars + 3) && lowerAtr > 0)
            {
                int lookback = Math.Max(3, Math.Min(EntryStructureLookbackBars, _lowerBars.Count - 2));
                double anchor = GetLowerStructuralStopAnchor(position.TradeType, lookback);
                if (IsFinitePositive(anchor))
                    plannedStop = position.TradeType == TradeType.Buy ? anchor - buffer : anchor + buffer;
            }

            bool wrongSide = position.TradeType == TradeType.Buy
                ? plannedStop >= position.EntryPrice - Symbol.TickSize || plannedStop <= 0
                : plannedStop <= position.EntryPrice + Symbol.TickSize || plannedStop <= 0;

            if (wrongSide && Bars != null && Bars.Count >= 2)
            {
                // Conservative M5 fallback only if lower-TF structure isn't usable.
                var candle = Bars.Last(1);
                plannedStop = position.TradeType == TradeType.Buy
                    ? candle.Low - Math.Max(2 * Symbol.TickSize, Math.Max(spread * M5SpreadBufferMultiplier, m5Atr * M5AtrBufferFraction))
                    : candle.High + Math.Max(2 * Symbol.TickSize, Math.Max(spread * M5SpreadBufferMultiplier, m5Atr * M5AtrBufferFraction));
                method = "M5 fallback wick";
                usedFallback = true;
            }

            wrongSide = position.TradeType == TradeType.Buy
                ? plannedStop >= position.EntryPrice - Symbol.TickSize || plannedStop <= 0
                : plannedStop <= position.EntryPrice + Symbol.TickSize || plannedStop <= 0;
            if (wrongSide)
            {
                double fallbackAtr = lowerAtr > 0 ? lowerAtr : m5Atr;
                if (fallbackAtr <= 0)
                    return RetryInitialStop(position, "No valid lower-TF or M5 ATR is available for a fallback stop.");
                plannedStop = position.TradeType == TradeType.Buy
                    ? position.EntryPrice - Math.Max(fallbackAtr * 1.0, spread * M5SpreadBufferMultiplier + 2 * Symbol.TickSize)
                    : position.EntryPrice + Math.Max(fallbackAtr * 1.0, spread * M5SpreadBufferMultiplier + 2 * Symbol.TickSize);
                method = "ATR emergency fallback";
                usedFallback = true;
            }

            double guidelineAtr = lowerAtr > 0 ? lowerAtr : m5Atr;
            bool exceedsAtrGuideline = guidelineAtr > 0 && MaximumInitialStopAtr > 0 &&
                Math.Abs(position.EntryPrice - plannedStop) > Math.Max(guidelineAtr * MaximumInitialStopAtr, 2.0 * Symbol.TickSize);

            return SubmitInitialStop(position, plannedStop, usedFallback, method, exceedsAtrGuideline);
        }

        private void TryCloseBreachedPosition(Position position)
        {
            if (position == null)
                return;

            DateTime lastAttempt;
            double retrySeconds = Math.Max(1.0, RetryIntervalMilliseconds / 1000.0);
            if (_lastEmergencyCloseAttemptTime.TryGetValue(position.Id, out lastAttempt) &&
                (Server.Time - lastAttempt).TotalSeconds < retrySeconds)
                return;

            _lastEmergencyCloseAttemptTime[position.Id] = Server.Time;
            var result = ClosePosition(position);
            if (result.IsSuccessful)
            {
                if (!IsPositionStillOpen(position.Id))
                {
                    // Some API event sequences report closure before ClosePosition returns.
                    _pendingEmergencyCloseIds.Remove(position.Id);
                    _lastEmergencyCloseAttemptTime.Remove(position.Id);
                }
                else
                {
                    // The request was accepted but the position is still visible. Verify again after
                    // the retry interval instead of flooding requests or assuming it is closed.
                    Diag("Emergency close accepted but position {0} is still visible; will verify again if it remains open.", position.Id);
                }
            }
            else
            {
                WarnThrottled(position, "Emergency close was rejected while the planned initial SL had already been crossed. The position may remain unprotected; retrying.");
            }
        }

        private bool IsPositionStillOpen(long positionId)
        {
            foreach (var current in Positions)
            {
                if (current.Id == positionId)
                    return true;
            }
            return false;
        }

        private bool RetryInitialStop(Position position, string message)
        {
            WarnThrottled(position, message + " Retrying on next tick/timer.");
            return false;
        }

        private bool SubmitInitialStop(Position position, double plannedStop, bool usedFallback, string method, bool exceedsAtrGuideline)
        {
            if (!IsFinitePositive(Math.Abs(position.EntryPrice - plannedStop)))
                return RetryInitialStop(position, "Initial SL distance is invalid.");

            // If price has already crossed the originally planned stop, never move that stop
            // farther away merely to manufacture a valid broker order. Optionally close the
            // unprotected position as the equivalent of the missed stop execution.
            bool plannedStopAlreadyBreached = position.TradeType == TradeType.Buy
                ? Symbol.Bid <= plannedStop
                : Symbol.Ask >= plannedStop;

            if (plannedStopAlreadyBreached)
            {
                if (!CloseIfInitialStopAlreadyCrossed)
                    return RetryInitialStop(position, "Price has already crossed the planned initial SL; not widening the stop. Enable emergency close or handle the position manually.");

                _pendingEmergencyCloseIds.Add(position.Id);
                TryCloseBreachedPosition(position);
                return false;
            }

            double minDistance = GetMinimumStopDistance(position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask);
            double stopPrice;
            bool hadToWidenPastPlan = false;

            if (position.TradeType == TradeType.Buy)
            {
                double brokerMaximum = Symbol.Bid - minDistance;
                hadToWidenPastPlan = plannedStop > brokerMaximum + Symbol.TickSize * 0.1;
                stopPrice = NormalizeStopPrice(Math.Min(plannedStop, brokerMaximum), TradeType.Buy, false);
                if (!IsFinitePositive(stopPrice) || stopPrice >= Symbol.Bid || stopPrice > Symbol.Bid - minDistance + Symbol.TickSize * 0.1)
                    return RetryInitialStop(position, "Initial BUY SL is not broker-valid at current quote.");
                if (stopPrice >= position.EntryPrice - Symbol.TickSize * 0.5)
                    return RetryInitialStop(position, "Initial BUY SL would not be on the loss side of entry; refusing unsafe placement.");
            }
            else
            {
                double brokerMinimum = Symbol.Ask + minDistance;
                hadToWidenPastPlan = plannedStop < brokerMinimum - Symbol.TickSize * 0.1;
                stopPrice = NormalizeStopPrice(Math.Max(plannedStop, brokerMinimum), TradeType.Sell, false);
                if (!IsFinitePositive(stopPrice) || stopPrice <= Symbol.Ask || stopPrice < Symbol.Ask + minDistance - Symbol.TickSize * 0.1)
                    return RetryInitialStop(position, "Initial SELL SL is not broker-valid at current quote.");
                if (stopPrice <= position.EntryPrice + Symbol.TickSize * 0.5)
                    return RetryInitialStop(position, "Initial SELL SL would not be on the loss side of entry; refusing unsafe placement.");
            }

            var result = position.ModifyStopLossPrice(stopPrice);
            if (!result.IsSuccessful)
                return RetryInitialStop(position, "Broker rejected initial SL. Position can remain unprotected until accepted.");

            var state = new PositionState
            {
                InitialRiskDistance = Math.Abs(position.EntryPrice - stopPrice),
                RiskDistanceEstimated = false
            };
            _states[position.Id] = state;
            SaveState(position, state);
            _lastWarningTime.Remove(position.Id);
            Diag("Initial SL accepted | position {0} {1} | method {2} | SL {3} | risk {4} | fallback {5}",
                position.Id, position.TradeType, method,
                stopPrice.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture),
                state.InitialRiskDistance.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture), usedFallback);
            if (exceedsAtrGuideline && hadToWidenPastPlan)
                WarnThrottled(position, "The planned initial stop exceeds the configured ATR guideline and the broker minimum distance forced it farther away. The stop remains beyond the planned wick/structure, but actual risk is larger than the guideline.");
            else if (exceedsAtrGuideline)
                WarnThrottled(position, "The required candle-wick/structure stop exceeds the configured ATR guideline. It was kept beyond the wick/structure rather than capped inside the wick; actual risk is larger than the guideline.");
            else if (hadToWidenPastPlan)
                WarnThrottled(position, "Broker minimum stop distance forced SL farther away than the candle-wick plan; actual risk is larger than the planned distance.");
            return true;
        }

        private void ManageBreakEvenAndTrailing(Position position, PositionState state)
        {
            if (!position.StopLoss.HasValue)
                return;

            double atr = GetAtrValue();
            double currentStop = position.StopLoss.Value;
            double profitDistance = position.TradeType == TradeType.Buy
                ? Symbol.Bid - position.EntryPrice
                : position.EntryPrice - Symbol.Ask;

            if (profitDistance <= 0)
                return;

            double minDistance = GetMinimumStopDistance(position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask);
            double desiredStop = currentStop;
            bool hasCandidate = false;

            // Stage 1: adaptive, cost-aware break-even. Weakening/poorly aligned trends protect
            // earlier, while a clean strong trend receives extra room to avoid needless stop-outs.
            // A completed lower-timeframe candle is still required before this method is called.
            double trendScore = GetM5DirectionalStrengthScore(position.TradeType, atr);
            double effectiveBreakEvenTriggerR = BreakEvenTriggerR;
            if (AdaptiveBreakEvenTiming)
            {
                double earlyTrigger = Math.Max(0.1, EarlyBreakEvenTriggerR);
                double strongTrigger = Math.Max(earlyTrigger, StrongTrendBreakEvenTriggerR);
                effectiveBreakEvenTriggerR = earlyTrigger + trendScore * (strongTrigger - earlyTrigger);
            }

            if (profitDistance >= state.InitialRiskDistance * effectiveBreakEvenTriggerR)
            {
                double commissionPips = EstimateRoundTurnCommissionPips(position);
                double bufferPips = Math.Max(BreakEvenBufferPips,
                    commissionPips + BreakEvenExtraSafetyPips);
                double buffer = bufferPips * Symbol.PipSize;

                double breakEvenStop = position.TradeType == TradeType.Buy
                    ? position.EntryPrice + buffer
                    : position.EntryPrice - buffer;

                breakEvenStop = NormalizeStopPrice(breakEvenStop, position.TradeType, true);

                bool isBrokerValid = position.TradeType == TradeType.Buy
                    ? breakEvenStop <= Symbol.Bid - minDistance
                    : breakEvenStop >= Symbol.Ask + minDistance;

                if (isBrokerValid)
                {
                    desiredStop = MoreProtectiveStop(position.TradeType, desiredStop, breakEvenStop);
                    hasCandidate = true;
                }
            }

            // Stage 2: adaptive ATR + completed-candle structure trailing. Strong aligned trends
            // receive a wider trail; weak trends use a tighter trail and a larger profit floor.
            // The candidate is never allowed to loosen the existing SL.
            double adaptiveTrailingStartR = TrailingStartR + trendScore * 0.20;
            if (atr > 0 && profitDistance >= state.InitialRiskDistance * adaptiveTrailingStartR)
            {
                double spread = Math.Max(0, Symbol.Ask - Symbol.Bid);
                double adaptiveAtrMultiplier = TrailingAtrMultiplier * (0.80 + 0.55 * trendScore);
                double trailingDistance = Math.Max(atr * adaptiveAtrMultiplier,
                    spread * 1.5 + Symbol.TickSize);
                double adaptiveLockR = Math.Max(MinimumProfitLockR, 0.12 + (1.0 - trendScore) * 0.18);
                double lockDistance = state.InitialRiskDistance * adaptiveLockR;
                double structureBuffer = Math.Max(spread * M5SpreadBufferMultiplier,
                    atr * TrailingStructureAtrBufferFraction);
                double recentSwingLow = double.MaxValue;
                double recentSwingHigh = double.MinValue;
                int structureBars = Math.Max(1, Math.Min(TrailingStructureLookbackBars, Bars.Count - 1));
                for (int i = 1; i <= structureBars; i++)
                {
                    recentSwingLow = Math.Min(recentSwingLow, Bars.Last(i).Low);
                    recentSwingHigh = Math.Max(recentSwingHigh, Bars.Last(i).High);
                }
                double trailCandidate;

                if (position.TradeType == TradeType.Buy)
                {
                    double rawTrail = Symbol.Bid - trailingDistance;
                    double structuralTrail = recentSwingLow - structureBuffer;
                    double profitFloor = position.EntryPrice + lockDistance;
                    // Do not force a profit floor through a recent swing low. Wait until both the
                    // ATR trail and structural anchor support a stop above the desired profit floor.
                    trailCandidate = Math.Min(rawTrail, structuralTrail);
                    if (trailCandidate >= profitFloor)
                    {
                        trailCandidate = NormalizeStopPrice(trailCandidate, TradeType.Buy, true);
                        bool valid = trailCandidate <= Symbol.Bid - minDistance &&
                                     trailCandidate >= profitFloor - Symbol.TickSize * 0.1;
                        if (valid)
                        {
                            desiredStop = MoreProtectiveStop(position.TradeType, desiredStop, trailCandidate);
                            hasCandidate = true;
                        }
                    }
                }
                else
                {
                    double rawTrail = Symbol.Ask + trailingDistance;
                    double structuralTrail = recentSwingHigh + structureBuffer;
                    double profitFloor = position.EntryPrice - lockDistance;
                    trailCandidate = Math.Max(rawTrail, structuralTrail);
                    // Symmetric rule for shorts: don't place the trail inside a recent swing high.
                    if (trailCandidate <= profitFloor)
                    {
                        trailCandidate = NormalizeStopPrice(trailCandidate, TradeType.Sell, true);
                        bool valid = trailCandidate >= Symbol.Ask + minDistance &&
                                     trailCandidate <= profitFloor + Symbol.TickSize * 0.1;
                        if (valid)
                        {
                            desiredStop = MoreProtectiveStop(position.TradeType, desiredStop, trailCandidate);
                            hasCandidate = true;
                        }
                    }
                }
            }

            if (!hasCandidate)
                return;

            double step = MinimumStopUpdateStepPips * Symbol.PipSize;
            bool improvesEnough = position.TradeType == TradeType.Buy
                ? desiredStop > currentStop + step
                : desiredStop < currentStop - step;

            // Absolute monotonicity: never loosen the current stop, irrespective of stage or ATR.
            if (!improvesEnough)
                return;

            var result = position.ModifyStopLossPrice(desiredStop);
            if (result.IsSuccessful)
            {
                _lastWarningTime.Remove(position.Id);
                Diag("SL advanced | position {0} | {1} -> {2}", position.Id,
                    currentStop.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture),
                    desiredStop.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture));
            }
            else
            {
                WarnThrottled(position, "Broker rejected a break-even/trailing update; retrying on the next tick/timer.");
            }
        }

        private bool HasRequiredCompletedBarsAfterEntry(Position position)
        {
            int requiredBars = CompletedBarsAfterEntryBeforeProfitLock;
            if (requiredBars <= 0)
                return true;

            if (_lowerBars == null || _lowerBars.Count < 3)
                return false;

            int entryBarIndex = _lowerBars.OpenTimes.GetIndexByTime(position.EntryTime);
            int currentBarIndex = _lowerBars.Count - 1; // live/forming lower-TF bar
            if (entryBarIndex >= 0 && entryBarIndex < _lowerBars.Count)
                return currentBarIndex >= entryBarIndex + requiredBars;

            TimeSpan duration = GetLowerTimeframeDuration();
            if (duration == TimeSpan.Zero)
                return false;
            return _lowerBars.OpenTimes.LastValue >= position.EntryTime.AddTicks(duration.Ticks * (requiredBars - 1L));
        }

        private TimeSpan GetLowerTimeframeDuration()
        {
            if (_lowerBars != null && _lowerBars.Count >= 2)
            {
                TimeSpan delta = _lowerBars.OpenTimes.LastValue - _lowerBars.OpenTimes.Last(1);
                if (delta > TimeSpan.Zero)
                    return delta;
            }
            switch (LowerEntryTimeframe)
            {
                case HybridLowerEntryTimeframe.M2: return TimeSpan.FromMinutes(2);
                case HybridLowerEntryTimeframe.M3: return TimeSpan.FromMinutes(3);
                default: return TimeSpan.FromMinutes(1);
            }
        }

        private void CleanupPreviousTpGuideObjects()
        {
            // Remove stale guide objects from closed positions, but preserve a guide for any
            // position that is still open. This avoids deleting another active manager's chart guide.
            string linePrefix = "SPG M5 MicroSwing HV TP Line ";
            string labelPrefix = "SPG M5 MicroSwing HV TP Label ";
            var namesToRemove = new List<string>();
            foreach (var chartObject in Chart.Objects)
            {
                if (chartObject == null || string.IsNullOrEmpty(chartObject.Name))
                    continue;

                string suffix = null;
                if (chartObject.Name.StartsWith(linePrefix, StringComparison.Ordinal))
                    suffix = chartObject.Name.Substring(linePrefix.Length);
                else if (chartObject.Name.StartsWith(labelPrefix, StringComparison.Ordinal))
                    suffix = chartObject.Name.Substring(labelPrefix.Length);
                if (suffix == null)
                    continue;

                long positionId;
                if (!long.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out positionId) ||
                    !IsPositionStillOpen(positionId))
                    namesToRemove.Add(chartObject.Name);
            }

            foreach (var objectName in namesToRemove)
                Chart.RemoveObject(objectName);
        }

        private string GetSuggestedTpLineName(long positionId)
        {
            return "SPG M5 MicroSwing HV TP Line " + positionId.ToString(CultureInfo.InvariantCulture);
        }

        private string GetSuggestedTpLabelName(long positionId)
        {
            return "SPG M5 MicroSwing HV TP Label " + positionId.ToString(CultureInfo.InvariantCulture);
        }

        private void RemoveSuggestedTpGuide(long positionId)
        {
            Chart.RemoveObject(GetSuggestedTpLineName(positionId));
            Chart.RemoveObject(GetSuggestedTpLabelName(positionId));
            _tpGuidePositionIds.Remove(positionId);

            PositionState state;
            if (_states.TryGetValue(positionId, out state))
            {
                state.LastTpDrawBarIndex = -1;
                state.LastSuggestedTpPrice = 0;
                state.LastSuggestedTpReached = false;
                state.LastTrendScore = 0;
            }
        }

        private double NormalizeSuggestedTpPrice(double price, TradeType tradeType)
        {
            double tickSize = Symbol.TickSize;
            if (tickSize <= 0)
                return Math.Round(price, Symbol.Digits);

            double units = price / tickSize;
            // Keep the visual target at or beyond the requested R multiple after tick rounding.
            double roundedUnits = tradeType == TradeType.Buy
                ? Math.Ceiling(units - 1e-10)
                : Math.Floor(units + 1e-10);

            return Math.Round(roundedUnits * tickSize, Symbol.Digits);
        }

        private double GetAdaptiveSuggestedTpR(Position position)
        {
            double baseR = Math.Max(0.5, SuggestedTakeProfitR);
            if (!AdaptiveSuggestedTp || Bars == null || Bars.Count < 7)
                return baseR;

            double atr = GetAtrValue();
            if (atr <= 0)
                return baseR;

            // Re-evaluate only from completed M5 candles: adverse displacement and weakening aligned bodies
            // lower the suggested R; strong aligned continuation expands it. This is a guide, never an order.
            double closeNow = Bars.Last(1).Close;
            double closeThen = Bars.Last(6).Close;
            double signedMove = position.TradeType == TradeType.Buy
                ? closeNow - closeThen
                : closeThen - closeNow;
            double directionalScore = Clamp01(signedMove / (atr * 2.5));

            double totalBody = 0.0;
            double totalRange = 0.0;
            double alignedBody = 0.0;
            for (int i = 1; i <= 3; i++)
            {
                var candle = Bars.Last(i);
                double body = Math.Abs(candle.Close - candle.Open);
                double range = Math.Max(Symbol.TickSize, candle.High - candle.Low);
                totalBody += body;
                totalRange += range;
                bool aligned = position.TradeType == TradeType.Buy
                    ? candle.Close > candle.Open
                    : candle.Close < candle.Open;
                if (aligned)
                    alignedBody += body;
            }
            double alignedBodyRatio = totalRange > 0 ? Clamp01(alignedBody / totalRange) : 0.0;
            double alignmentRatio = totalBody > 0 ? Clamp01(alignedBody / totalBody) : 0.0;
            double strength = Clamp01(0.55 * directionalScore + 0.25 * alignedBodyRatio + 0.20 * alignmentRatio);

            double minR = Math.Max(0.25, Math.Min(MinimumAdaptiveSuggestedTpR, MaximumSuggestedTpR));
            double maxR = Math.Max(minR, MaximumSuggestedTpR);
            return minR + strength * (maxR - minR);
        }

        private double GetNearestStructuralBarrier(Position position, double currentPrice, double atr)
        {
            return position == null ? 0.0 : GetNearestStructuralBarrier(position.TradeType, currentPrice, atr);
        }

        private double GetNearestStructuralBarrier(TradeType tradeType, double currentPrice, double atr)
        {
            if (Bars == null || Bars.Count < 5)
                return 0.0;

            int lookback = Math.Min(Math.Max(3, SuggestedTpStructureLookbackBars), Bars.Count - 2);
            double nearest = 0.0;
            if (tradeType == TradeType.Buy)
            {
                double bestDistance = double.MaxValue;
                for (int i = 2; i <= lookback; i++)
                {
                    double high = Bars.Last(i).High;
                    bool swingHigh = high >= Bars.Last(i - 1).High && high > Bars.Last(i + 1).High;
                    if (!swingHigh || high <= currentPrice)
                        continue;
                    double distance = high - currentPrice;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = high;
                    }
                }
            }
            else
            {
                double bestDistance = double.MaxValue;
                for (int i = 2; i <= lookback; i++)
                {
                    double low = Bars.Last(i).Low;
                    bool swingLow = low <= Bars.Last(i - 1).Low && low < Bars.Last(i + 1).Low;
                    if (!swingLow || low >= currentPrice)
                        continue;
                    double distance = currentPrice - low;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = low;
                    }
                }
            }
            return nearest;
        }

        private void UpdateSuggestedTpGuide(Position position, PositionState state)
        {
            if (position == null || state == null || !ShowSuggestedTpGuide || Bars == null || Bars.Count < 2 ||
                !IsFinitePositive(state.InitialRiskDistance) || !IsFinitePositive(SuggestedTakeProfitR))
            {
                if (position != null)
                    RemoveSuggestedTpGuide(position.Id);
                return;
            }

            int currentBarIndex = Bars.Count - 1;
            string lineName = GetSuggestedTpLineName(position.Id);
            string labelName = GetSuggestedTpLabelName(position.Id);

            // Freeze the guide inside each M5 candle. Recalculate only when a new M5 candle starts;
            // this prevents a crossed guide from following price tick-by-tick.
            if (state.LastTpDrawBarIndex == currentBarIndex)
                return;

            double atr = GetAtrValue();
            double trendScore = GetM5DirectionalStrengthScore(position.TradeType, atr);
            double targetR = GetAdaptiveSuggestedTpR(position);
            double direction = position.TradeType == TradeType.Buy ? 1.0 : -1.0;
            // Use the executable liquidation side: Bid for longs, Ask for shorts.
            double marketReference = position.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            double target = position.EntryPrice + direction * state.InitialRiskDistance * targetR;
            bool extendedAfterGuideWasReached = false;
            bool targetReachedButMomentumWeak = false;

            bool previousGuideReached = state.LastSuggestedTpPrice > 0 &&
                (position.TradeType == TradeType.Buy
                    ? marketReference >= state.LastSuggestedTpPrice
                    : marketReference <= state.LastSuggestedTpPrice);
            bool guideReached = state.LastSuggestedTpReached || previousGuideReached ||
                (position.TradeType == TradeType.Buy ? target <= marketReference : target >= marketReference);

            if (guideReached)
            {
                bool continuationStillStrong = trendScore >= 0.58 &&
                    (state.LastTrendScore <= 0.0 || trendScore >= state.LastTrendScore - 0.10);
                if (continuationStillStrong)
                {
                    // The previous target has been reached and completed-bar momentum still supports
                    // extension. Suggest the next objective ahead of executable market price.
                    double extension = atr > 0
                        ? Math.Max(Symbol.TickSize, atr * (0.15 + 0.45 * trendScore))
                        : Math.Max(Symbol.TickSize, Symbol.PipSize);
                    target = marketReference + direction * extension;
                    extendedAfterGuideWasReached = true;
                }
                else
                {
                    // A reached target is not blindly pushed farther when momentum weakens. Keep
                    // the attained guide visible and flag it for the trader's manual exit decision.
                    if (state.LastSuggestedTpPrice > 0 && previousGuideReached)
                        target = state.LastSuggestedTpPrice;
                    targetReachedButMomentumWeak = true;
                }
            }

            // Cap an objective that is still ahead of market at the nearest confirmed swing barrier.
            // For a target already reached under weakening momentum, retain the attained target line.
            double barrier = GetNearestStructuralBarrier(position, marketReference, atr);
            bool atBarrier = false;
            if (!targetReachedButMomentumWeak && barrier > 0 && atr > 0)
            {
                double barrierBuffer = Math.Max(Symbol.TickSize, atr * Math.Max(0.0, SuggestedTpStructureBufferAtr));
                if (position.TradeType == TradeType.Buy && barrier < target)
                {
                    double candidate = barrier - barrierBuffer;
                    target = candidate > marketReference + Symbol.TickSize ? candidate : barrier;
                    atBarrier = Math.Abs(target - barrier) < Symbol.TickSize * 0.5;
                }
                else if (position.TradeType == TradeType.Sell && barrier > target)
                {
                    double candidate = barrier + barrierBuffer;
                    target = candidate < marketReference - Symbol.TickSize ? candidate : barrier;
                    atBarrier = Math.Abs(target - barrier) < Symbol.TickSize * 0.5;
                }
            }

            target = NormalizeSuggestedTpPrice(target, position.TradeType);
            targetR = Math.Abs(target - position.EntryPrice) / state.InitialRiskDistance;
            bool targetOnProfitSide = IsFinitePositive(target) &&
                (position.TradeType == TradeType.Buy ? target > position.EntryPrice : target < position.EntryPrice);
            bool targetBehindMarket = position.TradeType == TradeType.Buy
                ? target <= marketReference
                : target >= marketReference;
            if (!targetOnProfitSide || (targetBehindMarket && !targetReachedButMomentumWeak))
            {
                // If a barrier leaves no room and no attained target is available, avoid a misleading line.
                RemoveSuggestedTpGuide(position.Id);
                state.LastTpDrawBarIndex = currentBarIndex;
                return;
            }

            TimeSpan barDuration = GetKnownIntradayBarDuration();
            if (barDuration == TimeSpan.Zero)
            {
                barDuration = Bars.OpenTimes.LastValue - Bars.OpenTimes.Last(1);
                if (barDuration <= TimeSpan.Zero)
                    barDuration = TimeSpan.FromMinutes(5);
            }

            DateTime startTime = Bars.OpenTimes.LastValue;
            int spanBars = Math.Max(1, SuggestedTpLineLengthBars);
            DateTime endTime = startTime.AddTicks(barDuration.Ticks * spanBars);

            Color guideColor = targetReachedButMomentumWeak ? Color.OrangeRed : Color.Cyan;
            var line = Chart.DrawTrendLine(lineName, startTime, target, endTime, target, guideColor, 2, LineStyle.Solid);
            line.IsInteractive = false;
            line.ExtendToInfinity = false;
            string targetPrefix = state.RiskDistanceEstimated ? "TP GUIDE (est.) " : "TP GUIDE ";
            string trendState = trendScore >= 0.68 ? "trend strong" : trendScore >= 0.40 ? "trend mixed" : "trend weak";
            string objectiveState = targetReachedButMomentumWeak
                ? "; reached — momentum weak; review exit"
                : atBarrier ? "; near structure" : extendedAfterGuideWasReached ? "; extended" : string.Empty;
            var label = Chart.DrawText(labelName,
                targetPrefix + target.ToString("F" + Symbol.Digits, CultureInfo.InvariantCulture) +
                " (" + targetR.ToString("0.##", CultureInfo.InvariantCulture) + "R; " + trendState + objectiveState + ")",
                startTime, target + (position.TradeType == TradeType.Buy ? Symbol.TickSize * 2.0 : -Symbol.TickSize * 2.0), guideColor);
            label.FontSize = 9;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.IsInteractive = false;

            state.LastSuggestedTpPrice = target;
            state.LastSuggestedTpReached = targetReachedButMomentumWeak;
            state.LastTrendScore = trendScore;
            state.LastTpDrawBarIndex = currentBarIndex;
            _tpGuidePositionIds.Add(position.Id);
        }

        private TimeSpan GetKnownIntradayBarDuration()
        {
            string shortName = (TimeFrame.ShortName ?? string.Empty).ToLowerInvariant();
            if (shortName == "m1")
                return TimeSpan.FromMinutes(1);
            if (shortName == "m2")
                return TimeSpan.FromMinutes(2);
            if (shortName == "m3")
                return TimeSpan.FromMinutes(3);
            if (shortName == "m5")
                return TimeSpan.FromMinutes(5);
            return TimeSpan.Zero;
        }

        private PositionState GetOrCreateState(Position position)
        {
            PositionState state;
            if (_states.TryGetValue(position.Id, out state))
                return state;

            string saved = LocalStorage.GetString(GetStateKey(position.Id), SharedScope);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var parts = saved.Split('|');
                double storedEntry;
                double storedRisk;
                long storedEntryTimeTicks;
                bool storedEstimated = false;
                bool estimateFieldValid = parts.Length == 4 ||
                    (parts.Length == 5 && bool.TryParse(parts[4], out storedEstimated));
                if ((parts.Length == 4 || parts.Length == 5) && estimateFieldValid &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out storedEntry) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out storedRisk) &&
                    string.Equals(parts[2], position.TradeType.ToString(), StringComparison.Ordinal) &&
                    long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out storedEntryTimeTicks) &&
                    Math.Abs(storedEntry - position.EntryPrice) <= Symbol.TickSize * 0.5 &&
                    storedEntryTimeTicks == position.EntryTime.Ticks &&
                    IsFinitePositive(storedRisk))
                {
                    state = new PositionState
                    {
                        InitialRiskDistance = storedRisk,
                        RiskDistanceEstimated = storedEstimated
                    };
                    _states[position.Id] = state;
                    return state;
                }
            }

            double risk;
            bool riskEstimated;
            if (position.StopLoss.HasValue && IsStopOnRiskSide(position))
            {
                // The current SL gives a usable distance, but without stored state we cannot know
                // whether it is still the original stop or was tightened manually; mark R as estimated.
                risk = Math.Abs(position.EntryPrice - position.StopLoss.Value);
                riskEstimated = true;
            }
            else
            {
                // If the original risk cannot be restored (e.g. SL is already at/above entry
                // and no saved state exists), use ATR only as an estimate and mark the TP guide.
                double atr = GetAtrValue();
                risk = atr > 0 ? atr * InitialStopAtrMultiplier : FallbackStopPips * Symbol.PipSize;
                riskEstimated = true;
            }

            if (!IsFinitePositive(risk))
            {
                risk = Math.Max(Symbol.TickSize, FallbackStopPips * Symbol.PipSize);
                riskEstimated = true;
            }

            state = new PositionState
            {
                InitialRiskDistance = risk,
                RiskDistanceEstimated = riskEstimated
            };
            _states[position.Id] = state;
            SaveState(position, state);
            return state;
        }

        private bool IsStopOnRiskSide(Position position)
        {
            if (!position.StopLoss.HasValue)
                return false;

            return position.TradeType == TradeType.Buy
                ? position.StopLoss.Value < position.EntryPrice - Symbol.TickSize * 0.5
                : position.StopLoss.Value > position.EntryPrice + Symbol.TickSize * 0.5;
        }

        private double EstimateRoundTurnCommissionPips(Position position)
        {
            if (Symbol.PipValue <= 0 || position.VolumeInUnits <= 0)
                return 0;

            // cTrader Symbol.PipValue is expressed per unit; multiply by position volume (not lots).
            double pipValueForPosition = Symbol.PipValue * position.VolumeInUnits;
            if (pipValueForPosition <= 0 || double.IsNaN(pipValueForPosition) || double.IsInfinity(pipValueForPosition))
                return 0;

            // cTrader documents Position.Commissions as the one-way commission amount associated
            // with the position; double it to estimate a round turn, then add an execution cushion.
            return (2.0 * Math.Abs(position.Commissions)) / pipValueForPosition;
        }

        private void SaveState(Position position, PositionState state)
        {
            string value = string.Format(CultureInfo.InvariantCulture, "{0:R}|{1:R}|{2}|{3}|{4}",
                position.EntryPrice, state.InitialRiskDistance, position.TradeType, position.EntryTime.Ticks,
                state.RiskDistanceEstimated);
            LocalStorage.SetString(GetStateKey(position.Id), value, SharedScope);
            LocalStorage.Flush(SharedScope);
        }

        private string GetStateKey(long positionId)
        {
            // cTrader LocalStorage keys permit only alphanumeric characters and spaces and have a 50-char limit.
            return "SPG State " + Account.Number.ToString(CultureInfo.InvariantCulture) + " " + positionId.ToString(CultureInfo.InvariantCulture);
        }

        private string GetOwnerKey(long positionId)
        {
            // Account + position ID are sufficient; omitting SymbolName keeps the key short and valid.
            return "SPG Owner " + Account.Number.ToString(CultureInfo.InvariantCulture) + " " + positionId.ToString(CultureInfo.InvariantCulture);
        }

        private void ReloadSharedStorageIfNeeded(Position position)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSharedStorageReloadUtc).TotalMilliseconds < 500)
                return;

            // cTrader documents Reload as the way to observe values written by other instances.
            // All writes in this cBot are flushed immediately before relying on them.
            _lastSharedStorageReloadUtc = now;
            try
            {
                LocalStorage.Reload(SharedScope);
            }
            catch (Exception ex)
            {
                WarnThrottled(position, "Could not refresh shared manager lease from local storage: " + ex.Message);
            }
        }

        private bool TryAcquireManagementLease(Position position)
        {
            ReloadSharedStorageIfNeeded(position);
            string key = GetOwnerKey(position.Id);
            OwnershipLease lease;
            long nowTicks = Server.Time.Ticks;

            if (TryReadLease(key, out lease))
            {
                if (lease.Owner == InstanceId)
                {
                    _ownedPositionIds.Add(position.Id);
                    if (lease.ExpiryTicks - nowTicks <= TimeSpan.FromSeconds(OwnershipLeaseSeconds * 0.55).Ticks)
                        WriteLease(key);
                    return true;
                }

                bool alive = lease.ExpiryTicks > nowTicks;
                if (alive && ManagerPriority <= lease.Priority)
                    return false;

                // Expired owner, or this instance has explicitly higher priority: take over.
            }

            WriteLease(key);
            _ownedPositionIds.Add(position.Id);
            return true;
        }

        private void WriteLease(string key)
        {
            long expiryTicks = Server.Time.AddSeconds(OwnershipLeaseSeconds).Ticks;
            string value = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                InstanceId, ManagerPriority, expiryTicks);
            LocalStorage.SetString(key, value, SharedScope);
            LocalStorage.Flush(SharedScope);
        }

        private bool TryReadLease(string key, out OwnershipLease lease)
        {
            lease = null;
            string raw = LocalStorage.GetString(key, SharedScope);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string[] parts = raw.Split('|');
            int priority;
            long expiryTicks;
            if (parts.Length != 3 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out priority) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expiryTicks))
                return false;

            lease = new OwnershipLease
            {
                Owner = parts[0],
                Priority = priority,
                ExpiryTicks = expiryTicks
            };
            return true;
        }

        private double GetAtrValue()
        {
            // Use the last completed bar's ATR rather than the forming bar to prevent a temporary
            // intrabar volatility spike from constantly changing the risk basis.
            if (_atr == null || Bars == null || Bars.Count < AtrPeriod + 2)
                return 0;

            double value = _atr.Result.Last(1);
            return IsFinitePositive(value) ? value : 0;
        }

        private double GetMinimumStopDistance(double referencePrice)
        {
            double minimum = Symbol.MinStopLossDistance;
            double priceDistance;

            if (minimum <= 0)
                priceDistance = Symbol.TickSize;
            else if (Symbol.MinDistanceType == SymbolMinDistanceType.Pips)
                priceDistance = minimum * Symbol.PipSize;
            else
                priceDistance = referencePrice * minimum / 100.0;

            // Add a small tick cushion so rounding/network timing is less likely to put a stop
            // exactly on the broker's rejection boundary.
            priceDistance += Math.Max(0, BrokerDistanceSafetyTicks) * Symbol.TickSize;
            return Math.Max(priceDistance, Symbol.TickSize);
        }

        private double NormalizeStopPrice(double price, TradeType tradeType, bool tightening)
        {
            double tickSize = Symbol.TickSize;
            if (tickSize <= 0)
                return Math.Round(price, Symbol.Digits);

            double units = price / tickSize;
            double roundedUnits;

            // Initial stops round away from market to avoid invalid distances. Later tightening
            // rounds toward profit, then revalidates broker distance before submission.
            if (tradeType == TradeType.Buy)
                roundedUnits = tightening ? Math.Ceiling(units - 1e-10) : Math.Floor(units + 1e-10);
            else
                roundedUnits = tightening ? Math.Floor(units + 1e-10) : Math.Ceiling(units - 1e-10);

            return Math.Round(roundedUnits * tickSize, Symbol.Digits);
        }

        private double MoreProtectiveStop(TradeType tradeType, double current, double candidate)
        {
            return tradeType == TradeType.Buy ? Math.Max(current, candidate) : Math.Min(current, candidate);
        }

        private bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void WarnThrottled(Position position, string message)
        {
            DateTime now = Server.Time;
            DateTime last;
            if (!_lastWarningTime.TryGetValue(position.Id, out last) || (now - last).TotalSeconds >= 10)
            {
                Diag("[Position {0}] {1}", position.Id, message);
                _lastWarningTime[position.Id] = now;
            }
        }
    }
}
