using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class M5TrendAlignmentPolicyTests
    {
        [TestCase(TrendBias.Bullish, TrendBias.Bullish)]
        [TestCase(TrendBias.Bearish, TrendBias.Bearish)]
        public void RequiredAlignment_AllowsMatchingDirection(TrendBias m5Bias, TrendBias signalBias)
        {
            Assert.That(M5TrendAlignmentPolicy.Allows(m5Bias, signalBias, true), Is.True);
        }

        [TestCase(TrendBias.Bullish, TrendBias.Bearish)]
        [TestCase(TrendBias.Bearish, TrendBias.Bullish)]
        public void RequiredAlignment_RejectsOppositeDirection(TrendBias m5Bias, TrendBias signalBias)
        {
            Assert.That(M5TrendAlignmentPolicy.Allows(m5Bias, signalBias, true), Is.False);
        }

        [TestCase(TrendBias.Neutral, TrendBias.Bullish)]
        [TestCase(TrendBias.Neutral, TrendBias.Bearish)]
        [TestCase(TrendBias.Bullish, TrendBias.Neutral)]
        [TestCase(TrendBias.Bearish, TrendBias.Neutral)]
        [TestCase(TrendBias.Neutral, TrendBias.Neutral)]
        public void RequiredAlignment_RejectsNeutralBias(TrendBias m5Bias, TrendBias signalBias)
        {
            Assert.That(M5TrendAlignmentPolicy.Allows(m5Bias, signalBias, true), Is.False);
        }

        [TestCase(TrendBias.Bullish, TrendBias.Bearish)]
        [TestCase(TrendBias.Neutral, TrendBias.Bullish)]
        public void SoftContextMode_LeavesDecisionToCaller(TrendBias m5Bias, TrendBias signalBias)
        {
            Assert.That(M5TrendAlignmentPolicy.Allows(m5Bias, signalBias, false), Is.True);
        }
    }
}
