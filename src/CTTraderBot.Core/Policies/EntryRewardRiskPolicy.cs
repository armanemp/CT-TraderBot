using System;

namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Calculates conservative entry-screening reward/risk independently of the visual TP guide.
    /// The configured base reward multiple is used unless a nearer valid forward structural barrier
    /// limits the target. An aspirational TP-guide extension is never used as proof of entry room.
    /// </summary>
    public static class EntryRewardRiskPolicy
    {
        public static double Calculate(
            bool isBuy,
            double entryPrice,
            double stopDistance,
            double baseRewardRisk,
            double nearestForwardBarrier,
            double barrierBuffer)
        {
            if (!IsFinitePositive(entryPrice) ||
                !IsFinitePositive(stopDistance) ||
                !IsFinitePositive(baseRewardRisk) ||
                !IsFinite(barrierBuffer))
                return 0.0;

            double direction = isBuy ? 1.0 : -1.0;
            double target = entryPrice + direction * stopDistance * baseRewardRisk;

            bool barrierAhead = IsFinitePositive(nearestForwardBarrier) &&
                (isBuy ? nearestForwardBarrier > entryPrice : nearestForwardBarrier < entryPrice);

            if (barrierAhead)
            {
                double bufferedTarget = nearestForwardBarrier - direction * Math.Max(0.0, barrierBuffer);
                bool bufferedTargetInProfit = isBuy
                    ? bufferedTarget > entryPrice
                    : bufferedTarget < entryPrice;
                bool barrierLimitsTarget = isBuy
                    ? nearestForwardBarrier < target
                    : nearestForwardBarrier > target;

                if (barrierLimitsTarget)
                    target = bufferedTargetInProfit ? bufferedTarget : nearestForwardBarrier;
            }

            double rewardRisk = Math.Abs(target - entryPrice) / stopDistance;
            return IsFinitePositive(rewardRisk) ? rewardRisk : 0.0;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0.0 && IsFinite(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
