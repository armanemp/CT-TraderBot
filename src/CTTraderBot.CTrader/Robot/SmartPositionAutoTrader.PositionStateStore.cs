using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private PositionState GetOrCreateState(Position position)
        {
            PositionState state;
            if (_states.TryGetValue(position.Id, out state))
                return state;

            string saved = null;
            try
            {
                saved = LocalStorage.GetString(GetStateKey(position.Id), SharedScope);
                _positionStateStorageHealthy = true;
            }
            catch (Exception ex)
            {
                _positionStateStorageHealthy = false;
                WarnThrottled(position, "Could not restore saved position state; falling back to estimated R: " + ex.Message);
                SetVolumeNotice("Position state could not be read. Any recovered R/TP guide will be marked estimated.", true);
            }

            if (!string.IsNullOrWhiteSpace(saved))
            {
                var parts = saved.Split('|');
                double storedEntry;
                double storedRisk;
                double storedApprovedRisk = 0.0;
                long storedEntryTimeTicks;
                bool storedEstimated = true;
                bool storedReviewRequired = false;
                bool isVersion2 = parts.Length == 8 && string.Equals(parts[0], "v2", StringComparison.Ordinal);
                bool isLegacy = parts.Length == 4 || parts.Length == 5;

                bool parsedFields = false;
                if (isVersion2)
                {
                    parsedFields =
                        bool.TryParse(parts[5], out storedEstimated) &&
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out storedApprovedRisk) &&
                        bool.TryParse(parts[7], out storedReviewRequired) &&
                        !double.IsNaN(storedApprovedRisk) &&
                        !double.IsInfinity(storedApprovedRisk) &&
                        storedApprovedRisk >= 0.0;
                }
                else if (isLegacy)
                {
                    // Four-field records predate the estimate marker; five-field records contain it.
                    parsedFields = parts.Length == 4 || bool.TryParse(parts[4], out storedEstimated);
                }

                int offset = isVersion2 ? 1 : 0;
                if ((isVersion2 || isLegacy) && parsedFields &&
                    double.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out storedEntry) &&
                    double.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out storedRisk) &&
                    string.Equals(parts[offset + 2], position.TradeType.ToString(), StringComparison.Ordinal) &&
                    long.TryParse(parts[offset + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out storedEntryTimeTicks) &&
                    Math.Abs(storedEntry - position.EntryPrice) <= Symbol.TickSize * 0.5 &&
                    storedEntryTimeTicks == position.EntryTime.Ticks &&
                    IsFinitePositive(storedRisk) &&
                    storedRisk >= Symbol.TickSize * 0.5)
                {
                    state = new PositionState
                    {
                        InitialRiskDistance = storedRisk,
                        RiskDistanceEstimated = storedEstimated,
                        ApprovedRiskAmount = isVersion2 ? storedApprovedRisk : 0.0,
                        RiskReviewRequired = isVersion2 && storedReviewRequired
                    };
                    _states[position.Id] = state;
                    if (!isVersion2)
                        SaveState(position, state); // Upgrade legacy state to the versioned representation.
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

        private void SaveState(Position position, PositionState state)
        {
            string value = string.Format(CultureInfo.InvariantCulture, "v2|{0:R}|{1:R}|{2}|{3}|{4}|{5:R}|{6}",
                position.EntryPrice, state.InitialRiskDistance, position.TradeType, position.EntryTime.Ticks,
                state.RiskDistanceEstimated, Math.Max(0.0, state.ApprovedRiskAmount), state.RiskReviewRequired);
            try
            {
                LocalStorage.SetString(GetStateKey(position.Id), value, SharedScope);
                LocalStorage.Flush(SharedScope);
                _positionStateStorageHealthy = true;
            }
            catch (Exception ex)
            {
                _positionStateStorageHealthy = false;
                WarnThrottled(position, "Position state could not be persisted; current-session management continues, but restart recovery may be estimated: " + ex.Message);
                SetVolumeNotice("Position state could not be saved. Restart recovery may use an estimated initial risk.", true);
            }
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
    }
}
