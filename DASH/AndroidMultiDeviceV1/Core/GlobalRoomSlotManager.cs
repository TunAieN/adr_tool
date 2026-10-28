namespace AndroidMultiDeviceV1.Core;

public sealed record RoomSlotDecision(bool Acquired,string Host,int ActiveCount,int Limit);
public sealed record RoomSlotRelease(bool Released,string? Host,int RemainingCount);

/// <summary>
/// Process-wide, atomic room occupancy registry. A device can own at most one
/// room slot. Stale leases are pruned periodically and on every public action.
/// </summary>
public sealed class GlobalRoomSlotManager
{
    static readonly TimeSpan LeaseTimeout=TimeSpan.FromMinutes(10);
    readonly object _gate=new();
    readonly Dictionary<string,Dictionary<string,DateTimeOffset>> _rooms=
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> _deviceRooms=new(StringComparer.OrdinalIgnoreCase);
    readonly System.Threading.Timer _cleanupTimer;

    public static GlobalRoomSlotManager Shared { get; } = new();

    GlobalRoomSlotManager()
    {
        _cleanupTimer=new System.Threading.Timer(_=>PruneExpired(DateTimeOffset.UtcNow),null,
            TimeSpan.FromMinutes(1),TimeSpan.FromMinutes(1));
    }

    public RoomSlotDecision TryAcquire(string host,string deviceKey,int limit)
    {
        host=LiveRoomHistory.Normalize(host);
        deviceKey=(deviceKey??string.Empty).Trim();
        limit=Math.Clamp(limit,1,100);
        if(host.Length==0)throw new ArgumentException("Host LIVE không hợp lệ.",nameof(host));
        if(deviceKey.Length==0)throw new ArgumentException("Device key không hợp lệ.",nameof(deviceKey));

        lock(_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            if(_deviceRooms.TryGetValue(deviceKey,out string? currentHost))
            {
                if(currentHost.Equals(host,StringComparison.OrdinalIgnoreCase) &&
                   _rooms.TryGetValue(host,out var currentOccupants) &&
                   currentOccupants.ContainsKey(deviceKey))
                {
                    currentOccupants[deviceKey]=DateTimeOffset.UtcNow;
                    return new RoomSlotDecision(true,host,currentOccupants.Count,limit);
                }
                ReleaseUnderLock(deviceKey);
            }

            if(!_rooms.TryGetValue(host,out var occupants))
            {
                occupants=new Dictionary<string,DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
                _rooms[host]=occupants;
            }
            if(occupants.Count>=limit)
                return new RoomSlotDecision(false,host,occupants.Count,limit);

            occupants[deviceKey]=DateTimeOffset.UtcNow;
            _deviceRooms[deviceKey]=host;
            return new RoomSlotDecision(true,host,occupants.Count,limit);
        }
    }

    public bool Heartbeat(string host,string deviceKey)
    {
        host=LiveRoomHistory.Normalize(host);
        lock(_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            if(!_deviceRooms.TryGetValue(deviceKey,out string? registeredHost) ||
               !registeredHost.Equals(host,StringComparison.OrdinalIgnoreCase) ||
               !_rooms.TryGetValue(host,out var occupants) ||
               !occupants.ContainsKey(deviceKey))return false;
            occupants[deviceKey]=DateTimeOffset.UtcNow;
            return true;
        }
    }

    public RoomSlotRelease Release(string deviceKey)
    {
        lock(_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            return ReleaseUnderLock(deviceKey);
        }
    }

    public int ActiveCount(string host)
    {
        host=LiveRoomHistory.Normalize(host);
        lock(_gate)
        {
            PruneExpiredUnderLock(DateTimeOffset.UtcNow);
            return _rooms.TryGetValue(host,out var occupants)?occupants.Count:0;
        }
    }

    internal int PruneExpired(DateTimeOffset now)
    {
        lock(_gate)return PruneExpiredUnderLock(now);
    }

    int PruneExpiredUnderLock(DateTimeOffset now)
    {
        var expired=_rooms.SelectMany(room=>room.Value
                .Where(entry=>now-entry.Value>=LeaseTimeout)
                .Select(entry=>entry.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach(string deviceKey in expired)ReleaseUnderLock(deviceKey);
        return expired.Length;
    }

    RoomSlotRelease ReleaseUnderLock(string deviceKey)
    {
        if(!_deviceRooms.Remove(deviceKey,out string? host))
            return new RoomSlotRelease(false,null,0);
        int remaining=0;
        if(_rooms.TryGetValue(host,out var occupants))
        {
            occupants.Remove(deviceKey);
            remaining=occupants.Count;
            if(remaining==0)_rooms.Remove(host);
        }
        return new RoomSlotRelease(true,host,remaining);
    }
}
