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
    }
}
