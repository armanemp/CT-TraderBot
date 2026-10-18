using System;
using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class ProtectiveStopPolicyTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void BuyStopMustBeStrictlyBeyondHalfTickTolerance()
        {
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(true, 99.94, 100.0, 0.1), Is.True);
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(true, 99.95, 100.0, 0.1), Is.False);
        }

        [Test]
        public void SellStopMustBeStrictlyBeyondHalfTickTolerance()
        {
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(false, 100.06, 100.0, 0.1), Is.True);
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(false, 100.05, 100.0, 0.1), Is.False);
        }

        [Test]
        public void NonFiniteStopGeometry_IsRejected()
        {
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(true, double.NaN, 100.0, 0.01), Is.False);
            Assert.That(ProtectiveStopPolicy.IsOnLossSide(false, 101.0, double.PositiveInfinity, 0.01), Is.False);
        }

        [Test]
        public void BuyInitialStopRoundsDownToTick()
        {
            Assert.That(ProtectiveStopPolicy.NormalizePrice(100.07, 0.05, 2, true, false),
                Is.EqualTo(100.05).Within(Tolerance));
        }

        [Test]
        public void BuyTighteningRoundsUpToTick()
        {
            Assert.That(ProtectiveStopPolicy.NormalizePrice(100.07, 0.05, 2, true, true),
                Is.EqualTo(100.10).Within(Tolerance));
        }

        [Test]
        public void SellInitialStopRoundsUpToTick()
        {
            Assert.That(ProtectiveStopPolicy.NormalizePrice(100.07, 0.05, 2, false, false),
                Is.EqualTo(100.10).Within(Tolerance));
        }

        [Test]
        public void SellTighteningRoundsDownToTick()
        {
            Assert.That(ProtectiveStopPolicy.NormalizePrice(100.07, 0.05, 2, false, true),
                Is.EqualTo(100.05).Within(Tolerance));
        }

        [Test]
        public void MissingTickSizeFallsBackToDigits()
        {
            Assert.That(ProtectiveStopPolicy.NormalizePrice(100.076, 0.0, 2, true, false),
                Is.EqualTo(100.08).Within(Tolerance));
        }

        [Test]
        public void NonFinitePriceIsRejected()
        {
            Assert.That(double.IsNaN(ProtectiveStopPolicy.NormalizePrice(double.NaN, 0.01, 2, true, false)), Is.True);
        }

        [Test]
        public void InvalidDigitsAreRejected()
        {
            Assert.That(double.IsNaN(ProtectiveStopPolicy.NormalizePrice(100.0, 0.01, 16, true, false)), Is.True);
        }

        [Test]
        public void PipsDistanceAddsSafetyTicks()
        {
            Assert.That(ProtectiveStopPolicy.GetMinimumStopDistance(3.0, true, 200.0, 0.1, 0.01, 2.0),
                Is.EqualTo(0.32).Within(Tolerance));
        }

        [Test]
        public void PercentageDistanceAddsSafetyTicks()
        {
            Assert.That(ProtectiveStopPolicy.GetMinimumStopDistance(0.5, false, 200.0, 0.1, 0.01, 2.0),
                Is.EqualTo(1.02).Within(Tolerance));
        }

        [Test]
        public void DisabledBrokerDistanceStillHonorsTickAndSafetyBuffer()
        {
            Assert.That(ProtectiveStopPolicy.GetMinimumStopDistance(0.0, true, 200.0, 0.1, 0.01, 2.0),
                Is.EqualTo(0.03).Within(Tolerance));
        }

        [Test]
        public void NegativeSafetyTicksAreClampedToZero()
        {
            Assert.That(ProtectiveStopPolicy.GetMinimumStopDistance(0.0, true, 200.0, 0.1, 0.01, -3.0),
                Is.EqualTo(0.01).Within(Tolerance));
        }

        [Test]
        public void InvalidMinimumDistanceInputsFailClosed()
        {
            Assert.That(double.IsNaN(ProtectiveStopPolicy.GetMinimumStopDistance(
                double.NaN, true, 200.0, 0.1, 0.01, 1.0)), Is.True);
            Assert.That(double.IsNaN(ProtectiveStopPolicy.GetMinimumStopDistance(
                2.0, true, 200.0, 0.0, 0.01, 1.0)), Is.True);
        }

        [Test]
        public void BuyChoosesHigherMoreProtectiveStop()
        {
            Assert.That(ProtectiveStopPolicy.MoreProtectiveStop(true, 99.0, 100.0), Is.EqualTo(100.0));
            Assert.That(ProtectiveStopPolicy.MoreProtectiveStop(true, 99.0, 98.0), Is.EqualTo(99.0));
        }

        [Test]
        public void SellChoosesLowerMoreProtectiveStop()
        {
            Assert.That(ProtectiveStopPolicy.MoreProtectiveStop(false, 101.0, 100.0), Is.EqualTo(100.0));
            Assert.That(ProtectiveStopPolicy.MoreProtectiveStop(false, 101.0, 102.0), Is.EqualTo(101.0));
        }

        [Test]
        public void NonFiniteCandidateDoesNotCorruptCurrentStop()
        {
            Assert.That(ProtectiveStopPolicy.MoreProtectiveStop(true, 99.0, double.NaN), Is.EqualTo(99.0));
            Assert.That(double.IsNaN(ProtectiveStopPolicy.MoreProtectiveStop(true, double.NaN, 99.0)), Is.True);
        }
    }
}
