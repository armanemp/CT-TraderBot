using CTTraderBot.Core.Policies;
using NUnit.Framework;

namespace CTTraderBot.Core.Tests.Policies
{
    [TestFixture]
    public sealed class PositionRiskStateCodecTests
    {
        private const double Entry = 2400.25;
        private const double Risk = 4.5;
        private const double Tick = 0.01;
        private const long EntryTicks = 639000000000000000;
        private const string Side = "Buy";

        private static PositionRiskStateRecord Sample()
        {
            return new PositionRiskStateRecord
            {
                EntryPrice = Entry,
                InitialRiskDistance = Risk,
                TradeType = Side,
                EntryTimeTicks = EntryTicks,
                RiskDistanceEstimated = false,
                ApprovedRiskAmount = 75.50,
                RiskReviewRequired = true,
                RiskBudgetUnavailable = false,
                EmergencyCloseRequired = true,
                RiskReviewSticky = true
            };
        }

        private static PositionRiskStateRecord Decode(string raw)
        {
            PositionRiskStateRecord record;
            Assert.That(
                PositionRiskStateCodec.TryDeserialize(raw, Entry, Side, EntryTicks, Tick, out record),
                Is.True,
                "serialized position state should be accepted");
            return record;
        }

        [Test]
        public void CurrentVersionRoundTripPreservesAllRiskFlags()
        {
            string raw = PositionRiskStateCodec.Serialize(Sample());
            PositionRiskStateRecord parsed = Decode(raw);

            Assert.That(raw.StartsWith("v5|"), Is.True);
            Assert.That(parsed.Version, Is.EqualTo(PositionRiskStateCodec.CurrentVersion));
            Assert.That(parsed.InitialRiskDistance, Is.EqualTo(Risk));
            Assert.That(parsed.ApprovedRiskAmount, Is.EqualTo(75.50));
            Assert.That(parsed.RiskReviewRequired, Is.True);
            Assert.That(parsed.RiskBudgetUnavailable, Is.False);
            Assert.That(parsed.EmergencyCloseRequired, Is.True);
            Assert.That(parsed.RiskReviewSticky, Is.True);
        }

        [Test]
        public void LegacyFourFieldRecordDefaultsToEstimatedRiskAndNoBudget()
        {
            PositionRiskStateRecord parsed = Decode("2400.25|4.5|Buy|639000000000000000");

            Assert.That(parsed.Version, Is.Zero);
            Assert.That(parsed.RiskDistanceEstimated, Is.True);
            Assert.That(parsed.ApprovedRiskAmount, Is.Zero);
            Assert.That(parsed.RiskReviewRequired, Is.False);
            Assert.That(parsed.RiskBudgetUnavailable, Is.False);
            Assert.That(parsed.EmergencyCloseRequired, Is.False);
            Assert.That(parsed.RiskReviewSticky, Is.False);
        }

        [Test]
        public void LegacyFiveFieldRecordHonorsEstimateFlag()
        {
            PositionRiskStateRecord parsed = Decode("2400.25|4.5|Buy|639000000000000000|False");
            Assert.That(parsed.RiskDistanceEstimated, Is.False);
        }

        [TestCase("v2|2400.25|4.5|Buy|639000000000000000|True|70|True", 2)]
        [TestCase("v3|2400.25|4.5|Buy|639000000000000000|True|70|True|True", 3)]
        [TestCase("v4|2400.25|4.5|Buy|639000000000000000|True|70|True|True|True", 4)]
        public void EarlierVersionedRecordsAreReadAndCanBeUpgraded(string raw, int expectedVersion)
        {
            PositionRiskStateRecord parsed = Decode(raw);
            Assert.That(parsed.Version, Is.EqualTo(expectedVersion));
            Assert.That(parsed.ApprovedRiskAmount, Is.EqualTo(70.0));
            Assert.That(parsed.RiskReviewRequired, Is.True);
        }

        [Test]
        public void VersionFourEmergencyCloseRequestIsPreserved()
        {
            PositionRiskStateRecord parsed = Decode("v4|2400.25|4.5|Buy|639000000000000000|True|70|True|False|True");
            Assert.That(parsed.EmergencyCloseRequired, Is.True);
            Assert.That(parsed.RiskReviewSticky, Is.False);
        }

        [TestCase("Sell", Entry, EntryTicks)]
        [TestCase(Side, Entry + 1, EntryTicks)]
        [TestCase(Side, Entry, EntryTicks + 1)]
        public void WrongPositionIdentityIsRejected(string side, double entry, long ticks)
        {
            PositionRiskStateRecord parsed;
            Assert.That(
                PositionRiskStateCodec.TryDeserialize(
                    PositionRiskStateCodec.Serialize(Sample()), entry, side, ticks, Tick, out parsed),
                Is.False);
            Assert.That(parsed, Is.Null);
        }

        [TestCase("")]
        [TestCase("v5|incomplete")]
        [TestCase("v6|2400.25|4.5|Buy|639000000000000000|True|70|True|False|False|False")]
        [TestCase("v5|2400.25|4.5|Buy|639000000000000000|bad|70|True|False|False|False")]
        [TestCase("v5|2400.25|4.5|Buy|639000000000000000|True|NaN|True|False|False|False")]
        [TestCase("v5|2400.25|4.5|Buy|639000000000000000|True|-1|True|False|False|False")]
        [TestCase("v5|2400.25|0|Buy|639000000000000000|True|70|True|False|False|False")]
        public void MalformedOrUnsafeStateIsRejected(string raw)
        {
            PositionRiskStateRecord parsed;
            Assert.That(
                PositionRiskStateCodec.TryDeserialize(raw, Entry, Side, EntryTicks, Tick, out parsed),
                Is.False);
        }

        [Test]
        public void InvalidTickSizeIsRejected()
        {
            PositionRiskStateRecord parsed;
            Assert.That(
                PositionRiskStateCodec.TryDeserialize(
                    PositionRiskStateCodec.Serialize(Sample()), Entry, Side, EntryTicks, 0.0, out parsed),
                Is.False);
        }

        [Test]
        public void SerializerRejectsNonFiniteBudget()
        {
            PositionRiskStateRecord record = Sample();
            record.ApprovedRiskAmount = double.PositiveInfinity;
            Assert.Throws<System.ArgumentException>(
                (NUnit.Framework.TestDelegate)(() => { PositionRiskStateCodec.Serialize(record); }));
        }
    }
}
