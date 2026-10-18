using System;

namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Detects a fresh crossing of a trigger level using the previous and current quote.
    /// The previous quote must be positive so attaching the robot without a prior quote
    /// cannot turn an already-broken level into a new entry signal.
    /// </summary>
    public static class FreshPriceCrossPolicy
    {
        public static bool CrossedAbove(double previousPrice, double currentPrice, double triggerPrice)
        {
            return IsFinite(previousPrice) && IsFinite(currentPrice) && IsFinite(triggerPrice) &&
                   previousPrice > 0.0 && previousPrice <= triggerPrice && currentPrice > triggerPrice;
        }

        public static bool CrossedBelow(double previousPrice, double currentPrice, double triggerPrice)
        {
            return IsFinite(previousPrice) && IsFinite(currentPrice) && IsFinite(triggerPrice) &&
                   previousPrice > 0.0 && previousPrice >= triggerPrice && currentPrice < triggerPrice;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
