using System;
using System.Collections.Generic;
using System.Globalization;
using CTTraderBot.Core.Policies;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private void TryOpenHybridPosition(bool allowIntrabarBreakout)
        {
            if (!EnableAutomaticEntries)
            {
                ReportEntryBlocked("automatic entries are disabled in the cBot parameters");
                return;
            }

            if (Bars == null || _lowerBars == null ||
                Bars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10))
            {
                ReportEntryBlocked("waiting for sufficient M5 and lower-timeframe history");
                return;
            }

            if (!CanStartNewEntry())
                return;

            DateTime activeLowerBarTime = _lowerBars.LastBar.OpenTime;
            if (activeLowerBarTime <= _lastEntryAttemptLowerBarOpenTime)
                return;

            // Cooldown is expressed in LOWER-timeframe bars, not M5 bars.
            if (EntryCooldownBars > 0 && _lowerBars.Count - _lastEntryBarIndex < EntryCooldownBars)
            {
                ReportEntryBlocked("lower-timeframe entry cooldown is active");
                return;
            }

            int openCount = Positions.FindAll(AutoTradeLabel, SymbolName).Length;
            if (openCount >= MaximumOpenPositions)
            {
                ReportEntryBlocked("maximum number of managed positions is already open");
                return;
            }

            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            if (MaximumSpreadPips > 0 && spreadPips > MaximumSpreadPips)
            {
                ReportEntryBlocked("current spread exceeds the configured pip limit");
                return;
            }

            TradeType? signal = GetHybridEntrySignal(allowIntrabarBreakout && UseIntrabarMicroSwingBreakout);
            if (!signal.HasValue)
            {
                ReportEntryBlocked("no valid closed-bar setup or fresh micro-swing crossing at this tick");
                return;
            }

            // Run remaining checks before consuming this lower-TF candle. If a closed-bar pattern
            // fails sizing/quality, a later fresh micro-swing cross in the same candle may still qualify.
            double atr = GetAtrValue();
            double lowerAtr = GetLowerAtrValue();
            if (atr <= 0 || lowerAtr <= 0)
            {
                ReportEntryBlocked("waiting for valid M5 and lower-timeframe ATR values");
                return;
            }

            double spread = Math.Max(0, Symbol.Ask - Symbol.Bid);
            double spreadAtrRatio = atr > 0 ? spread / atr : double.MaxValue;
            if (MaximumSpreadToAtrRatio > 0 && spreadAtrRatio > MaximumSpreadToAtrRatio)
            {
                ReportEntryBlocked("spread is too large relative to M5 ATR");
                return;
            }

            double trendStrengthBeforeEntry = GetM5DirectionalStrengthScore(signal.Value, atr);
            // This is a soft quality check. A confirmed lower-TF structure break can override it,
            // so an early reversal is not rejected only because M5 has not turned yet.
            bool confirmedLowerShift = IsConfirmedLowerStructureBreak(signal.Value);
            if (trendStrengthBeforeEntry < MinimumM5TrendStrength && !confirmedLowerShift)
            {
                ReportEntryBlocked("M5 directional strength is below threshold and no confirmed lower-timeframe structure shift exists");
                return;
            }

            double entryReference = signal.Value == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double fastEmaReference = _entryFastEma.Result.Last(1);
            double entryExtension = Math.Abs(entryReference - fastEmaReference) / atr;
            if (MaximumEntryExtensionAtr > 0 && entryExtension > MaximumEntryExtensionAtr)
            {
                ReportEntryBlocked("entry is too extended from the M5 fast EMA");
                return;
            }

            // The first stop anchor is LOWER-timeframe market structure, not a wide M5 candle range.
            double buffer = Math.Max(2 * Symbol.TickSize,
                Math.Max(spread * Math.Max(1.0, M5SpreadBufferMultiplier), lowerAtr * 0.10));
            int lookback = Math.Max(3, Math.Min(EntryStructureLookbackBars, _lowerBars.Count - 2));
            double structuralExtreme = GetLowerStructuralStopAnchor(signal.Value, lookback);
            if (!IsFinitePositive(structuralExtreme))
            {
                ReportEntryBlocked("no valid lower-timeframe structural stop anchor was found");
                return;
            }

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
            {
                ReportEntryBlocked("planned structural stop distance is invalid or exceeds the lower-TF ATR limit");
                return;
            }

            double stopPips = stopDistance / Symbol.PipSize;
            double targetR = GetEntryTargetR(signal.Value, atr, entryReference, stopDistance); // visual guide only
            if (targetR < MinimumOpportunityRewardR)
            {
                ReportEntryBlocked("forward structural room is below the minimum estimated reward/risk requirement");
                return;
            }

            double setupQuality = GetOpportunityQualityScore(signal.Value, atr, targetR);
            // A lower-TF structural break can create an excellent early entry even before M5 strength peaks.
            if (setupQuality < MinimumEntryQualityScore)
            {
                ReportEntryBlocked("composite setup quality is below the configured threshold");
                return;
            }

            double allowedRiskPercent = GetAdaptiveAllowedRiskPercent(setupQuality);
            double equity = Account.Equity;
            if (!IsFinitePositive(equity) || allowedRiskPercent <= 0)
            {
                ReportEntryBlocked("equity or calculated risk budget is invalid", true);
                return;
            }

            // Risk budget is recalculated from current equity on every potential entry.
            double maximumAllowedLoss = equity * allowedRiskPercent / 100.0;
            double stopAndSlippagePips = stopPips + Math.Max(0.0, SizingSlippageReservePips);
            if (!IsFinitePositive(maximumAllowedLoss) || !IsFinitePositive(stopAndSlippagePips))
            {
                ReportEntryBlocked("risk budget or stop-loss pip estimate is invalid", true);
                return;
            }

            // Verify the broker's commission schedule and account-currency conversion before
            // calculating volume. Unknown transaction costs must not silently become zero.
            double minimumVolumeRisk = EstimateTotalEntryLoss(
                Symbol.VolumeInUnitsMin, stopAndSlippagePips, entryReference);
            if (!IsFinitePositive(minimumVolumeRisk))
            {
                SetVolumeNotice("Entry skipped: full downside risk could not be estimated, including broker round-turn commission and account-currency conversion.", true);
                ReportEntryBlocked("broker commission or currency conversion is unavailable for a reliable risk estimate", true);
                return;
            }

            double volume;
            bool manualVolumeReducedForMinorOverrun = false;
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

                volume = GetMaximumVolumeWithinRiskBudget(
                    signal.Value, marginLimitedUnits, maximumAllowedLoss,
                    stopAndSlippagePips, entryReference);
                double minAutoUnits = Symbol.NormalizeVolumeInUnits(Symbol.QuantityToVolumeInUnits(MinimumAutoVolumeLots), RoundingMode.Up);
                if (volume < Math.Max(Symbol.VolumeInUnitsMin, minAutoUnits))
                {
                    SetVolumeNotice("Auto mode: stop distance, slippage reserve, broker commission and risk budget do not support the configured minimum volume.", true);
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

            double estimatedLoss = EstimateTotalEntryLoss(volume, stopAndSlippagePips, entryReference);
            if (!IsFinitePositive(estimatedLoss))
            {
                SetVolumeNotice("Entry skipped: stop risk plus round-turn commission could not be estimated reliably.", true);
                return;
            }
            if (estimatedLoss > maximumAllowedLoss * 1.001)
            {
                // In Manual mode, a small (<=5%) risk-budget overrun caused by reserve/rounding
                // differences is corrected by reducing volume to the largest broker-valid step
                // within budget. Larger overruns still require the user to revise the requested lot.
                if (_runtimeVolumeMode == MicroSwingPrecisionVolumeMode.ManualLots &&
                    estimatedLoss <= maximumAllowedLoss * (1.0 + SmallManualRiskOverrunTolerance))
                {
                    double requestedUnits = volume;
                    double adjustedUnits = GetMaximumVolumeWithinRiskBudget(
                        signal.Value, requestedUnits, maximumAllowedLoss,
                        stopAndSlippagePips, entryReference);

                    if (adjustedUnits >= Symbol.VolumeInUnitsMin && adjustedUnits < requestedUnits)
                    {
                        volume = adjustedUnits;
                        estimatedLoss = EstimateTotalEntryLoss(volume, stopAndSlippagePips, entryReference);
                        manualVolumeReducedForMinorOverrun =
                            IsFinitePositive(estimatedLoss) && estimatedLoss <= maximumAllowedLoss * 1.001;
                    }
                }

                if (!IsFinitePositive(estimatedLoss) || estimatedLoss > maximumAllowedLoss * 1.001)
                {
                    SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                        "Entry skipped: estimated stop, slippage and round-turn commission risk is {0:F2} {1}, above this setup's {2:F2} budget. Reduce the selected volume or review the risk limits.",
                        estimatedLoss, Account.Asset.Name, maximumAllowedLoss), true);
                    return;
                }
            }

            bool usedCommissionFallbackForEntry = _lastCommissionEstimateUsedFallback;
            string riskAdjustmentNotice = string.Empty;
            if (usedCommissionFallbackForEntry)
                riskAdjustmentNotice += String.Format(CultureInfo.InvariantCulture,
                    " | conservative commission reserve used ({0:F1} pip-equivalent minimum on gold profile)",
                    UnknownGoldCommissionFallbackPips);
            if (manualVolumeReducedForMinorOverrun)
                riskAdjustmentNotice += " | manual volume was reduced one or more broker steps to fit the approved risk budget";

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

            bool riskEstimateNeedsAttention = usedCommissionFallbackForEntry || manualVolumeReducedForMinorOverrun;
            ReportEntryStatus(String.Format(CultureInfo.InvariantCulture,
                "qualified: {0:F2} lots; estimated stop + slippage + round-turn commission risk {1:F2} {2}{3}",
                Symbol.VolumeInUnitsToQuantity(volume), estimatedLoss, Account.Asset.Name, riskAdjustmentNotice),
                riskEstimateNeedsAttention);
            SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                "Ready: {0:F2} lots | estimated stop + slippage + round-turn commission risk {1:F2} {2} | required margin {3:F2}{4}.",
                Symbol.VolumeInUnitsToQuantity(volume), estimatedLoss, Account.Asset.Name, requiredMargin, riskAdjustmentNotice),
                riskEstimateNeedsAttention);

            string comment = "M5+" + LowerEntryTimeframe.ToString() + " precision | " + _runtimeVolumeMode.ToString() + " | TP guide only";
            // Consume the candle only when sending the order; filter failures do not block a later fresh trigger.
            _lastEntryAttemptLowerBarOpenTime = activeLowerBarTime;
            // Real broker SL is sent; TP is explicitly null because the target is a chart-only guide.
            var result = ExecuteMarketOrder(signal.Value, SymbolName, volume, AutoTradeLabel, stopPips, null, comment);
            if (!result.IsSuccessful)
            {
                ReportEntryStatus("broker rejected the order: " + result.Error.ToString(), true);
                SetVolumeNotice("Broker rejected the order: " + result.Error.ToString(), true);
                return;
            }

            _lastEntryBarIndex = _lowerBars.Count;
            ReportEntryStatus("market order accepted; checking attached initial SL" + riskAdjustmentNotice,
                riskEstimateNeedsAttention);
            SetVolumeNotice(String.Format(CultureInfo.InvariantCulture,
                "Opened {0:F2} lots. Initial SL is attached; TP remains guide-only{1}.",
                Symbol.VolumeInUnitsToQuantity(volume), riskAdjustmentNotice), riskEstimateNeedsAttention);

            if (result.Position != null)
            {
                // Save the approved budget before attempting absolute structural alignment. If the
                // planned level was crossed in the meantime, the emergency-close lock must survive
                // this instance's restart and be visible to a second instance.
                bool hasValidActualStop = HasUsableStopLoss(result.Position);
                double actualRiskDistance = hasValidActualStop
                    ? Math.Abs(result.Position.EntryPrice - result.Position.StopLoss.Value)
                    : stopDistance;

                var entryState = new PositionState
                {
                    InitialRiskDistance = actualRiskDistance,
                    RiskDistanceEstimated = !hasValidActualStop,
                    ApprovedRiskAmount = maximumAllowedLoss,
                    RiskReviewRequired = false,
                    LastSuggestedTpPrice = 0,
                    LastTpDrawBarIndex = -1
                };
                _states[result.Position.Id] = entryState;
                RefreshPostFillRiskReview(result.Position, entryState, false);
                SaveState(result.Position, entryState);

                AlignFilledPositionStopToStructure(result.Position, plannedStop);
                if (!IsPositionStillOpen(result.Position.Id) || _pendingEmergencyCloseIds.Contains(result.Position.Id))
                    return;

                hasValidActualStop = HasUsableStopLoss(result.Position);
                actualRiskDistance = hasValidActualStop
                    ? Math.Abs(result.Position.EntryPrice - result.Position.StopLoss.Value)
                    : stopDistance;
                entryState.InitialRiskDistance = actualRiskDistance;
                entryState.RiskDistanceEstimated = !hasValidActualStop;

                // Re-assess after the structural SL is confirmed. The broker-valid structural
                // stop is never pulled inside the wick merely to satisfy the risk budget.
                RefreshPostFillRiskReview(result.Position, entryState, false);
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

            TrendBias signalBias = lowerSignal.Value == TradeType.Buy ? TrendBias.Bullish : TrendBias.Bearish;
            TrendBias currentM5Bias = m5Bias.HasValue
                ? (m5Bias.Value == TradeType.Buy ? TrendBias.Bullish : TrendBias.Bearish)
                : TrendBias.Neutral;
            if (!M5TrendAlignmentPolicy.Allows(currentM5Bias, signalBias, RequireM5TrendAlignment))
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

    }
}
