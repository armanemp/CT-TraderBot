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

        private double GetMaximumVolumeWithinRiskBudget(
            TradeType tradeType,
            double maximumUnits,
            double maximumAllowedLoss,
            double stopAndSlippagePips,
            double entryPrice)
        {
            if (!IsFinitePositive(maximumUnits) || !IsFinitePositive(maximumAllowedLoss) ||
                !IsFinitePositive(stopAndSlippagePips) || !IsFinitePositive(entryPrice))
                return 0.0;

            maximumUnits = Symbol.NormalizeVolumeInUnits(
                Math.Min(maximumUnits, Symbol.VolumeInUnitsMax), RoundingMode.Down);
            double minimumUnits = Symbol.VolumeInUnitsMin;
            if (maximumUnits < minimumUnits)
                return 0.0;

            double minimumRisk = EstimateTotalEntryLoss(minimumUnits, stopAndSlippagePips, entryPrice);
            if (!IsFinitePositive(minimumRisk) || minimumRisk > maximumAllowedLoss)
                return 0.0;

            double maximumRisk = EstimateTotalEntryLoss(maximumUnits, stopAndSlippagePips, entryPrice);
            if (IsFinitePositive(maximumRisk) && maximumRisk <= maximumAllowedLoss)
                return maximumUnits;

            // Solve for the largest broker-valid volume whose stop + slippage + round-turn
            // commission estimate fits the approved loss budget. Binary search handles volume
            // steps and the broker's minimum-commission floor without assuming linear costs.
            double low = minimumUnits;
            double high = maximumUnits;
            for (int i = 0; i < 48; i++)
            {
                double middle = Symbol.NormalizeVolumeInUnits((low + high) * 0.5, RoundingMode.Down);
                if (middle <= low || middle >= high)
                    break;

                double estimatedLoss = EstimateTotalEntryLoss(middle, stopAndSlippagePips, entryPrice);
                if (!IsFinitePositive(estimatedLoss))
                    return 0.0;

                if (estimatedLoss <= maximumAllowedLoss)
                    low = middle;
                else
                    high = middle;
            }

            return Symbol.NormalizeVolumeInUnits(low, RoundingMode.Down);
        }

        private double EstimateTotalEntryLoss(
            double volumeInUnits,
            double stopAndSlippagePips,
            double entryPrice)
        {
            if (!IsFinitePositive(volumeInUnits) || !IsFinitePositive(stopAndSlippagePips) ||
                !IsFinitePositive(entryPrice))
                return double.NaN;

            double stopAndSlippageLoss = Symbol.AmountRisked(volumeInUnits, stopAndSlippagePips);
            if (!IsFinitePositive(stopAndSlippageLoss))
                return double.NaN;

            double roundTurnCommission = EstimateRoundTurnCommissionCash(volumeInUnits, entryPrice);
            return RoundTurnCommissionPolicy.AddRoundTurnCommissionToLoss(
                stopAndSlippageLoss, roundTurnCommission);
        }

        private double EstimateRoundTurnCommissionCash(double volumeInUnits, double entryPrice)
        {
            if (!IsFinitePositive(volumeInUnits) || !IsFinitePositive(entryPrice) ||
                Symbol == null || Symbol.QuoteAsset == null || Account == null || Account.Asset == null ||
                !IsFiniteNonNegative(Symbol.Commission) || !IsFiniteNonNegative(Symbol.MinCommission))
                return double.NaN;

            double lots = Symbol.VolumeInUnitsToQuantity(volumeInUnits);
            double tradedNotionalInQuote = volumeInUnits * entryPrice;
            if (!IsFinitePositive(lots) || !IsFinitePositive(tradedNotionalInQuote))
                return double.NaN;

            double oneWayCommission = 0.0;
            string commissionAsset = Account.Asset.Name;
            if (Symbol.Commission > 0.0)
            {
                BrokerCommissionBasis basis;
                double tradedNotionalInUsd = 0.0;
                switch (Symbol.CommissionType)
                {
                    case SymbolCommissionType.UsdPerMillionUsdVolume:
                        basis = BrokerCommissionBasis.UsdPerMillionUsdVolume;
                        tradedNotionalInUsd = ConvertAssetAmount(
                            tradedNotionalInQuote, Symbol.QuoteAsset.Name, "USD");
                        if (!IsFinitePositive(tradedNotionalInUsd))
                            return double.NaN;
                        commissionAsset = "USD";
                        break;

                    case SymbolCommissionType.UsdPerOneLot:
                        basis = BrokerCommissionBasis.UsdPerOneLot;
                        commissionAsset = "USD";
                        break;

                    case SymbolCommissionType.PercentageOfTradingVolume:
                        basis = BrokerCommissionBasis.PercentageOfTradingVolume;
                        commissionAsset = Symbol.QuoteAsset.Name;
                        break;

                    case SymbolCommissionType.QuoteCurrencyPerOneLot:
                        basis = BrokerCommissionBasis.QuoteCurrencyPerOneLot;
                        commissionAsset = Symbol.QuoteAsset.Name;
                        break;

                    default:
                        return double.NaN;
                }

                oneWayCommission = RoundTurnCommissionPolicy.EstimateOneWayCommission(
                    Symbol.Commission, lots, tradedNotionalInUsd, tradedNotionalInQuote, basis);
            }

            if (!IsFiniteNonNegative(oneWayCommission))
                return double.NaN;

            double commissionInAccountAsset = ConvertAssetAmount(
                oneWayCommission, commissionAsset, Account.Asset.Name);
            if (!IsFiniteNonNegative(commissionInAccountAsset))
                return double.NaN;

            double minimumOneWayInAccountAsset = 0.0;
            if (Symbol.MinCommission > 0.0)
            {
                string minimumCommissionAsset;
                switch (Symbol.MinCommissionType)
                {
                    case SymbolMinCommissionType.Asset:
                        if (Symbol.MinCommissionAsset == null)
                            return double.NaN;
                        minimumCommissionAsset = Symbol.MinCommissionAsset.Name;
                        break;

                    case SymbolMinCommissionType.QuoteAsset:
                        minimumCommissionAsset = Symbol.QuoteAsset.Name;
                        break;

                    default:
                        return double.NaN;
                }

                minimumOneWayInAccountAsset = ConvertAssetAmount(
                    Symbol.MinCommission, minimumCommissionAsset, Account.Asset.Name);
                if (!IsFiniteNonNegative(minimumOneWayInAccountAsset))
                    return double.NaN;
            }

            return RoundTurnCommissionPolicy.CalculateRoundTurnCash(
                commissionInAccountAsset, minimumOneWayInAccountAsset);
        }

        private double ConvertAssetAmount(double amount, string sourceAssetName, string targetAssetName)
        {
            if (!IsFiniteNonNegative(amount) ||
                string.IsNullOrWhiteSpace(sourceAssetName) ||
                string.IsNullOrWhiteSpace(targetAssetName))
                return double.NaN;

            if (amount == 0.0 ||
                string.Equals(sourceAssetName, targetAssetName, StringComparison.OrdinalIgnoreCase))
                return amount;

            try
            {
                double converted = AssetConverter.Convert(amount, sourceAssetName, targetAssetName);
                return IsFiniteNonNegative(converted) ? converted : double.NaN;
            }
            catch (Exception ex)
            {
                Diag("Could not convert estimated commission from {0} to {1}: {2}",
                    sourceAssetName, targetAssetName, ex.Message);
                return double.NaN;
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
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
