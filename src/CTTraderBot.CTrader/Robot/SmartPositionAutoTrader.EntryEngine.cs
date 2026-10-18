using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
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
    }
}
