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
            // Copy first: an emergency ClosePosition call can remove a position while management is running.
            var snapshot = new List<Position>();
            foreach (var position in Positions)
                snapshot.Add(position);

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

            // If an intended initial SL was crossed before installation and the emergency close
            // failed, keep retrying the close rather than silently recalculating a wider stop.
            if (_pendingEmergencyCloseIds.Contains(position.Id))
            {
                TryCloseBreachedPosition(position);
                return;
            }

            // The SL attempt is deliberately immediate; the candle delay applies only to tightening
            // a stop into profit, not to the initial protection.
            if (!position.StopLoss.HasValue && !TrySetInitialStop(position))
                return;

            var state = GetOrCreateState(position);
            if (state == null || state.InitialRiskDistance <= 0)
                return;

            UpdateSuggestedTpGuide(position, state);

            if (!HasRequiredCompletedBarsAfterEntry(position))
                return;

            ManageBreakEvenAndTrailing(position, state);
        }

        private bool IsPositionStillOpen(long positionId)
        {
            foreach (var current in Positions)
            {
                if (current.Id == positionId)
                    return true;
            }
            return false;
        }
    }
}
