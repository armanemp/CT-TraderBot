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
            if (_sharedStorageHealthy && _positionStateStorageHealthy)
                return true;

            ReportEntryBlocked("position state or ownership storage is unhealthy; new entries are paused until storage recovery", true);
            return false;
        }
    }
}
