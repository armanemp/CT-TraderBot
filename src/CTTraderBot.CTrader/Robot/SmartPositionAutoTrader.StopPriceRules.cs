using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private bool IsStopOnRiskSide(Position position)
        {
            if (!position.StopLoss.HasValue)
                return false;

            return position.TradeType == TradeType.Buy
                ? position.StopLoss.Value < position.EntryPrice - Symbol.TickSize * 0.5
                : position.StopLoss.Value > position.EntryPrice + Symbol.TickSize * 0.5;
        }

        private double GetMinimumStopDistance(double referencePrice)
        {
            double minimum = Symbol.MinStopLossDistance;
            double priceDistance;

            if (minimum <= 0)
                priceDistance = Symbol.TickSize;
            else if (Symbol.MinDistanceType == SymbolMinDistanceType.Pips)
                priceDistance = minimum * Symbol.PipSize;
            else
                priceDistance = referencePrice * minimum / 100.0;

            // Add a small tick cushion so rounding/network timing is less likely to put a stop
            // exactly on the broker's rejection boundary.
            priceDistance += Math.Max(0, BrokerDistanceSafetyTicks) * Symbol.TickSize;
            return Math.Max(priceDistance, Symbol.TickSize);
        }

        private double NormalizeStopPrice(double price, TradeType tradeType, bool tightening)
        {
            double tickSize = Symbol.TickSize;
            if (tickSize <= 0)
                return Math.Round(price, Symbol.Digits);

            double units = price / tickSize;
            double roundedUnits;

            // Initial stops round away from market to avoid invalid distances. Later tightening
            // rounds toward profit, then revalidates broker distance before submission.
            if (tradeType == TradeType.Buy)
                roundedUnits = tightening ? Math.Ceiling(units - 1e-10) : Math.Floor(units + 1e-10);
            else
                roundedUnits = tightening ? Math.Floor(units + 1e-10) : Math.Ceiling(units - 1e-10);

            return Math.Round(roundedUnits * tickSize, Symbol.Digits);
        }

        private double MoreProtectiveStop(TradeType tradeType, double current, double candidate)
        {
            return tradeType == TradeType.Buy ? Math.Max(current, candidate) : Math.Min(current, candidate);
        }

    }
}
