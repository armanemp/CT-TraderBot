using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class EntryRewardRiskPolicyTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void BuyWithoutBarrier_UsesConfiguredBaseR()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 0.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void SellWithoutBarrier_UsesConfiguredBaseR()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(false, 100.0, 2.0, 2.0, 0.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void BuyNearBarrier_CapsAtBufferedStructuralRoom()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 3.0, 104.0, 0.5),
                Is.EqualTo(1.75).Within(Tolerance));
        }

        [Test]
        public void SellNearBarrier_CapsAtBufferedStructuralRoom()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(false, 100.0, 2.0, 3.0, 96.0, 0.5),
                Is.EqualTo(1.75).Within(Tolerance));
        }

        [Test]
        public void BuyBarrierBeyondBaseTarget_DoesNotIncreaseRequestedBaseR()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 120.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void SellBarrierBeyondBaseTarget_DoesNotIncreaseRequestedBaseR()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(false, 100.0, 2.0, 2.0, 80.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void BarrierOnWrongSide_IsIgnoredForBuy()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 99.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void BarrierOnWrongSide_IsIgnoredForSell()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(false, 100.0, 2.0, 2.0, 101.0, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void BarrierCloserThanBuffer_UsesBarrierRatherThanNegativeRoom()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 100.25, 0.5),
                Is.EqualTo(0.125).Within(Tolerance));
        }

        [Test]
        public void InvalidStopDistance_FailsClosed()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 0.0, 2.0, 0.0, 0.5), Is.EqualTo(0.0));
        }

        [Test]
        public void NonFiniteBaseR_FailsClosed()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, double.PositiveInfinity, 0.0, 0.5), Is.EqualTo(0.0));
        }

        [Test]
        public void NonFiniteBarrierIsIgnored()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, double.PositiveInfinity, 0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }

        [Test]
        public void NonFiniteBuffer_FailsClosed()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 0.0, double.NaN), Is.EqualTo(0.0));
        }

        [Test]
        public void NegativeBufferIsTreatedAsZero()
        {
            Assert.That(EntryRewardRiskPolicy.Calculate(true, 100.0, 2.0, 2.0, 104.0, -0.5),
                Is.EqualTo(2.0).Within(Tolerance));
        }
    }
}
