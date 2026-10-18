using System;
using cAlgo.API;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private string _lastEntryBlockReason = string.Empty;
        private DateTime _lastEntryStatusUpdateTime = DateTime.MinValue;

        private void ReportEntryBlocked(string reason, bool isWarning = false)
        {
            if (_entryStatusText == null || string.IsNullOrWhiteSpace(reason))
                return;

            DateTime now = Server.Time;
            bool sameReason = string.Equals(reason, _lastEntryBlockReason, StringComparison.Ordinal);
            double elapsedSeconds = _lastEntryStatusUpdateTime == DateTime.MinValue
                ? double.MaxValue
                : (now - _lastEntryStatusUpdateTime).TotalSeconds;

            if ((sameReason && elapsedSeconds < 15.0) ||
                (!sameReason && elapsedSeconds < 0.5))
                return;

            _lastEntryBlockReason = reason;
            _lastEntryStatusUpdateTime = now;
            _entryStatusText.Text = "Entry blocked: " + reason;
            _entryStatusText.ForegroundColor = isWarning
                ? Color.FromHex("#FFB4A8")
                : Color.FromHex("#CAD2DE");

            if (EnableDiagnosticLogging)
                Print("Entry blocked: {0}", reason);
        }

        private void ReportEntryStatus(string message, bool isWarning = false)
        {
            if (_entryStatusText == null)
                return;

            _entryStatusText.Text = "Entry: " + (message ?? string.Empty);
            _entryStatusText.ForegroundColor = isWarning
                ? Color.FromHex("#FFB4A8")
                : Color.FromHex("#B8E0CC");
            _lastEntryBlockReason = string.Empty;
            _lastEntryStatusUpdateTime = DateTime.MinValue;
        }

        private bool CanStartNewEntry()
        {
            if (!_sharedStorageHealthy || !_positionStateStorageHealthy)
            {
                ReportEntryBlocked("position state or ownership storage is unhealthy; new entries are paused until storage recovery", true);
                return false;
            }

            // Risk state is shared across instances. A non-owner instance must refresh its
            // cached state from device storage before deciding whether it may open another order.
            if (!RefreshOpenPositionRiskGuards())
            {
                ReportEntryBlocked("a managed position has unconfirmed protection, unreadable risk state, or estimated downside risk above its approved budget; new entries are paused", true);
                return false;
            }

            if (_riskReviewRequiredPositionIds.Count > 0)
            {
                ReportEntryBlocked("a managed position has unconfirmed protection or estimated downside risk above its approved budget; new entries are paused until risk returns within budget or the position is closed", true);
                return false;
            }

            return true;
        }

        private bool RefreshOpenPositionRiskGuards()
        {
            var snapshot = Positions.FindAll(AutoTradeLabel, SymbolName);
            Position refreshTarget = null;

            foreach (var position in snapshot)
            {
                if (_ignoredExistingPositionIds.Contains(position.Id) ||
                    _ownedPositionIds.Contains(position.Id))
                    continue;

                refreshTarget = position;
                break;
            }

            // Reload is throttled by the existing device-storage coordinator. Never continue
            // with a stale risk record if that refresh fails.
            if (refreshTarget != null && !ReloadSharedStorageIfNeeded(refreshTarget))
                return false;

            foreach (var position in snapshot)
            {
                if (_ignoredExistingPositionIds.Contains(position.Id))
                    continue;

                // This instance doesn't own the manager lease, so don't trust an older in-memory
                // copy. Re-read the persisted approved budget/review flag instead.
                if (!_ownedPositionIds.Contains(position.Id))
                    _states.Remove(position.Id);

                PositionState state = GetOrCreateState(position);
                if (state == null)
                    return false;

                RefreshPostFillRiskReview(position, state);
                if (!_sharedStorageHealthy || !_positionStateStorageHealthy)
                    return false;

                if (state.RiskReviewRequired || state.RiskBudgetUnavailable || !HasUsableStopLoss(position))
                {
                    _riskReviewRequiredPositionIds.Add(position.Id);
                    return false;
                }
            }

            return true;
        }
    }
}
