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
                FlagRiskReview(position,
                    "price crossed the intended absolute structural stop during/just after entry; the stop will not be widened to disguise the breach",
                    CloseIfInitialStopAlreadyCrossed,
                    true);
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
                // Conservative M5 fallback: use the configured number of completed candles so a
                // single anomalous candle does not define the recovery stop by itself.
                int wickLookback = Math.Max(1, Math.Min(StructuralWickLookbackBars, Bars.Count - 1));
                double m5Extreme = position.TradeType == TradeType.Buy ? double.MaxValue : double.MinValue;
                for (int i = 1; i <= wickLookback; i++)
                {
                    if (position.TradeType == TradeType.Buy)
                        m5Extreme = Math.Min(m5Extreme, Bars.Last(i).Low);
                    else
                        m5Extreme = Math.Max(m5Extreme, Bars.Last(i).High);
                }

                double m5Buffer = Math.Max(2 * Symbol.TickSize,
                    Math.Max(spread * M5SpreadBufferMultiplier, m5Atr * M5AtrBufferFraction));
                plannedStop = position.TradeType == TradeType.Buy
                    ? m5Extreme - m5Buffer
                    : m5Extreme + m5Buffer;
                method = "M5 fallback wick";
                usedFallback = true;
            }

            wrongSide = position.TradeType == TradeType.Buy
                ? plannedStop >= position.EntryPrice - Symbol.TickSize || plannedStop <= 0
                : plannedStop <= position.EntryPrice + Symbol.TickSize || plannedStop <= 0;
            if (wrongSide)
            {
                double fallbackAtr = lowerAtr > 0 ? lowerAtr : m5Atr;
                if (fallbackAtr > 0)
                {
                    double atrFallbackDistance = Math.Max(
                        fallbackAtr * M5FallbackAtrMultiplier,
                        spread * M5SpreadBufferMultiplier + 2 * Symbol.TickSize);
                    plannedStop = position.TradeType == TradeType.Buy
                        ? position.EntryPrice - atrFallbackDistance
                        : position.EntryPrice + atrFallbackDistance;
                    method = "ATR emergency fallback";
                }
                else
                {
                    // Final recovery path if both timeframe ATR values are unavailable.
                    double fixedDistance = Math.Max(FallbackStopPips * Symbol.PipSize, 2 * Symbol.TickSize);
                    if (!IsFinitePositive(fixedDistance))
                        return RetryInitialStop(position, "Neither ATR nor a valid fixed-pip fallback distance is available.");

                    plannedStop = position.TradeType == TradeType.Buy
                        ? position.EntryPrice - fixedDistance
                        : position.EntryPrice + fixedDistance;
                    method = "fixed-pip fallback (ATR unavailable)";
                }
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
                FlagRiskReview(position,
                    "price crossed the intended initial stop before broker protection could be confirmed; the stop will not be widened",
                    CloseIfInitialStopAlreadyCrossed,
                    true);

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

            PositionState state;
            if (!_states.TryGetValue(position.Id, out state) || state == null)
                state = new PositionState();

            // Update only the measured initial-R fields. Preserve the approved-risk budget and
            // review flag so re-protecting a position cannot accidentally clear the entry guard.
            state.InitialRiskDistance = Math.Abs(position.EntryPrice - stopPrice);
            state.RiskDistanceEstimated = false;
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

    }
}
