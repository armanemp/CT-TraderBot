using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
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

        private bool IsStopOnRiskSide(Position position)
        {
            if (!position.StopLoss.HasValue)
                return false;

            return position.TradeType == TradeType.Buy
                ? position.StopLoss.Value < position.EntryPrice - Symbol.TickSize * 0.5
                : position.StopLoss.Value > position.EntryPrice + Symbol.TickSize * 0.5;
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
    }
}
