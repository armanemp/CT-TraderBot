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
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume : Robot
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
        private readonly HashSet<long> _riskReviewRequiredPositionIds = new HashSet<long>();
        private readonly Dictionary<long, DateTime> _lastEmergencyCloseAttemptTime = new Dictionary<long, DateTime>();
        private readonly HashSet<long> _tpGuidePositionIds = new HashSet<long>();
        private DateTime _lastSharedStorageReloadUtc = DateTime.MinValue;
        private bool _sharedStorageHealthy = true;

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
        private TextBlock _entryStatusText;
        private bool _positionStateStorageHealthy = true;

        private const LocalStorageScope SharedScope = LocalStorageScope.Device;

        // Silent by default: the cBot runs autonomously without routine logs/popups.
        // Enable diagnostics only when investigating a specific issue.


        private sealed class PositionState
        {
            public double InitialRiskDistance;
            public bool RiskDistanceEstimated;
            // Approved loss amount captured before submission. Zero means no budget was persisted
            // (legacy/adopted position); such positions still require a usable broker SL.
            public double ApprovedRiskAmount;
            public bool RiskReviewRequired;
            // True when a persisted/approved budget cannot be trusted; never auto-clear this guard.
            public bool RiskBudgetUnavailable;
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


















































































































































    }
}
