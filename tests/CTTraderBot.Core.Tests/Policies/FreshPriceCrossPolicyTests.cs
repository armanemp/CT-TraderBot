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

        [Test]
        public void NonFinitePreviousQuote_IsNeverAValidCrossing()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(double.PositiveInfinity, 101.0, 100.0), Is.False);
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(double.PositiveInfinity, 99.0, 100.0), Is.False);
        }

        [Test]
        public void NonFiniteCurrentQuote_IsNeverAValidCrossing()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(99.0, double.PositiveInfinity, 100.0), Is.False);
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(101.0, double.NegativeInfinity, 100.0), Is.False);
        }

        [Test]
        public void NonFiniteTrigger_IsNeverAValidCrossing()
        {
            Assert.That(FreshPriceCrossPolicy.CrossedAbove(99.0, 101.0, double.NaN), Is.False);
            Assert.That(FreshPriceCrossPolicy.CrossedBelow(101.0, 99.0, double.PositiveInfinity), Is.False);
        }
    }
}
