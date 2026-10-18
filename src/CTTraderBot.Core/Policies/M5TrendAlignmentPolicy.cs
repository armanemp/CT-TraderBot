namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Enforces the meaning of the required M5 alignment option.
    /// When enabled, a neutral or conflicting M5 bias blocks the signal.
    /// When disabled, the caller may apply its separate soft-context rules.
    /// </summary>
    public static class M5TrendAlignmentPolicy
    {
        public static bool Allows(TrendBias m5Bias, TrendBias signalBias, bool requireAlignment)
        {
            if (!requireAlignment)
                return true;

            return m5Bias != TrendBias.Neutral &&
                   signalBias != TrendBias.Neutral &&
                   m5Bias == signalBias;
        }
    }
}
