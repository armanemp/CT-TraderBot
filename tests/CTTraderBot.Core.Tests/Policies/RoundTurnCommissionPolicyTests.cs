using System;
using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class RoundTurnCommissionPolicyTests
    {
        [TestCase(30.0, 0.10, 100000.0, 100000.0, BrokerCommissionBasis.UsdPerMillionUsdVolume, 3.0)]
        [TestCase(3.5, 0.20, 0.0, 0.0, BrokerCommissionBasis.UsdPerOneLot, 0.70)]
        [TestCase(0.01, 1.0, 0.0, 150000.0, BrokerCommissionBasis.PercentageOfTradingVolume, 15.0)]
        [TestCase(2.2, 0.50, 0.0, 0.0, BrokerCommissionBasis.QuoteCurrencyPerOneLot, 1.10)]
        public void EstimatesOneWayCommissionForEachBrokerBasis(
            double rate,
            double lots,
            double notionalUsd,
            double notionalQuote,
            BrokerCommissionBasis basis,
            double expected)
        {
            Assert.That(
                RoundTurnCommissionPolicy.EstimateOneWayCommission(rate, lots, notionalUsd, notionalQuote, basis),
                Is.EqualTo(expected).Within(1e-10));
        }

        [Test]
        public void ZeroRateDoesNotRequireNotionalConversion()
        {
            Assert.That(
                RoundTurnCommissionPolicy.EstimateOneWayCommission(
                    0.0, 0.10, double.NaN, double.NaN, BrokerCommissionBasis.UsdPerMillionUsdVolume),
                Is.Zero);
        }

        [Test]
        public void InvalidCommissionBasisInputsAreUnknown()
        {
            Assert.That(
                RoundTurnCommissionPolicy.EstimateOneWayCommission(
                    30.0, 0.0, 100000.0, 100000.0, BrokerCommissionBasis.UsdPerMillionUsdVolume),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.EstimateOneWayCommission(
                    0.01, 1.0, 0.0, double.NaN, BrokerCommissionBasis.PercentageOfTradingVolume),
                Is.NaN);
            Assert.That(
                RoundTurnCommissionPolicy.EstimateOneWayCommission(
                    double.PositiveInfinity, 1.0, 100000.0, 100000.0, BrokerCommissionBasis.UsdPerMillionUsdVolume),
                Is.NaN);
        }

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
