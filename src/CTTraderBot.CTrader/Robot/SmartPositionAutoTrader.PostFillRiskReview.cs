using System;
using System.Globalization;
using CTTraderBot.Core.Policies;
using cAlgo.API;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private const double PostFillRiskToleranceFraction = 0.05;

        private void FlagRiskReview(Position position, string reason, bool emergencyCloseRequired = false, bool stickyReviewRequired = false)
        {
            if (position == null)
                return;

            PositionState state;
            if (!_states.TryGetValue(position.Id, out state) || state == null)
                state = GetOrCreateState(position);

            if (state != null)
            {
                state.RiskReviewRequired = true;
                state.EmergencyCloseRequired = state.EmergencyCloseRequired || emergencyCloseRequired;
                state.RiskReviewSticky = state.RiskReviewSticky || stickyReviewRequired;
                _states[position.Id] = state;
                // This is an exceptional crossed-stop path. Persist even if the in-memory flag
                // was already true, so a second instance/restart cannot miss the emergency review.
                SaveState(position, state);
            }

            _riskReviewRequiredPositionIds.Add(position.Id);
            string notice = "RISK GUARD: " + reason +
                ". Emergency review is required; new entries remain paused until the position closes or a valid risk assessment clears the guard.";
            WarnThrottled(position, notice);
            SetVolumeNotice(notice, true);
            ReportEntryStatus("new entries paused: " + reason, true);
        }

        private bool HasUsableStopLoss(Position position)
        {
            if (position == null || !position.StopLoss.HasValue ||
                !IsFinitePositive(position.StopLoss.Value))
                return false;

            // A stop must remain on the protective side of the live quote. A stop already
            // crossed while the position is still reported open is treated as unconfirmed.
            return position.TradeType == TradeType.Buy
                ? position.StopLoss.Value < Symbol.Bid
                : position.StopLoss.Value > Symbol.Ask;
        }

        private double EstimateCurrentDownsideRisk(Position position)
        {
            if (!HasUsableStopLoss(position) || !IsFinitePositive(Symbol.PipSize) ||
                !IsFinitePositive(position.VolumeInUnits))
                return double.NaN;

            double stopDistance = position.TradeType == TradeType.Buy
                ? Math.Max(0.0, position.EntryPrice - position.StopLoss.Value)
                : Math.Max(0.0, position.StopLoss.Value - position.EntryPrice);

            double commissionPips = EstimateRoundTurnCommissionPips(position);
            if (double.IsNaN(commissionPips) || double.IsInfinity(commissionPips) || commissionPips < 0.0)
                return double.NaN;

            double riskPips = stopDistance / Symbol.PipSize +
                Math.Max(0.0, SizingSlippageReservePips) + commissionPips;
            if (double.IsNaN(riskPips) || double.IsInfinity(riskPips) || riskPips < 0.0)
                return double.NaN;

            double estimatedLoss = Symbol.AmountRisked(position.VolumeInUnits, riskPips);
            return !double.IsNaN(estimatedLoss) && !double.IsInfinity(estimatedLoss) && estimatedLoss >= 0.0
                ? estimatedLoss
                : double.NaN;
        }

        private void RefreshPostFillRiskReview(Position position, PositionState state, bool persistChanges = true)
        {
            if (position == null || state == null)
                return;

            bool requiresReview;
            string reason = string.Empty;
            double estimatedLoss = double.NaN;

            if (state.EmergencyCloseRequired)
            {
                requiresReview = true;
                reason = "the intended stop was crossed and emergency close is still required";
            }
            else if (state.RiskReviewSticky)
            {
                requiresReview = true;
                reason = "the intended structural stop was crossed; manual review is required because automatic close is disabled";
            }
            else if (!HasUsableStopLoss(position))
            {
                requiresReview = true;
                reason = "the position has no usable broker stop-loss";
            }
            else if (state.RiskBudgetUnavailable)
            {
                requiresReview = true;
                reason = "the saved approved risk budget is missing or corrupt and cannot be verified";
            }
            else if (state.ApprovedRiskAmount <= 0.0)
            {
                // Legacy/adopted positions have no recoverable original budget. A usable broker
                // stop is required, but do not invent an entry-specific budget for old state.
                requiresReview = false;
            }
            else
            {
                estimatedLoss = EstimateCurrentDownsideRisk(position);
                PostFillRiskAssessment assessment = PostFillRiskPolicy.Assess(
                    estimatedLoss, state.ApprovedRiskAmount, PostFillRiskToleranceFraction);

                requiresReview = assessment != PostFillRiskAssessment.WithinBudget;
                if (assessment == PostFillRiskAssessment.OverBudget)
                {
                    reason = string.Format(CultureInfo.InvariantCulture,
                        "estimated downside risk {0:F2} exceeds the approved {1:F2} budget by more than the 5% tolerance",
                        estimatedLoss, state.ApprovedRiskAmount);
                }
                else if (assessment == PostFillRiskAssessment.Unknown)
                {
                    reason = "the current stop-loss risk estimate could not be verified";
                }
            }

            bool changed = state.RiskReviewRequired != requiresReview;
            state.RiskReviewRequired = requiresReview;

            if (requiresReview)
                _riskReviewRequiredPositionIds.Add(position.Id);
            else
                _riskReviewRequiredPositionIds.Remove(position.Id);

            if (changed && persistChanges)
                SaveState(position, state);

            if (requiresReview)
            {
                string notice = "RISK GUARD: " + (string.IsNullOrEmpty(reason)
                    ? "the approved risk budget requires review"
                    : reason) +
                    ". The existing broker stop is left unchanged; new entries are paused until the risk estimate returns within budget or the position closes.";
                if (changed)
                    WarnThrottled(position, notice);

                // Keep the safety status visible even after restart, when the review flag may
                // already be persisted and therefore unchanged in this process.
                SetVolumeNotice(notice, true);
                ReportEntryStatus("new entries paused: " + reason, true);
            }
            else if (changed)
            {
                SetVolumeNotice("Risk guard cleared: the broker stop is usable and estimated downside risk is within the approved budget.", false);
            }
        }
    }
}
