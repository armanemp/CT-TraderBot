using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
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

    }
}
