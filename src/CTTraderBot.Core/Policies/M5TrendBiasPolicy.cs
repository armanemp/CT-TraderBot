namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Describes the direction inferred from the M5 fast/slow EMA relationship,
    /// the latest closed price and the fast EMA's previous closed value.
    /// </summary>
    public enum TrendBias
    {
        Neutral = 0,
        Bullish = 1,
        Bearish = 2
    }

    /// <summary>
    /// Pure implementation of the existing M5 trend-bias predicate.
    /// </summary>
    public static class M5TrendBiasPolicy
    {
        public static TrendBias Calculate(
            double fastEma,
            double slowEma,
            double priorFastEma,
            double lastClosedPrice)
        {
            bool bullish = fastEma > slowEma &&
                           lastClosedPrice >= slowEma &&
                           fastEma >= priorFastEma;

            bool bearish = fastEma < slowEma &&
                           lastClosedPrice <= slowEma &&
                           fastEma <= priorFastEma;

            if (bullish == bearish)
                return TrendBias.Neutral;

            return bullish ? TrendBias.Bullish : TrendBias.Bearish;
        }
    }
}
