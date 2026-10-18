using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private void Diag(string format, params object[] args)
        {
            if (EnableDiagnosticLogging)
                Print(format, args);
        }

        private double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private double GetAtrValue()
        {
            // Use the last completed bar's ATR rather than the forming bar to prevent a temporary
            // intrabar volatility spike from constantly changing the risk basis.
            if (_atr == null || Bars == null || Bars.Count < AtrPeriod + 2)
                return 0;

            double value = _atr.Result.Last(1);
            return IsFinitePositive(value) ? value : 0;
        }

        private bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void WarnThrottled(Position position, string message)
        {
            DateTime now = Server.Time;
            DateTime last;
            if (!_lastWarningTime.TryGetValue(position.Id, out last) || (now - last).TotalSeconds >= 10)
            {
                Diag("[Position {0}] {1}", position.Id, message);
                _lastWarningTime[position.Id] = now;
            }
        }
    }
}
