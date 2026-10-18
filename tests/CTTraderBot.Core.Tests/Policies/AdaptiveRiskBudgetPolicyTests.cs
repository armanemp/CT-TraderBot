using System;
using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class AdaptiveRiskBudgetPolicyTests
    {
        private const double Tolerance = 1e-10;

        [Test]
        public void AdaptiveMode_UsesMinimumAtZeroQuality()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(0.0, true, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(0.5).Within(Tolerance));
        }

        [Test]
        public void AdaptiveMode_UsesSquaredQualityCurve()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(0.5, true, 0.5, 1.5, 0.0);

            // 0.5 + (0.5 * 0.5) * (1.5 - 0.5) = 0.75.
            Assert.That(actual, Is.EqualTo(0.75).Within(Tolerance));
        }

        [Test]
        public void AdaptiveMode_UsesMaximumAtFullQuality()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(1.0, true, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(1.5).Within(Tolerance));
        }

        [TestCase(-2.0, 0.0)]
        [TestCase(2.0, 1.5)]
        public void AdaptiveMode_ClampsQualityToZeroThroughOne(double quality, double expected)
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(quality, true, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void NonAdaptiveMode_UsesMaximumRegardlessOfQuality()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(0.0, false, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(1.5).Within(Tolerance));
        }

        [Test]
        public void PositiveAbsoluteCeiling_CapsCalculatedBudget()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(1.0, true, 0.5, 2.0, 1.25);

            Assert.That(actual, Is.EqualTo(1.25).Within(Tolerance));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        public void NonPositiveAbsoluteCeiling_DisablesHardCap(double ceiling)
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(1.0, true, 0.5, 1.5, ceiling);

            Assert.That(actual, Is.EqualTo(1.5).Within(Tolerance));
        }

        [Test]
        public void MaximumBelowMinimum_IsNormalizedToMinimum()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(0.0, true, 1.0, 0.5, 0.0);

            Assert.That(actual, Is.EqualTo(1.0).Within(Tolerance));
        }

        [Test]
        public void NegativeMinimumAndMaximum_NeverProduceNegativeBudget()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(1.0, false, -2.0, -1.0, 0.0);

            Assert.That(actual, Is.EqualTo(0.0).Within(Tolerance));
        }

        [Test]
        public void NonFiniteQuality_FallsBackToMinimumBudget()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(double.NaN, true, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(0.5).Within(Tolerance));
        }

        [Test]
        public void InfiniteQuality_FallsBackToMinimumBudget()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(double.PositiveInfinity, true, 0.5, 1.5, 0.0);

            Assert.That(actual, Is.EqualTo(0.5).Within(Tolerance));
        }

        [Test]
        public void HardCeilingIsAppliedAfterAdaptiveBudget()
        {
            double actual = AdaptiveRiskBudgetPolicy.Calculate(0.0, true, 1.0, 2.0, 0.75);

            Assert.That(actual, Is.EqualTo(0.75).Within(Tolerance));
        }
    }
}
