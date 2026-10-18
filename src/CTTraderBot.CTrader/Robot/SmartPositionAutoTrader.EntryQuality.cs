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
        private double GetOpportunityQualityScore(TradeType tradeType, double atr, double targetR)
        {
            double trendScore = GetM5DirectionalStrengthScore(tradeType, atr);
            double lowerSignalScore = GetLowerSignalQualityScore(tradeType);
            double structuralRoomScore = targetR > 0 ? Clamp01(targetR / (targetR + 1.5)) : 0.0;
            bool confirmedStructureShift = IsConfirmedLowerStructureBreak(tradeType);
            // A confirmed lower-TF break is an independent reason to consider an early shift even
            // when M5's slower trend score still points the other way. This remains a heuristic,
            // not a measured probability of profit.
            return Clamp01(0.40 * trendScore + 0.36 * lowerSignalScore +
                           0.16 * structuralRoomScore + (confirmedStructureShift ? 0.08 : 0.0));
        }

        private double GetLowerSignalQualityScore(TradeType tradeType)
        {
            if (_lowerBars == null || _lowerBars.Count < 3 || _lowerFastEma == null || _lowerSlowEma == null)
                return 0.0;

            var c = _lowerBars.Last(1);
            var p = _lowerBars.Last(2);
            double range = Math.Max(Symbol.TickSize, c.High - c.Low);
            double body = Math.Abs(c.Close - c.Open);
            double bodyRatio = Clamp01(body / range);
            double closeLocation = tradeType == TradeType.Buy
                ? Clamp01((c.Close - c.Low) / range)
                : Clamp01((c.High - c.Close) / range);
            double wick = tradeType == TradeType.Buy
                ? Math.Max(0.0, Math.Min(c.Open, c.Close) - c.Low)
                : Math.Max(0.0, c.High - Math.Max(c.Open, c.Close));
            double wickRatio = Clamp01(wick / range);
            bool directionCandle = tradeType == TradeType.Buy ? c.Close > c.Open : c.Close < c.Open;
            bool emaAligned = tradeType == TradeType.Buy
                ? _lowerFastEma.Result.Last(1) >= _lowerSlowEma.Result.Last(1) && c.Close >= _lowerFastEma.Result.Last(1)
                : _lowerFastEma.Result.Last(1) <= _lowerSlowEma.Result.Last(1) && c.Close <= _lowerFastEma.Result.Last(1);
            bool engulf = tradeType == TradeType.Buy
                ? c.Close > c.Open && p.Close < p.Open && c.Close >= p.Open && c.Open <= p.Close
                : c.Close < c.Open && p.Close > p.Open && c.Close <= p.Open && c.Open >= p.Close;
            bool momentum = tradeType == TradeType.Buy
                ? c.Close > c.Open && c.Close > p.High && bodyRatio >= 0.30
                : c.Close < c.Open && c.Close < p.Low && bodyRatio >= 0.30;
            bool structuralBreak = IsConfirmedLowerStructureBreak(tradeType);
            double lowerAtr = GetLowerAtrValue();
            bool pullbackReclaim = lowerAtr > 0 && (tradeType == TradeType.Buy
                ? c.Close > c.Open && c.Close >= _lowerFastEma.Result.Last(1) && c.Low <= _lowerFastEma.Result.Last(1) + lowerAtr * 0.20
                : c.Close < c.Open && c.Close <= _lowerFastEma.Result.Last(1) && c.High >= _lowerFastEma.Result.Last(1) - lowerAtr * 0.20);
            double patternScore = structuralBreak ? 0.98 : momentum ? 0.94 : engulf ? 0.88 : pullbackReclaim ? 0.84 : (directionCandle && wickRatio >= 0.25 && closeLocation >= 0.58) ? 0.79 : 0.60;
            double bodyQuality = Clamp01(bodyRatio / 0.60);
            double emaScore = emaAligned ? 1.0 : 0.35;
            double followThrough = tradeType == TradeType.Buy
                ? (c.Close >= p.Close ? 1.0 : 0.25)
                : (c.Close <= p.Close ? 1.0 : 0.25);
            return Clamp01(0.34 * patternScore + 0.24 * closeLocation + 0.16 * bodyQuality + 0.14 * emaScore + 0.12 * followThrough);
        }

        private double GetM5DirectionalStrengthScore(TradeType tradeType, double atr)
        {
            if (Bars == null || Bars.Count < 7 || atr <= 0)
                return 0.35;

            double closeNow = Bars.Last(1).Close;
            double closeThen = Bars.Last(6).Close;
            double signedMove = tradeType == TradeType.Buy ? closeNow - closeThen : closeThen - closeNow;
            double displacementScore = Clamp01(signedMove / (atr * 2.0));

            double bodySum = 0.0;
            double rangeSum = 0.0;
            double alignedBodySum = 0.0;
            for (int i = 1; i <= 3; i++)
            {
                var candle = Bars.Last(i);
                double body = Math.Abs(candle.Close - candle.Open);
                double range = Math.Max(Symbol.TickSize, candle.High - candle.Low);
                bodySum += body;
                rangeSum += range;
                bool aligned = tradeType == TradeType.Buy ? candle.Close > candle.Open : candle.Close < candle.Open;
                if (aligned)
                    alignedBodySum += body;
            }

            double bodyRatio = rangeSum > 0 ? Clamp01(bodySum / rangeSum) : 0.0;
            double alignmentRatio = bodySum > 0 ? Clamp01(alignedBodySum / bodySum) : 0.0;
            return Clamp01(0.45 * displacementScore + 0.30 * bodyRatio + 0.25 * alignmentRatio);
        }

        private double GetEntryTargetR(TradeType tradeType, double atr, double entryReference, double stopDistance)
        {
            if (!IsFinitePositive(atr) || stopDistance <= Symbol.TickSize ||
                !IsFinitePositive(entryReference))
                return 0.0;

            // Entry screening must not reuse the adaptive chart-guide target (which can extend
            // to a large R multiple without a confirmed barrier). Screen using the configured
            // base R, capped only by the nearest valid forward structural barrier.
            double baseR = Math.Max(0.5, SuggestedTakeProfitR);
            double m5Barrier = GetNearestStructuralBarrier(tradeType, entryReference, atr);
            double lowerBarrier = GetNearestLowerStructuralBarrier(tradeType, entryReference);
            double barrier = GetNearestForwardBarrier(tradeType, entryReference, m5Barrier, lowerBarrier);
            double buffer = Math.Max(Symbol.TickSize, atr * Math.Max(0.0, SuggestedTpStructureBufferAtr));

            return EntryRewardRiskPolicy.Calculate(
                tradeType == TradeType.Buy,
                entryReference,
                stopDistance,
                baseR,
                barrier,
                buffer);
        }

    }
}
