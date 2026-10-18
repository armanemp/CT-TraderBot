using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
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
    }
}
