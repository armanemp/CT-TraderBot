namespace CTTraderBot.Core.Policies
{
    public enum PostFillRiskAssessment
    {
        WithinBudget,
        OverBudget,
        Unknown
    }

    /// <summary>
    /// Compares post-fill downside-risk estimates with the approved pre-trade loss budget.
    /// This policy is deliberately broker-independent; an unknown estimate never counts as safe.
    /// </summary>
    public static class PostFillRiskPolicy
    {
        public static PostFillRiskAssessment Assess(
            double estimatedLoss,
            double approvedLoss,
            double toleranceFraction)
        {
            if (!IsFinite(estimatedLoss) || estimatedLoss < 0.0 ||
                !IsFinite(approvedLoss) || approvedLoss <= 0.0 ||
                !IsFinite(toleranceFraction) || toleranceFraction < 0.0 || toleranceFraction > 1.0)
                return PostFillRiskAssessment.Unknown;

            double limit = approvedLoss * (1.0 + toleranceFraction);
            if (!IsFinite(limit))
                return PostFillRiskAssessment.Unknown;

            return estimatedLoss > limit
                ? PostFillRiskAssessment.OverBudget
                : PostFillRiskAssessment.WithinBudget;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
