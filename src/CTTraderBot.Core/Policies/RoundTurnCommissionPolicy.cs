namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Broker-independent arithmetic for including a round-turn commission estimate in downside risk.
    /// Monetary inputs must already be converted to the account deposit currency by the adapter.
    /// Invalid inputs return NaN so they cannot be mistaken for a zero-cost trade.
    /// </summary>
    public enum BrokerCommissionBasis
    {
        UsdPerMillionUsdVolume,
        UsdPerOneLot,
        PercentageOfTradingVolume,
        QuoteCurrencyPerOneLot
    }

    public static class RoundTurnCommissionPolicy
    {
        public static double EstimateOneWayCommission(
            double commissionRate,
            double lots,
            double tradedNotionalInUsd,
            double tradedNotionalInQuote,
            BrokerCommissionBasis basis)
        {
            if (!IsFiniteNonNegative(commissionRate) || !IsFinitePositive(lots))
                return double.NaN;

            if (commissionRate == 0.0)
                return 0.0;

            double amount;
            switch (basis)
            {
                case BrokerCommissionBasis.UsdPerMillionUsdVolume:
                    if (!IsFinitePositive(tradedNotionalInUsd))
                        return double.NaN;
                    amount = commissionRate * tradedNotionalInUsd / 1000000.0;
                    break;

                case BrokerCommissionBasis.UsdPerOneLot:
                    amount = commissionRate * lots;
                    break;

                case BrokerCommissionBasis.PercentageOfTradingVolume:
                    if (!IsFinitePositive(tradedNotionalInQuote))
                        return double.NaN;
                    amount = tradedNotionalInQuote * commissionRate / 100.0;
                    break;

                case BrokerCommissionBasis.QuoteCurrencyPerOneLot:
                    amount = commissionRate * lots;
                    break;

                default:
                    return double.NaN;
            }

            return IsFiniteNonNegative(amount) ? amount : double.NaN;
        }

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

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }
    }
}
