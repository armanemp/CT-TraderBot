using System;
using System.Globalization;

namespace CTTraderBot.Core.Policies
{
    /// <summary>
    /// Platform-independent representation of a cBot position's persisted risk state.
    /// </summary>
    public sealed class PositionRiskStateRecord
    {
        public int Version { get; set; }
        public double EntryPrice { get; set; }
        public double InitialRiskDistance { get; set; }
        public string TradeType { get; set; }
        public long EntryTimeTicks { get; set; }
        public bool RiskDistanceEstimated { get; set; }
        public double ApprovedRiskAmount { get; set; }
        public bool RiskReviewRequired { get; set; }
        public bool RiskBudgetUnavailable { get; set; }
        public bool EmergencyCloseRequired { get; set; }
        public bool RiskReviewSticky { get; set; }
    }

    /// <summary>
    /// Encodes the current v5 position-state format and reads earlier versions without
    /// trusting malformed records or records belonging to a different position identity.
    /// </summary>
    public static class PositionRiskStateCodec
    {
        public const int CurrentVersion = 5;

        public static bool TryDeserialize(
            string serialized,
            double expectedEntryPrice,
            string expectedTradeType,
            long expectedEntryTimeTicks,
            double tickSize,
            out PositionRiskStateRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(serialized) ||
                !IsFinite(expectedEntryPrice) ||
                string.IsNullOrWhiteSpace(expectedTradeType) ||
                !IsFinite(tickSize) || tickSize <= 0.0)
                return false;

            string[] parts = serialized.Split('|');
            int version;
            int offset;
            int optionalFieldCount;

            if (parts.Length > 0 && string.Equals(parts[0], "v5", StringComparison.Ordinal))
            {
                version = 5;
                offset = 1;
                optionalFieldCount = 6;
                if (parts.Length != 11) return false;
            }
            else if (parts.Length > 0 && string.Equals(parts[0], "v4", StringComparison.Ordinal))
            {
                version = 4;
                offset = 1;
                optionalFieldCount = 5;
                if (parts.Length != 10) return false;
            }
            else if (parts.Length > 0 && string.Equals(parts[0], "v3", StringComparison.Ordinal))
            {
                version = 3;
                offset = 1;
                optionalFieldCount = 4;
                if (parts.Length != 9) return false;
            }
            else if (parts.Length > 0 && string.Equals(parts[0], "v2", StringComparison.Ordinal))
            {
                version = 2;
                offset = 1;
                optionalFieldCount = 3;
                if (parts.Length != 8) return false;
            }
            else
            {
                version = 0;
                offset = 0;
                if (parts.Length != 4 && parts.Length != 5) return false;
                optionalFieldCount = parts.Length - 4;
            }

            double entryPrice;
            double initialRiskDistance;
            long entryTimeTicks;
            if (!double.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out entryPrice) ||
                !double.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out initialRiskDistance) ||
                !string.Equals(parts[offset + 2], expectedTradeType, StringComparison.Ordinal) ||
                !long.TryParse(parts[offset + 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out entryTimeTicks) ||
                !IsFinite(entryPrice) ||
                !IsFinite(initialRiskDistance) ||
                initialRiskDistance <= 0.0 ||
                initialRiskDistance < tickSize * 0.5 ||
                Math.Abs(entryPrice - expectedEntryPrice) > tickSize * 0.5 ||
                entryTimeTicks != expectedEntryTimeTicks)
                return false;

            bool estimated = true;
            double approvedRisk = 0.0;
            bool reviewRequired = false;
            bool budgetUnavailable = false;
            bool emergencyCloseRequired = false;
            bool stickyReview = false;
            int optionalStart = offset + 4;

            if (optionalFieldCount >= 1 &&
                !bool.TryParse(parts[optionalStart], out estimated))
                return false;

            if (optionalFieldCount >= 3)
            {
                if (!double.TryParse(parts[optionalStart + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out approvedRisk) ||
                    !bool.TryParse(parts[optionalStart + 2], out reviewRequired) ||
                    !IsFinite(approvedRisk) || approvedRisk < 0.0)
                    return false;
            }

            if (optionalFieldCount >= 4 &&
                !bool.TryParse(parts[optionalStart + 3], out budgetUnavailable))
                return false;

            if (optionalFieldCount >= 5 &&
                !bool.TryParse(parts[optionalStart + 4], out emergencyCloseRequired))
                return false;

            if (optionalFieldCount >= 6 &&
                !bool.TryParse(parts[optionalStart + 5], out stickyReview))
                return false;

            record = new PositionRiskStateRecord
            {
                Version = version,
                EntryPrice = entryPrice,
                InitialRiskDistance = initialRiskDistance,
                TradeType = parts[offset + 2],
                EntryTimeTicks = entryTimeTicks,
                RiskDistanceEstimated = estimated,
                ApprovedRiskAmount = approvedRisk,
                RiskReviewRequired = reviewRequired,
                RiskBudgetUnavailable = budgetUnavailable,
                EmergencyCloseRequired = emergencyCloseRequired,
                RiskReviewSticky = stickyReview
            };
            return true;
        }

        public static string Serialize(PositionRiskStateRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            if (!IsFinite(record.EntryPrice) ||
                !IsFinite(record.InitialRiskDistance) || record.InitialRiskDistance <= 0.0 ||
                string.IsNullOrWhiteSpace(record.TradeType) ||
                record.EntryTimeTicks < 0 ||
                !IsFinite(record.ApprovedRiskAmount) || record.ApprovedRiskAmount < 0.0)
                throw new ArgumentException("Position risk state contains invalid identity or numeric fields.", nameof(record));

            return string.Format(
                CultureInfo.InvariantCulture,
                "v5|{0:R}|{1:R}|{2}|{3}|{4}|{5:R}|{6}|{7}|{8}|{9}",
                record.EntryPrice,
                record.InitialRiskDistance,
                record.TradeType,
                record.EntryTimeTicks,
                record.RiskDistanceEstimated,
                record.ApprovedRiskAmount,
                record.RiskReviewRequired,
                record.RiskBudgetUnavailable,
                record.EmergencyCloseRequired,
                record.RiskReviewSticky);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
