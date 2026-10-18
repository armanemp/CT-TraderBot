using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private void ManageCurrentSymbolPositions()
        {
            // Query only this bot's label on this symbol instead of scanning the account's
            // entire position collection on every market tick. FindAll returns a stable array.
            var snapshot = Positions.FindAll(AutoTradeLabel, SymbolName);

            foreach (var position in snapshot)
            {
                if (IsPositionStillOpen(position.Id) && ShouldManage(position))
                    ManagePosition(position);
            }
        }

        private bool IsThisBotPosition(Position position)
        {
            return position != null &&
                   string.Equals(position.SymbolName, SymbolName, StringComparison.Ordinal) &&
                   string.Equals(position.Label, AutoTradeLabel, StringComparison.Ordinal);
        }

        private bool ShouldManage(Position position)
        {
            // Exact label + exact symbol: never manage manual positions or different cBots.
            return IsThisBotPosition(position) && !_ignoredExistingPositionIds.Contains(position.Id);
        }

        private void ManagePosition(Position position)
        {
            if (position == null || !TryAcquireManagementLease(position))
                return;

            // If an intended SL was crossed before installation and the emergency close failed,
            // keep retrying the close rather than calculating a wider replacement stop.
            if (_pendingEmergencyCloseIds.Contains(position.Id))
            {
                // A price-crossing emergency close is an unbounded-risk review state until the
                // position is actually gone. Keep the entry gate closed on every retry tick.
                _riskReviewRequiredPositionIds.Add(position.Id);
                SetVolumeNotice("RISK GUARD: emergency close is pending because the intended stop level was crossed. New entries remain paused until closure is confirmed.", true);
                TryCloseBreachedPosition(position);
                return;
            }

            var state = GetOrCreateState(position);
            if (state == null || state.InitialRiskDistance <= 0)
                return;

            // Load and evaluate the persisted guard before trying to recover protection. Missing
            // protection pauses entries immediately; if it is restored successfully, assess again.
            RefreshPostFillRiskReview(position, state);
            if (!position.StopLoss.HasValue)
            {
                if (!TrySetInitialStop(position))
                {
                    RefreshPostFillRiskReview(position, state);
                    return;
                }
                RefreshPostFillRiskReview(position, state);
            }

            // Do not trail/update guides from an SL that is absent, already crossed, or otherwise
            // unusable at the live quote. The risk guard stays active until protection is verified.
            if (!HasUsableStopLoss(position))
            {
                RefreshPostFillRiskReview(position, state);
                return;
            }

            UpdateSuggestedTpGuide(position, state);

            if (!HasRequiredCompletedBarsAfterEntry(position))
                return;

            ManageBreakEvenAndTrailing(position, state);
        }

        private bool IsPositionStillOpen(long positionId)
        {
            // This pinned cTrader API exposes FindById(int), while the bot persists IDs as long.
            // Keep the comparison type-safe and scan only this bot's usually small filtered set.
            foreach (var current in Positions.FindAll(AutoTradeLabel, SymbolName))
            {
                if (current.Id == positionId)
                    return true;
            }

            return false;
        }
    }
}
