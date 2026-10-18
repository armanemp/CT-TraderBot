using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class M5TrendBiasPolicyTests
    {
        [Test]
        public void RisingFastEmaAboveSlowEmaWithPriceAboveSlow_IsBullish()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 101.5, 101.0), Is.EqualTo(TrendBias.Bullish));
        }

        [Test]
        public void BullishBiasAllowsFastEmaToRemainFlat()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 102.0, 100.0), Is.EqualTo(TrendBias.Bullish));
        }

        [Test]
        public void FallingFastEmaBelowSlowEmaWithPriceBelowSlow_IsBearish()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(98.0, 100.0, 98.5, 99.0), Is.EqualTo(TrendBias.Bearish));
        }

        [Test]
        public void BearishBiasAllowsFastEmaToRemainFlat()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(98.0, 100.0, 98.0, 100.0), Is.EqualTo(TrendBias.Bearish));
        }

        [Test]
        public void EqualFastAndSlowEma_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(100.0, 100.0, 101.0, 102.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void PriceBelowSlowEma_PreventsBullishBias()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 101.0, 99.9), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void PriceAboveSlowEma_PreventsBearishBias()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(98.0, 100.0, 99.0, 100.1), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void FallingFastEmaPreventsBullishBias()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 102.1, 101.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void RisingFastEmaPreventsBearishBias()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(98.0, 100.0, 97.9, 99.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void PriceAndEmaSlopeConflict_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 101.0, 99.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void NonFiniteFastEma_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(double.NaN, 100.0, 101.0, 102.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void NonFiniteSlowEma_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, double.PositiveInfinity, 101.0, 102.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void NonFinitePriorEma_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, double.NaN, 102.0), Is.EqualTo(TrendBias.Neutral));
        }

        [Test]
        public void NonFiniteClosedPrice_IsNeutral()
        {
            Assert.That(M5TrendBiasPolicy.Calculate(102.0, 100.0, 101.0, double.PositiveInfinity), Is.EqualTo(TrendBias.Neutral));
        }
    }
}
