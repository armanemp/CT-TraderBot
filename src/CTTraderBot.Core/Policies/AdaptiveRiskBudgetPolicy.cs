using System;

namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Computes the configured percentage-of-equity risk budget from a rule-based
    /// setup-quality score. The score is not a win-probability estimate.
    /// </summary>
    public static class AdaptiveRiskBudgetPolicy
    {
        public static double Calculate(
            double qualityScore,
            bool adaptiveEnabled,
            double minimumRiskPercent,
            double maximumRiskPercent,
            double absoluteRiskCeilingPercent)
        {
            double minimum = Math.Max(0.0, minimumRiskPercent);
            double maximum = Math.Max(minimum, maximumRiskPercent);
            double allowed;

            if (adaptiveEnabled)
            {
                double quality = Clamp01(qualityScore);
                double riskCurve = Math.Pow(quality, 2.0);
                allowed = minimum + riskCurve * (maximum - minimum);
            }
            else
            {
                allowed = maximum;
            }

            // A positive ceiling is a hard cap. Zero or a negative value disables it,
            // matching the existing cBot parameter contract.
            if (absoluteRiskCeilingPercent > 0.0)
                allowed = Math.Min(allowed, absoluteRiskCeilingPercent);

            return Math.Max(0.0, allowed);
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
