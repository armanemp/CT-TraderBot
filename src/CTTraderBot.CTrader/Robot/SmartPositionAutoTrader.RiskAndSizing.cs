using System;
using System.Collections.Generic;
using System.Globalization;
using CTTraderBot.Core.Policies;
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
            return AdaptiveRiskBudgetPolicy.Calculate(
                qualityScore,
                EnableAdaptiveRiskBudget,
                MinimumOpportunityRiskPercent,
                MaximumOpportunityRiskPercent,
                AbsoluteRiskCeilingPercent);
        }

        private double EstimateRoundTurnCommissionPips(Position position)
        {
            // Missing/non-finite conversion inputs mean commission cannot be estimated; never
            // report zero commission merely because the conversion data is invalid.
            if (position == null ||
                !IsFinitePositive(Symbol.PipValue) ||
                !IsFinitePositive(position.VolumeInUnits))
                return double.NaN;

            // cTrader Symbol.PipValue is expressed per unit; multiply by position volume (not lots).
            double pipValueForPosition = Symbol.PipValue * position.VolumeInUnits;
            if (!IsFinitePositive(pipValueForPosition))
                return double.NaN;

            double commissionAmount = Math.Abs(position.Commissions);
            if (double.IsNaN(commissionAmount) || double.IsInfinity(commissionAmount))
                return double.NaN;

            // Commission and the configured slippage reserve are added separately by the caller.
            double commissionPips = (2.0 * commissionAmount) / pipValueForPosition;
            return !double.IsNaN(commissionPips) && !double.IsInfinity(commissionPips) && commissionPips >= 0.0
                ? commissionPips
                : double.NaN;
        }
    }
}
