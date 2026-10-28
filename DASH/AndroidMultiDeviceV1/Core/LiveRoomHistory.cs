using System.Collections.Concurrent;

namespace AndroidMultiDeviceV1.Core;

/// <summary>Process-wide, bounded history of hosts already handled by this tool.</summary>
public static class LiveRoomHistory
{
    public static readonly ConcurrentDictionary<string,DateTime> VisitedRoomsCache=
        new(StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string username) =>
        (username??"").Trim().TrimStart('@').ToLowerInvariant();

    public static bool TryGetActive(string username,int cooldownMinutes,out TimeSpan age)
    {
        age=TimeSpan.MaxValue;
        string key=Normalize(username);
        if(key.Length==0 || cooldownMinutes<=0)return false;
        CleanupExpired(cooldownMinutes);
        if(!VisitedRoomsCache.TryGetValue(key,out DateTime visitedAt))return false;
        age=DateTime.Now-visitedAt;
        if(age<TimeSpan.Zero)age=TimeSpan.Zero;
        return age<TimeSpan.FromMinutes(cooldownMinutes);
    }

    public static void MarkVisited(string username,int cooldownMinutes)
    {
        CleanupExpired(cooldownMinutes);
        string key=Normalize(username);
        if(key.Length==0)return;
        VisitedRoomsCache[key]=DateTime.Now;
    }

    public static void CleanupExpired(int cooldownMinutes)
    {
        if(cooldownMinutes<=0)
        {
            VisitedRoomsCache.Clear();
            return;
        }
        DateTime expiry=DateTime.Now.AddMinutes(-cooldownMinutes);
        foreach(var pair in VisitedRoomsCache)
            if(pair.Value<=expiry)
                VisitedRoomsCache.TryRemove(pair.Key,out _);
    }
}
