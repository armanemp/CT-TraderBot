namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Broker-independent arithmetic for including a round-turn commission estimate in downside risk.
    /// Monetary inputs must already be converted to the account deposit currency by the adapter.
    /// Invalid inputs return NaN so they cannot be mistaken for a zero-cost trade.
    /// </summary>
    public static class RoundTurnCommissionPolicy
    {
        public static double CalculateRoundTurnCash(
            double oneWayCommissionAmount,
            double minimumOneWayCommissionAmount)
        {
            if (!IsFiniteNonNegative(oneWayCommissionAmount) ||
                !IsFiniteNonNegative(minimumOneWayCommissionAmount))
                return double.NaN;

            double roundTurn = 2.0 * System.Math.Max(oneWayCommissionAmount, minimumOneWayCommissionAmount);
            return IsFiniteNonNegative(roundTurn) ? roundTurn : double.NaN;
        }

        public static double AddRoundTurnCommissionToLoss(
            double stopAndSlippageLossAmount,
            double roundTurnCommissionAmount)
        {
            if (!IsFiniteNonNegative(stopAndSlippageLossAmount) ||
                !IsFiniteNonNegative(roundTurnCommissionAmount))
                return double.NaN;

            double total = stopAndSlippageLossAmount + roundTurnCommissionAmount;
            return IsFiniteNonNegative(total) ? total : double.NaN;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }
    }
}
