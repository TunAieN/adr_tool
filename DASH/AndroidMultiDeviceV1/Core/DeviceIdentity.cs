using System.Text.RegularExpressions;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Owns the persistent identity and display-name invariants for managed devices.
/// Serial remains the runtime ADB key; DeviceId remains stable if the endpoint changes.
/// </summary>
internal static partial class DeviceIdentity
{
    [GeneratedRegex(@"^Phone\s+(\d+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)]
    private static partial Regex DefaultPhoneNamePattern();

    public static string NormalizeSerial(string? serial) => (serial??string.Empty).Trim();

    public static string AllocateName(IEnumerable<DeviceRecord> devices)
    {
        var snapshot=devices.ToList();
        var used=new HashSet<string>(snapshot.Select(device=>device.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        int next=snapshot.Select(device=>DefaultPhoneNamePattern().Match(device.Name.Trim()))
            .Where(match=>match.Success && int.TryParse(match.Groups[1].Value,out _))
            .Select(match=>int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max()+1;
        string candidate;
        do candidate=$"Phone {next++:D2}";
        while(used.Contains(candidate));
        return candidate;
    }

    /// <summary>
    /// Repairs legacy/hand-edited settings deterministically. The first record for
    /// a serial/name is retained; later duplicate names receive a fresh Phone NN.
    /// </summary>
    public static bool Normalize(Settings settings)
    {
        bool changed=false;
        settings.Devices??=[];
        settings.RemovedDeviceSerials??=[];
        int roomLimit=Math.Clamp(settings.MaxDevicesPerLiveRoom,1,100);
        if(settings.MaxDevicesPerLiveRoom!=roomLimit)
        {
            settings.MaxDevicesPerLiveRoom=roomLimit;
            changed=true;
        }

        var serials=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids=new HashSet<Guid>();
        for(int index=0;index<settings.Devices.Count;)
        {
            var device=settings.Devices[index];
            string serial=NormalizeSerial(device.Serial);
            if(serial.Length==0 || !serials.Add(serial))
            {
                settings.Devices.RemoveAt(index);
                changed=true;
                continue;
            }
            if(device.Serial!=serial){device.Serial=serial;changed=true;}
            index++;
        }

        foreach(var device in settings.Devices)
        {
            if(device.DeviceId==Guid.Empty || !ids.Add(device.DeviceId))
            {
                device.DeviceId=Guid.NewGuid();
                ids.Add(device.DeviceId);
                changed=true;
            }
        }

        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var device in settings.Devices)
        {
            string name=device.Name.Trim();
            if(name.Length==0 || !names.Add(name))
            {
                device.Name=AllocateName(settings.Devices.Concat(
                    names.Select(existing=>new DeviceRecord{Name=existing})));
                names.Add(device.Name);
                changed=true;
            }
            else if(device.Name!=name){device.Name=name;changed=true;}
        }

        var removed=settings.RemovedDeviceSerials
            .Select(NormalizeSerial)
            .Where(serial=>serial.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if(!removed.SequenceEqual(settings.RemovedDeviceSerials,StringComparer.OrdinalIgnoreCase))
        {
            settings.RemovedDeviceSerials=removed;
            changed=true;
        }
        return changed;
    }
}
