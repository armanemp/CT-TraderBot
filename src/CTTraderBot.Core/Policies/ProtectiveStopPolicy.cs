using System;

namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Pure numerical rules for protective-stop geometry. Broker submission remains in the cTrader adapter.
    /// </summary>
    public static class ProtectiveStopPolicy
    {
        public static bool IsOnLossSide(bool isBuy, double stopPrice, double entryPrice, double tickSize)
        {
            if (!IsFinite(stopPrice) || !IsFinite(entryPrice) || !IsFinite(tickSize) || tickSize < 0.0)
                return false;

            double tolerance = tickSize * 0.5;
            return isBuy
                ? stopPrice < entryPrice - tolerance
                : stopPrice > entryPrice + tolerance;
        }

        public static double GetMinimumStopDistance(
            double minimumDistance,
            bool minimumDistanceIsPips,
            double referencePrice,
            double pipSize,
            double tickSize,
            double safetyTicks)
        {
            if (!IsFinite(minimumDistance) || !IsFinite(referencePrice) ||
                !IsFinite(tickSize) || tickSize <= 0.0 || !IsFinite(safetyTicks))
                return double.NaN;

            double distance;
            if (minimumDistance <= 0.0)
            {
                distance = tickSize;
            }
            else if (minimumDistanceIsPips)
            {
                if (!IsFinite(pipSize) || pipSize <= 0.0)
                    return double.NaN;

                distance = minimumDistance * pipSize;
            }
            else
            {
                distance = referencePrice * minimumDistance / 100.0;
            }

            distance += Math.Max(0.0, safetyTicks) * tickSize;
            return IsFinite(distance) ? Math.Max(distance, tickSize) : double.NaN;
        }

        public static double NormalizePrice(
            double price,
            double tickSize,
            int digits,
            bool isBuy,
            bool tightening)
        {
            if (!IsFinite(price) || !IsFinite(tickSize) || digits < 0 || digits > 15)
                return double.NaN;

            if (tickSize <= 0.0)
                return Math.Round(price, digits);

            double units = price / tickSize;
            if (!IsFinite(units))
                return double.NaN;

            double roundedUnits;
            // Initial stops round away from market; tightening rounds toward profit.
            if (isBuy)
                roundedUnits = tightening ? Math.Ceiling(units - 1e-10) : Math.Floor(units + 1e-10);
            else
                roundedUnits = tightening ? Math.Floor(units + 1e-10) : Math.Ceiling(units - 1e-10);

            double normalized = Math.Round(roundedUnits * tickSize, digits);
            return IsFinite(normalized) ? normalized : double.NaN;
        }

        public static double MoreProtectiveStop(bool isBuy, double currentStop, double candidateStop)
        {
            if (!IsFinite(currentStop))
                return double.NaN;

            if (!IsFinite(candidateStop))
                return currentStop;

            return isBuy ? Math.Max(currentStop, candidateStop) : Math.Min(currentStop, candidateStop);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
