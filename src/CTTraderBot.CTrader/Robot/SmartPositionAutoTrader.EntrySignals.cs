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
        private TradeType? GetM5TrendBias()
        {
            if (Bars == null || Bars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _entryFastEma == null || _entrySlowEma == null)
                return null;

            double fast = _entryFastEma.Result.Last(1);
            double slow = _entrySlowEma.Result.Last(1);
            double priorFast = _entryFastEma.Result.Last(2);
            double close = Bars.Last(1).Close;

            TrendBias bias = M5TrendBiasPolicy.Calculate(fast, slow, priorFast, close);
            if (bias == TrendBias.Bullish)
                return TradeType.Buy;
            if (bias == TrendBias.Bearish)
                return TradeType.Sell;
            return null;
        }

        private TradeType? GetLowerTimeframeSignal()
        {
            if (_lowerBars == null || _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10) ||
                _lowerFastEma == null || _lowerSlowEma == null)
                return null;

            var c = _lowerBars.Last(1);
            var p = _lowerBars.Last(2);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double body = Math.Max(Symbol.TickSize, Math.Abs(c.Close - c.Open));
            double lowerWick = Math.Min(c.Open, c.Close) - c.Low;
            double upperWick = c.High - Math.Max(c.Open, c.Close);
            double closeLocation = (c.Close - c.Low) / range;
            double bodyRatio = body / range;
            double lowerAtr = GetLowerAtrValue();
            if (lowerAtr <= 0)
                return null;

            bool bullishRejection = c.Close > c.Open && lowerWick >= body * 0.75 &&
                                    closeLocation >= 0.58 && c.Close >= p.Close;
            bool bearishRejection = c.Close < c.Open && upperWick >= body * 0.75 &&
                                    closeLocation <= 0.42 && c.Close <= p.Close;
            bool bullishEngulf = c.Close > c.Open && p.Close < p.Open && c.Close >= p.Open && c.Open <= p.Close;
            bool bearishEngulf = c.Close < c.Open && p.Close > p.Open && c.Close <= p.Open && c.Open >= p.Close;
            bool bullishMomentum = c.Close > c.Open && c.Close > p.High && bodyRatio >= 0.30 && closeLocation >= 0.62;
            bool bearishMomentum = c.Close < c.Open && c.Close < p.Low && bodyRatio >= 0.30 && closeLocation <= 0.38;

            double fast = _lowerFastEma.Result.Last(1);
            double slow = _lowerSlowEma.Result.Last(1);
            bool bullishEma = fast >= slow && c.Close >= fast;
            bool bearishEma = fast <= slow && c.Close <= fast;

            double previousSwingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars, true);
            double previousSwingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars, true);
            double breakBuffer = Math.Max(Symbol.TickSize, lowerAtr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            bool bullishStructureBreak = previousSwingHigh > 0 && c.Close > previousSwingHigh + breakBuffer &&
                                         c.Close > c.Open && closeLocation >= 0.60 && bodyRatio >= 0.25;
            bool bearishStructureBreak = previousSwingLow > 0 && c.Close < previousSwingLow - breakBuffer &&
                                         c.Close < c.Open && closeLocation <= 0.40 && bodyRatio >= 0.25;

            // Pullback/reclaim is an additional early trigger; tolerance scales with lower-TF volatility.
            bool bullishPullbackReclaim = c.Close > c.Open && c.Close >= fast &&
                c.Low <= fast + lowerAtr * 0.20 && c.Close >= p.Close;
            bool bearishPullbackReclaim = c.Close < c.Open && c.Close <= fast &&
                c.High >= fast - lowerAtr * 0.20 && c.Close <= p.Close;

            bool buySetup = bullishRejection || bullishEngulf || bullishMomentum || bullishStructureBreak || bullishPullbackReclaim;
            bool sellSetup = bearishRejection || bearishEngulf || bearishMomentum || bearishStructureBreak || bearishPullbackReclaim;

            if (RequireLowerEmaAlignment)
            {
                // Structure breaks are exempted from the EMA alignment gate: a confirmed BOS can lead the EMA.
                buySetup = bullishStructureBreak || (buySetup && bullishEma);
                sellSetup = bearishStructureBreak || (sellSetup && bearishEma);
            }

            if (buySetup == sellSetup)
                return null;
            return buySetup ? TradeType.Buy : TradeType.Sell;
        }

        private TradeType? GetLowerMicroSwingBreakoutSignal()
        {
            if (_lowerBars == null || _lowerBars.Count < Math.Max(EntrySlowEmaPeriod + 3, 10))
                return null;

            double lowerAtr = GetLowerAtrValue();
            if (lowerAtr <= 0)
                return null;

            double triggerBuffer = Math.Max(Symbol.TickSize, lowerAtr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            double swingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars);
            double swingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars);
            if (swingHigh <= 0 || swingLow <= 0)
                return null;

            // Detect a FRESH price crossing, not simply a price that was already beyond an old swing
            // when the cBot started. This prevents stale-level entry on the first tick after attach.
            double buyTrigger = swingHigh + triggerBuffer;
            double sellTrigger = swingLow - triggerBuffer;
            bool buyBreak = FreshPriceCrossPolicy.CrossedAbove(_lastTickAsk, Symbol.Ask, buyTrigger);
            bool sellBreak = FreshPriceCrossPolicy.CrossedBelow(_lastTickBid, Symbol.Bid, sellTrigger);
            if (buyBreak == sellBreak)
                return null;
            return buyBreak ? TradeType.Buy : TradeType.Sell;
        }

        private bool IsConfirmedLowerStructureBreak(TradeType tradeType)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return false;
            var c = _lowerBars.Last(1);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double bodyRatio = Math.Abs(c.Close - c.Open) / range;
            double closeLocation = (c.Close - c.Low) / range;
            double swingHigh = GetPriorLowerSwingLevel(true, EntryStructureLookbackBars, true);
            double swingLow = GetPriorLowerSwingLevel(false, EntryStructureLookbackBars, true);
            double atr = GetLowerAtrValue();
            double buffer = Math.Max(Symbol.TickSize, atr * Math.Max(0.0, MicroSwingBreakoutBufferAtr));
            if (tradeType == TradeType.Buy)
                return c.Close > c.Open && swingHigh > 0 && c.Close > swingHigh + buffer && bodyRatio >= 0.30 && closeLocation >= 0.65;
            return c.Close < c.Open && swingLow > 0 && c.Close < swingLow - buffer && bodyRatio >= 0.30 && closeLocation <= 0.35;
        }

    }
}
