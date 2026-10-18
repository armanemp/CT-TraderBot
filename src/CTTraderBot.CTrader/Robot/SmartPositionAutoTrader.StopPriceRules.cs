using CTTraderBot.Core.Policies;
using cAlgo.API;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private bool IsStopOnRiskSide(Position position)
        {
            if (!position.StopLoss.HasValue)
                return false;

            return ProtectiveStopPolicy.IsOnLossSide(
                position.TradeType == TradeType.Buy,
                position.StopLoss.Value,
                position.EntryPrice,
                Symbol.TickSize);
        }

        private double GetMinimumStopDistance(double referencePrice)
        {
            return ProtectiveStopPolicy.GetMinimumStopDistance(
                Symbol.MinStopLossDistance,
                Symbol.MinDistanceType == SymbolMinDistanceType.Pips,
                referencePrice,
                Symbol.PipSize,
                Symbol.TickSize,
                BrokerDistanceSafetyTicks);
        }

        private double NormalizeStopPrice(double price, TradeType tradeType, bool tightening)
        {
            return ProtectiveStopPolicy.NormalizePrice(
                price, Symbol.TickSize, Symbol.Digits,
                tradeType == TradeType.Buy, tightening);
        }

        private double MoreProtectiveStop(TradeType tradeType, double current, double candidate)
        {
            return ProtectiveStopPolicy.MoreProtectiveStop(
                tradeType == TradeType.Buy, current, candidate);
        }

    }
}
