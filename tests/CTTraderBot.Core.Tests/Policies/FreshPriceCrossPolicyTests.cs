using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class FreshPriceCrossPolicyTests
    {
        [Test]
        public void CrossedAbove_ReturnsTrueWhenQuoteMovesFromBelowToAbove()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(99.9, 100.1, 100.0), Is.True);
        }

        [Test]
        public void CrossedAbove_AllowsPreviousQuoteExactlyAtTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(100.0, 100.1, 100.0), Is.True);
        }

        [Test]
        public void CrossedAbove_ReturnsFalseWhenPriceWasAlreadyAboveTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(100.1, 100.2, 100.0), Is.False);
        }

        [Test]
        public void CrossedAbove_RequiresCurrentQuoteStrictlyAboveTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(99.9, 100.0, 100.0), Is.False);
        }

        [Test]
        public void CrossedAbove_ReturnsFalseWithoutPositivePreviousQuote()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(0.0, 100.1, 100.0), Is.False);
        }

        [Test]
        public void CrossedBelow_ReturnsTrueWhenQuoteMovesFromAboveToBelow()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(100.1, 99.9, 100.0), Is.True);
        }

        [Test]
        public void CrossedBelow_AllowsPreviousQuoteExactlyAtTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(100.0, 99.9, 100.0), Is.True);
        }

        [Test]
        public void CrossedBelow_ReturnsFalseWhenPriceWasAlreadyBelowTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(99.9, 99.8, 100.0), Is.False);
        }

        [Test]
        public void CrossedBelow_RequiresCurrentQuoteStrictlyBelowTrigger()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(100.1, 100.0, 100.0), Is.False);
        }

        [Test]
        public void CrossedBelow_ReturnsFalseWithoutPositivePreviousQuote()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(0.0, 99.9, 100.0), Is.False);
        }
    }
}
