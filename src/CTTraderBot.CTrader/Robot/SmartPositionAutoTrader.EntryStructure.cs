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
        private double GetLowerStructuralStopAnchor(TradeType tradeType, int lookback)
        {
            if (_lowerBars == null || _lowerBars.Count < lookback + 3)
                return 0.0;

            // Prefer the nearest confirmed local pivot behind price. The pivot must have a newer
            // and an older closed neighbour, preventing a forming candle from moving the stop anchor.
            int maxIndex = Math.Min(Math.Max(3, lookback + 1), _lowerBars.Count - 2);
            for (int i = 2; i <= maxIndex; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (tradeType == TradeType.Buy)
                {
                    bool swingLow = pivot.Low <= newer.Low && pivot.Low < older.Low;
                    if (swingLow && pivot.Low < Symbol.Ask)
                        return pivot.Low;
                }
                else
                {
                    bool swingHigh = pivot.High >= newer.High && pivot.High > older.High;
                    if (swingHigh && pivot.High > Symbol.Bid)
                        return pivot.High;
                }
            }

            // Fallback is the recent completed-bar extreme, never the current forming bar.
            double extreme = tradeType == TradeType.Buy ? double.MaxValue : double.MinValue;
            int fallbackLookback = Math.Min(Math.Max(2, lookback), _lowerBars.Count - 1);
            for (int i = 1; i <= fallbackLookback; i++)
            {
                if (tradeType == TradeType.Buy)
                    extreme = Math.Min(extreme, _lowerBars.Last(i).Low);
                else
                    extreme = Math.Max(extreme, _lowerBars.Last(i).High);
            }
            return tradeType == TradeType.Buy ? (extreme < double.MaxValue ? extreme : 0.0)
                                               : (extreme > double.MinValue ? extreme : 0.0);
        }

        private double GetPriorLowerSwingLevel(bool wantHigh, int lookback, bool excludeMostRecentClosedBar = false)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return 0.0;

            // For a closed-candle signal, exclude Last(1) from its own reference range to avoid look-ahead.
            int firstIndex = excludeMostRecentClosedBar ? 3 : 2;
            int maxIndex = Math.Min(Math.Max(firstIndex + 1, lookback + (excludeMostRecentClosedBar ? 2 : 1)), _lowerBars.Count - 2);
            for (int i = firstIndex; i <= maxIndex; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (wantHigh)
                {
                    if (pivot.High >= newer.High && pivot.High > older.High)
                        return pivot.High;
                }
                else
                {
                    if (pivot.Low <= newer.Low && pivot.Low < older.Low)
                        return pivot.Low;
                }
            }

            // If no confirmed pivot exists, fall back to a local range boundary. In closed-bar tests,
            // start at Last(2) so the current signal candle can never define the level it supposedly broke.
            int fallbackStart = excludeMostRecentClosedBar ? 2 : 1;
            int fallbackCount = Math.Min(Math.Max(3, lookback), _lowerBars.Count - fallbackStart);
            double boundary = wantHigh ? double.MinValue : double.MaxValue;
            for (int offset = 0; offset < fallbackCount; offset++)
            {
                int i = fallbackStart + offset;
                if (wantHigh)
                    boundary = Math.Max(boundary, _lowerBars.Last(i).High);
                else
                    boundary = Math.Min(boundary, _lowerBars.Last(i).Low);
            }
            if (wantHigh && boundary > double.MinValue) return boundary;
            if (!wantHigh && boundary < double.MaxValue) return boundary;
            return 0.0;
        }

        private double GetLowerAtrValue()
        {
            if (_lowerBars == null || _lowerBars.Count < AtrPeriod + 2)
                return 0.0;

            int count = Math.Max(2, Math.Min(AtrPeriod, _lowerBars.Count - 2));
            double totalTrueRange = 0.0;
            for (int i = 1; i <= count; i++)
            {
                var bar = _lowerBars.Last(i);
                double previousClose = _lowerBars.Last(i + 1).Close;
                double trueRange = Math.Max(bar.High - bar.Low,
                    Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
                totalTrueRange += trueRange;
            }
            return totalTrueRange / count;
        }

        private double GetNearestLowerStructuralBarrier(TradeType tradeType, double currentPrice)
        {
            if (_lowerBars == null || _lowerBars.Count < 5)
                return 0.0;
            int lookback = Math.Min(Math.Max(4, EntryStructureLookbackBars + 2), _lowerBars.Count - 2);
            double nearest = 0.0;
            double bestDistance = double.MaxValue;
            for (int i = 2; i <= lookback; i++)
            {
                var pivot = _lowerBars.Last(i);
                var newer = _lowerBars.Last(i - 1);
                var older = _lowerBars.Last(i + 1);
                if (tradeType == TradeType.Buy)
                {
                    bool swingHigh = pivot.High >= newer.High && pivot.High > older.High && pivot.High > currentPrice;
                    double distance = pivot.High - currentPrice;
                    if (swingHigh && distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = pivot.High;
                    }
                }
                else
                {
                    bool swingLow = pivot.Low <= newer.Low && pivot.Low < older.Low && pivot.Low < currentPrice;
                    double distance = currentPrice - pivot.Low;
                    if (swingLow && distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = pivot.Low;
                    }
                }
            }
            return nearest;
        }

        private double GetNearestForwardBarrier(TradeType tradeType, double currentPrice, double first, double second)
        {
            bool firstValid = first > 0 && (tradeType == TradeType.Buy ? first > currentPrice : first < currentPrice);
            bool secondValid = second > 0 && (tradeType == TradeType.Buy ? second > currentPrice : second < currentPrice);
            if (!firstValid) return secondValid ? second : 0.0;
            if (!secondValid) return first;
            return tradeType == TradeType.Buy ? Math.Min(first, second) : Math.Max(first, second);
        }

    }
}
