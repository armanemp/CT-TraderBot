using System;
using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class RoundTurnCommissionPolicyTests
    {
        [Test]
        public void AddsBothSidesOfCommissionToStopAndSlippageRisk()
        {
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(2.5, 1.0),
                Is.EqualTo(5.0));
            Assert.That(
                RoundTurnCommissionPolicy.AddRoundTurnCommissionToLoss(47.0, 5.0),
                Is.EqualTo(52.0));
        }

        [Test]
        public void MinimumOneWayCommissionAppliesWhenItExceedsVolumeEstimate()
        {
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(0.40, 1.25),
                Is.EqualTo(2.50));
        }

        [Test]
        public void ZeroCommissionIsAllowedForCommissionFreeSymbols()
        {
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(0.0, 0.0),
                Is.Zero);
            Assert.That(
                RoundTurnCommissionPolicy.AddRoundTurnCommissionToLoss(12.0, 0.0),
                Is.EqualTo(12.0));
        }

        [Test]
        public void NegativeOrNonFiniteCommissionIsUnknown()
        {
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(-0.1, 0.0),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(double.NaN, 0.0),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(1.0, double.PositiveInfinity),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.AddRoundTurnCommissionToLoss(10.0, double.NaN),
                Is.NaN);
        }

        [Test]
        public void OverflowedRoundTurnOrTotalRiskIsUnknown()
        {
            Assert.That(
                RoundTurnCommissionPolicy.CalculateRoundTurnCash(double.MaxValue, 0.0),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.AddRoundTurnCommissionToLoss(double.MaxValue, double.MaxValue),
                Is.NaN);
        }
    }
}
