using System;
using System.Collections.Generic;
using System.Globalization;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    public partial class SmartPositionAutoTraderM5MicroSwingPrecisionHedgedVolume
    {
        private bool ReloadSharedStorageIfNeeded(Position position)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSharedStorageReloadUtc).TotalMilliseconds < 500)
                return _sharedStorageHealthy;

            // Never make an ownership decision from stale cache after a failed refresh.
            _lastSharedStorageReloadUtc = now;
            try
            {
                LocalStorage.Reload(SharedScope);
                _sharedStorageHealthy = true;
                return true;
            }
            catch (Exception ex)
            {
                _sharedStorageHealthy = false;
                WarnThrottled(position, "Could not refresh shared manager lease; this instance will not manage the position until storage is readable: " + ex.Message);
                SetVolumeNotice("Shared manager storage is unavailable. Position management is paused to avoid competing managers.", true);
                return false;
            }
        }

        private bool TryAcquireManagementLease(Position position)
        {
            if (!ReloadSharedStorageIfNeeded(position))
                return false;

            string key = GetOwnerKey(position.Id);
            OwnershipLease lease;
            long nowTicks = Server.Time.Ticks;

            bool leaseExists = TryReadLease(key, out lease);
            if (!_sharedStorageHealthy)
                return false;

            if (leaseExists)
            {
                if (lease.Owner == InstanceId)
                {
                    if (lease.ExpiryTicks - nowTicks <= TimeSpan.FromSeconds(OwnershipLeaseSeconds * 0.55).Ticks &&
                        !WriteLease(key))
                    {
                        _ownedPositionIds.Remove(position.Id);
                        return false;
                    }

                    _ownedPositionIds.Add(position.Id);
                    return true;
                }

                bool alive = lease.ExpiryTicks > nowTicks;
                if (alive && ManagerPriority <= lease.Priority)
                    return false;

                // Expired owner, or this instance has explicitly higher priority: take over.
            }

            if (!WriteLease(key))
                return false;

            _ownedPositionIds.Add(position.Id);
            return true;
        }

        private bool WriteLease(string key)
        {
            long expiryTicks = Server.Time.AddSeconds(OwnershipLeaseSeconds).Ticks;
            string value = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}",
                InstanceId, ManagerPriority, expiryTicks);
            try
            {
                LocalStorage.SetString(key, value, SharedScope);
                LocalStorage.Flush(SharedScope);
                _sharedStorageHealthy = true;
                return true;
            }
            catch (Exception ex)
            {
                _sharedStorageHealthy = false;
                Diag("Could not persist management lease; refusing ownership: {0}", ex.Message);
                SetVolumeNotice("Could not persist the shared management lease. This instance will not modify positions.", true);
                return false;
            }
        }

        private bool TryReadLease(string key, out OwnershipLease lease)
        {
            lease = null;
            string raw;
            try
            {
                raw = LocalStorage.GetString(key, SharedScope);
            }
            catch (Exception ex)
            {
                _sharedStorageHealthy = false;
                Diag("Could not read shared management lease: {0}", ex.Message);
                return false;
            }

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
