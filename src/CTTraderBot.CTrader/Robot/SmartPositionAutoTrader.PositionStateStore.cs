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
            bool riskBudgetUnavailable = false;
            try
            {
                saved = LocalStorage.GetString(GetStateKey(position.Id), SharedScope);
                _positionStateStorageHealthy = true;
            }
            catch (Exception ex)
            {
                _positionStateStorageHealthy = false;
                riskBudgetUnavailable = true;
                WarnThrottled(position, "Could not restore saved position state; falling back to estimated R: " + ex.Message);
                SetVolumeNotice("Position state could not be read. The approved risk budget is unknown; new entries remain paused while this position is open.", true);
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
                bool storedBudgetUnavailable = false;
                bool storedEmergencyCloseRequired = false;
                bool storedRiskReviewSticky = false;
                bool isVersion5 = parts.Length == 11 && string.Equals(parts[0], "v5", StringComparison.Ordinal);
                bool isVersion4 = parts.Length == 10 && string.Equals(parts[0], "v4", StringComparison.Ordinal);
                bool isVersion3 = parts.Length == 9 && string.Equals(parts[0], "v3", StringComparison.Ordinal);
                bool isVersion2 = parts.Length == 8 && string.Equals(parts[0], "v2", StringComparison.Ordinal);
                bool isLegacy = parts.Length == 4 || parts.Length == 5;

                bool parsedFields = false;
                if (isVersion5)
                {
                    parsedFields =
                        bool.TryParse(parts[5], out storedEstimated) &&
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out storedApprovedRisk) &&
                        bool.TryParse(parts[7], out storedReviewRequired) &&
                        bool.TryParse(parts[8], out storedBudgetUnavailable) &&
                        bool.TryParse(parts[9], out storedEmergencyCloseRequired) &&
                        bool.TryParse(parts[10], out storedRiskReviewSticky) &&
                        !double.IsNaN(storedApprovedRisk) &&
                        !double.IsInfinity(storedApprovedRisk) &&
                        storedApprovedRisk >= 0.0;
                }
                else if (isVersion4)
                {
                    parsedFields =
                        bool.TryParse(parts[5], out storedEstimated) &&
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out storedApprovedRisk) &&
                        bool.TryParse(parts[7], out storedReviewRequired) &&
                        bool.TryParse(parts[8], out storedBudgetUnavailable) &&
                        bool.TryParse(parts[9], out storedEmergencyCloseRequired) &&
                        !double.IsNaN(storedApprovedRisk) &&
                        !double.IsInfinity(storedApprovedRisk) &&
                        storedApprovedRisk >= 0.0;
                }
                else if (isVersion3)
                {
                    parsedFields =
                        bool.TryParse(parts[5], out storedEstimated) &&
                        double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out storedApprovedRisk) &&
                        bool.TryParse(parts[7], out storedReviewRequired) &&
                        bool.TryParse(parts[8], out storedBudgetUnavailable) &&
                        !double.IsNaN(storedApprovedRisk) &&
                        !double.IsInfinity(storedApprovedRisk) &&
                        storedApprovedRisk >= 0.0;
                }
                else if (isVersion2)
                {
                    // Accept the earlier v2 state shape and immediately upgrade it to v4.
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

                int offset = isVersion5 || isVersion4 || isVersion3 || isVersion2 ? 1 : 0;
                if ((isVersion5 || isVersion4 || isVersion3 || isVersion2 || isLegacy) && parsedFields &&
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
                        ApprovedRiskAmount = isVersion5 || isVersion4 || isVersion3 || isVersion2 ? storedApprovedRisk : 0.0,
                        RiskReviewRequired = (isVersion5 || isVersion4 || isVersion3 || isVersion2) && storedReviewRequired,
                        RiskBudgetUnavailable = (isVersion5 || isVersion4 || isVersion3) && storedBudgetUnavailable,
                        EmergencyCloseRequired = (isVersion5 || isVersion4) && storedEmergencyCloseRequired,
                        RiskReviewSticky = isVersion5 && storedRiskReviewSticky
                    };
                    _states[position.Id] = state;
                    if (!isVersion5)
                        SaveState(position, state); // Upgrade earlier versioned/legacy state to current v5.
                    return state;
                }
            }

            // A non-empty record that could not be validated may have contained the approved
            // budget. Do not replace it with an estimated R and silently treat the position safe.
            if (!string.IsNullOrWhiteSpace(saved))
                riskBudgetUnavailable = true;

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
                RiskDistanceEstimated = riskEstimated,
                RiskReviewRequired = riskBudgetUnavailable,
                RiskBudgetUnavailable = riskBudgetUnavailable
            };
            _states[position.Id] = state;
            SaveState(position, state);
            return state;
        }

        private void SaveState(Position position, PositionState state)
        {
            string value = string.Format(CultureInfo.InvariantCulture, "v5|{0:R}|{1:R}|{2}|{3}|{4}|{5:R}|{6}|{7}|{8}|{9}",
                position.EntryPrice, state.InitialRiskDistance, position.TradeType, position.EntryTime.Ticks,
                state.RiskDistanceEstimated, Math.Max(0.0, state.ApprovedRiskAmount), state.RiskReviewRequired,
                state.RiskBudgetUnavailable, state.EmergencyCloseRequired, state.RiskReviewSticky);
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
