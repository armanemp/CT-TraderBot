using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class PostFillRiskPolicyTests
    {
        [TestCase(90.0, 100.0, 0.05)]
        [TestCase(100.0, 100.0, 0.05)]
        [TestCase(105.0, 100.0, 0.05)]
        [TestCase(0.0, 100.0, 0.05)]
        public void RiskAtOrBelowToleranceLimit_IsWithinBudget(double estimatedLoss, double approvedLoss, double tolerance)
        {
            Assert.That(
                PostFillRiskPolicy.Assess(estimatedLoss, approvedLoss, tolerance),
                Is.EqualTo(PostFillRiskAssessment.WithinBudget));
        }

        [TestCase(105.0001, 100.0, 0.05)]
        [TestCase(110.0, 100.0, 0.05)]
        public void RiskAboveToleranceLimit_IsOverBudget(double estimatedLoss, double approvedLoss, double tolerance)
        {
            Assert.That(
                PostFillRiskPolicy.Assess(estimatedLoss, approvedLoss, tolerance),
                Is.EqualTo(PostFillRiskAssessment.OverBudget));
        }

        [Test]
        public void NonFiniteLoss_IsUnknown()
        {
            Assert.That(PostFillRiskPolicy.Assess(double.NaN, 100.0, 0.05), Is.EqualTo(PostFillRiskAssessment.Unknown));
            Assert.That(PostFillRiskPolicy.Assess(double.PositiveInfinity, 100.0, 0.05), Is.EqualTo(PostFillRiskAssessment.Unknown));
        }

        [Test]
        public void InvalidBudget_IsUnknown()
        {
            Assert.That(PostFillRiskPolicy.Assess(1.0, 0.0, 0.05), Is.EqualTo(PostFillRiskAssessment.Unknown));
            Assert.That(PostFillRiskPolicy.Assess(1.0, -1.0, 0.05), Is.EqualTo(PostFillRiskAssessment.Unknown));
            Assert.That(PostFillRiskPolicy.Assess(1.0, double.NaN, 0.05), Is.EqualTo(PostFillRiskAssessment.Unknown));
        }

        [Test]
        public void InvalidTolerance_IsUnknown()
        {
            Assert.That(PostFillRiskPolicy.Assess(1.0, 100.0, -0.01), Is.EqualTo(PostFillRiskAssessment.Unknown));
            Assert.That(PostFillRiskPolicy.Assess(1.0, 100.0, 1.01), Is.EqualTo(PostFillRiskAssessment.Unknown));
            Assert.That(PostFillRiskPolicy.Assess(1.0, 100.0, double.NaN), Is.EqualTo(PostFillRiskAssessment.Unknown));
        }

        [Test]
        public void OverflowedThreshold_IsUnknown()
        {
            Assert.That(
                PostFillRiskPolicy.Assess(double.MaxValue, double.MaxValue, 1.0),
                Is.EqualTo(PostFillRiskAssessment.Unknown));
        }
    }
}
