using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private double GetEstimatedMarginSafe(TradeType tradeType, double volumeInUnits)
        {
            if (!IsFinitePositive(volumeInUnits))
                return double.NaN;

            try
            {
                double margin = Symbol.GetEstimatedMargin(tradeType, volumeInUnits);
                return IsFinitePositive(margin) ? margin : double.NaN;
            }
            catch (Exception ex)
            {
                Diag("Could not estimate required margin for {0} units: {1}", volumeInUnits, ex.Message);
                return double.NaN;
            }
        }

        private double GetMaximumVolumeWithinMargin(TradeType tradeType, double maximumUnits, double marginBudget)
        {
            if (!IsFinitePositive(maximumUnits) || !IsFinitePositive(marginBudget))
                return 0.0;

            maximumUnits = Symbol.NormalizeVolumeInUnits(Math.Min(maximumUnits, Symbol.VolumeInUnitsMax), RoundingMode.Down);
            double minimumUnits = Symbol.VolumeInUnitsMin;
            if (maximumUnits < minimumUnits)
                return 0.0;

            double marginAtMaximum = GetEstimatedMarginSafe(tradeType, maximumUnits);
            if (!IsFinitePositive(marginAtMaximum))
                return 0.0;
            if (marginAtMaximum <= marginBudget)
                return maximumUnits;

            double marginAtMinimum = GetEstimatedMarginSafe(tradeType, minimumUnits);
            if (!IsFinitePositive(marginAtMinimum) || marginAtMinimum > marginBudget)
                return 0.0;

            // Binary search is used instead of simple proportional scaling because brokers may
            // apply dynamic leverage tiers and volume steps to metal symbols.
            double low = minimumUnits;
            double high = maximumUnits;
            for (int i = 0; i < 48; i++)
            {
                double middle = Symbol.NormalizeVolumeInUnits((low + high) * 0.5, RoundingMode.Down);
                if (middle <= low || middle >= high)
                    break;

                double margin = GetEstimatedMarginSafe(tradeType, middle);
                if (!IsFinitePositive(margin))
                    return 0.0;

                if (margin <= marginBudget)
                    low = middle;
                else
                    high = middle;
            }

            return Symbol.NormalizeVolumeInUnits(low, RoundingMode.Down);
        }

        private double GetAdaptiveAllowedRiskPercent(double qualityScore)
        {
            double minimum = Math.Max(0.0, MinimumOpportunityRiskPercent);
            double maximum = Math.Max(minimum, MaximumOpportunityRiskPercent);
            double allowed;

            if (EnableAdaptiveRiskBudget)
            {
                // Convex sizing keeps middling setups closer to the minimum budget; only strong
                // composite scores approach the maximum. This score is a rule-based quality index,
                // not a statistically calibrated win probability.
                double quality = Clamp01(qualityScore);
                double riskCurve = Math.Pow(quality, 2.0);
                allowed = minimum + riskCurve * (maximum - minimum);
            }
            else
                allowed = maximum;

            // This is a disaster-control ceiling, not the normal sizing rule. The active budget
            // remains opportunity-dependent below the ceiling.
            if (AbsoluteRiskCeilingPercent > 0)
                allowed = Math.Min(allowed, AbsoluteRiskCeilingPercent);

            return Math.Max(0.0, allowed);
        }

        private double EstimateRoundTurnCommissionPips(Position position)
        {
            if (Symbol.PipValue <= 0 || position.VolumeInUnits <= 0)
                return 0;

            // cTrader Symbol.PipValue is expressed per unit; multiply by position volume (not lots).
            double pipValueForPosition = Symbol.PipValue * position.VolumeInUnits;
            if (pipValueForPosition <= 0 || double.IsNaN(pipValueForPosition) || double.IsInfinity(pipValueForPosition))
                return 0;

            // cTrader documents Position.Commissions as the one-way commission amount associated
            // with the position; double it to estimate a round turn, then add an execution cushion.
            return (2.0 * Math.Abs(position.Commissions)) / pipValueForPosition;
        }
    }
}
