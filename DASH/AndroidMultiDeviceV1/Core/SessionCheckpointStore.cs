using System.Text.Json;

namespace AndroidMultiDeviceV1.Core;

// Diagnostic record only: NEVER treated as proof that TikTok is still logged in.
public sealed class SessionCheckpoint
{
    public string DeviceSerial { get; set; } = "";
    public Guid AccountId { get; set; }
    public string Stage { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed class SessionCheckpointStore
{
    static readonly string FilePath=Path.Combine(Storage.Folder,"session-checkpoints.json");
    static readonly object Gate=new();
    public void Record(string serial,Guid accountId,string stage)
    {
        lock(Gate)
        {
            List<SessionCheckpoint> records=[];
            try
            {
                if(File.Exists(FilePath))
                    records=JsonSerializer.Deserialize<List<SessionCheckpoint>>(File.ReadAllText(FilePath),Storage.JsonOptions)??[];
            }
            catch(JsonException){ /* Diagnostics do not override live UI truth */ }
            records.RemoveAll(x=>string.Equals(x.DeviceSerial,serial,StringComparison.OrdinalIgnoreCase));
            records.Add(new SessionCheckpoint{DeviceSerial=serial,AccountId=accountId,Stage=stage,UpdatedAt=DateTimeOffset.UtcNow});
            Storage.WriteAtomic(FilePath,JsonSerializer.Serialize(records,Storage.JsonOptions));
        }
    }
    // Prune diagnostic references to deleted accounts; never used as login proof.
    public void RemoveAccounts(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selected=ids.ToHashSet();
        if(selected.Count==0)return;
        lock(Gate)
        {
            if(!File.Exists(FilePath))return;
            var records=JsonSerializer.Deserialize<List<SessionCheckpoint>>(
                File.ReadAllText(FilePath),Storage.JsonOptions)??[];
            if(records.RemoveAll(x=>selected.Contains(x.AccountId))>0)
                Storage.WriteAtomic(FilePath,JsonSerializer.Serialize(records,Storage.JsonOptions));
        }
    }

}
