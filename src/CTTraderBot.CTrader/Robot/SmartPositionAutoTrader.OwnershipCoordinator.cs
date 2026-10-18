using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private void ReloadSharedStorageIfNeeded(Position position)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSharedStorageReloadUtc).TotalMilliseconds < 500)
                return;

            // cTrader documents Reload as the way to observe values written by other instances.
            // All writes in this cBot are flushed immediately before relying on them.
            _lastSharedStorageReloadUtc = now;
            try
            {
                LocalStorage.Reload(SharedScope);
            }
            catch (Exception ex)
            {
                WarnThrottled(position, "Could not refresh shared manager lease from local storage: " + ex.Message);
            }
        }

        private bool TryAcquireManagementLease(Position position)
        {
            ReloadSharedStorageIfNeeded(position);
            string key = GetOwnerKey(position.Id);
            OwnershipLease lease;
            long nowTicks = Server.Time.Ticks;

            if (TryReadLease(key, out lease))
            {
                if (lease.Owner == InstanceId)
                {
                    _ownedPositionIds.Add(position.Id);
                    if (lease.ExpiryTicks - nowTicks <= TimeSpan.FromSeconds(OwnershipLeaseSeconds * 0.55).Ticks)
                        WriteLease(key);
                    return true;
                }

                bool alive = lease.ExpiryTicks > nowTicks;
                if (alive && ManagerPriority <= lease.Priority)
                    return false;

                // Expired owner, or this instance has explicitly higher priority: take over.
            }

            WriteLease(key);
            _ownedPositionIds.Add(position.Id);
            return true;
        }

        private void WriteLease(string key)
        {
            long expiryTicks = Server.Time.AddSeconds(OwnershipLeaseSeconds).Ticks;
            string value = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                InstanceId, ManagerPriority, expiryTicks);
            LocalStorage.SetString(key, value, SharedScope);
            LocalStorage.Flush(SharedScope);
        }

        private bool TryReadLease(string key, out OwnershipLease lease)
        {
            lease = null;
            string raw = LocalStorage.GetString(key, SharedScope);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string[] parts = raw.Split('|');
            int priority;
            long expiryTicks;
            if (parts.Length != 3 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out priority) ||
                !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expiryTicks))
                return false;

            lease = new OwnershipLease
            {
                Owner = parts[0],
                Priority = priority,
                ExpiryTicks = expiryTicks
            };
            return true;
        }
    }
}
