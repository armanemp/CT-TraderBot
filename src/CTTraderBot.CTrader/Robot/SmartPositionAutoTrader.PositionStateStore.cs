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
                PositionRiskStateRecord restored;
                if (PositionRiskStateCodec.TryDeserialize(
                    saved,
                    position.EntryPrice,
                    position.TradeType.ToString(),
                    position.EntryTime.Ticks,
                    Symbol.TickSize,
                    out restored))
                {
                    state = new PositionState
                    {
                        InitialRiskDistance = restored.InitialRiskDistance,
                        RiskDistanceEstimated = restored.RiskDistanceEstimated,
                        ApprovedRiskAmount = restored.ApprovedRiskAmount,
                        RiskReviewRequired = restored.RiskReviewRequired,
                        RiskBudgetUnavailable = restored.RiskBudgetUnavailable,
                        EmergencyCloseRequired = restored.EmergencyCloseRequired,
                        RiskReviewSticky = restored.RiskReviewSticky
                    };
                    _states[position.Id] = state;
                    if (restored.Version != PositionRiskStateCodec.CurrentVersion)
                        SaveState(position, state); // Upgrade every accepted legacy/versioned record to current v5.
                    return state;
                }

                // A non-empty record that failed parsing/identity validation may have contained
                // the approved budget or an emergency-close request; keep the entry lock closed.
                riskBudgetUnavailable = true;
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
            try
            {
                string value = PositionRiskStateCodec.Serialize(new PositionRiskStateRecord
                {
                    EntryPrice = position.EntryPrice,
                    InitialRiskDistance = state.InitialRiskDistance,
                    TradeType = position.TradeType.ToString(),
                    EntryTimeTicks = position.EntryTime.Ticks,
                    RiskDistanceEstimated = state.RiskDistanceEstimated,
                    ApprovedRiskAmount = Math.Max(0.0, state.ApprovedRiskAmount),
                    RiskReviewRequired = state.RiskReviewRequired,
                    RiskBudgetUnavailable = state.RiskBudgetUnavailable,
                    EmergencyCloseRequired = state.EmergencyCloseRequired,
                    RiskReviewSticky = state.RiskReviewSticky
                });
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
