using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// A verified, device-scoped navigation recovery for non-authentication TikTok
/// subpages. It never clears app data, changes accounts or dismisses protection
/// challenges. All Appium actions run on the owning worker (not a parallel timer).
/// </summary>
public sealed class TikTokNavigationRecovery
{
    const string TikTokPackage="com.ss.android.ugc.trill";
    readonly AppiumSession _session;
    readonly AdbClient _adb;
    readonly string _serial;
    readonly Action<string> _log;

    public TikTokNavigationRecovery(AppiumSession session,AdbClient adb,string serial,Action<string> log)
    {
        _session=session;
        _adb=adb;
        _serial=serial;
        _log=log;
    }

    public static bool IsFindPeopleScreen(string xml)
    {
        if(string.IsNullOrWhiteSpace(xml))return false;
        try
        {
            var labels=XDocument.Parse(xml).Descendants()
                .SelectMany(node=>new[]{(string?)node.Attribute("text"),
                    (string?)node.Attribute("content-desc")})
                .Where(value=>!string.IsNullOrWhiteSpace(value))
                .Select(value=>value!.Trim()).ToArray();
            bool header=labels.Any(value=>EqualsLabel(value,"Find people") ||
                EqualsLabel(value,"Tìm bạn bè") || EqualsLabel(value,"Tìm người"));
            if(!header)return false;
            // The title on its own might be a search suggestion or a video.
            // Require a second, page-specific piece of accessibility evidence.
            return labels.Any(value=>EqualsLabel(value,"Search by name or username") ||
                EqualsLabel(value,"Connect Now") || EqualsLabel(value,"Use QR code") ||
                EqualsLabel(value,"Invite friends") || EqualsLabel(value,"Kết nối ngay") ||
                EqualsLabel(value,"Dùng mã QR") || EqualsLabel(value,"Mời bạn bè"));
        }
        catch(System.Xml.XmlException){return false;}
    }

    static bool EqualsLabel(string actual,string expected)=>
        actual.Equals(expected,StringComparison.OrdinalIgnoreCase);

    static bool IsSafeSearchCheckpoint(string xml)=>
        !IsFindPeopleScreen(xml) &&
        !LiveSearchEngine.IsSearchRecoveryProtectedUi(xml) &&
        (TikTokForegroundProbe.SourceHasHomeFeedNavigation(xml) ||
         LiveSearchEngine.IsLiveSearchGridXml(xml) ||
         xml.Contains("resource-id=\"com.ss.android.ugc.trill:id/ka6\"",StringComparison.Ordinal) ||
         xml.Contains("resource-id=\"com.ss.android.ugc.trill:id/k_g\"",StringComparison.Ordinal) ||
         xml.Contains("resource-id=\"com.ss.android.ugc.trill:id/hwj\"",StringComparison.Ordinal) ||
         xml.Contains("resource-id=\"com.ss.android.ugc.trill:id/hvp\"",StringComparison.Ordinal));

    /// <summary>
    /// Called only after Find people is positively identified. First tries up
    /// to five Back actions; if still trapped, one force-stop/relaunch retaining
    /// TikTok data. Returns true only on a verified Search/Home checkpoint.
    /// </summary>
    public async Task<bool> RecoverFindPeopleAsync(CancellationToken ct,string phase)
    {
        ct.ThrowIfCancellationRequested();
        string xml=await _session.SourceAsync(ct);
        if(!IsFindPeopleScreen(xml))return IsSafeSearchCheckpoint(xml);
        _log($"UI_STATE_DETECTED: FIND_PEOPLE; phase={phase}");
        _log($"NAV_RECOVERY_START: phase={phase}; maxBack=5; maxRestart=1; pmClear=false");
        await SaveDiagnosticsAsync(xml,phase,"initial",ct);
        for(int back=1;back<=5;back++)
        {
            ct.ThrowIfCancellationRequested();
            string package=await _session.CurrentPackageAsync(ct);
            if(package!=TikTokPackage || LiveSearchEngine.IsSearchRecoveryProtectedUi(xml))
            {
                _log($"NAV_RECOVERY_BLOCKED: phase={phase}; foreground={package}; protectedUi=true");
                return false;
            }
            // Prefer a verified visible Back button only when such an element
            // is exposed, otherwise AndroidBack follows the phone's own stack.
            await _session.BackAsync(ct);
            _log($"NAV_RECOVERY_BACK: attempt={back}/5; phase={phase}");
            await Task.Delay(750,ct);
            xml=await _session.SourceAsync(ct);
            if(IsSafeSearchCheckpoint(xml))
            {
                _log($"NAV_RECOVERY_SUCCESS: method=BACK; attempt={back}/5; phase={phase}");
                return true;
            }
            if(!IsFindPeopleScreen(xml))
            {
                _log($"NAV_RECOVERY_RECHECK: back={back}/5; leftFindPeople=true; " +
                    "searchCheckpoint=false; allow UI to settle");
                await Task.Delay(1000,ct);
                xml=await _session.SourceAsync(ct);
                if(IsSafeSearchCheckpoint(xml))
                {
                    _log($"NAV_RECOVERY_SUCCESS: method=BACK_SETTLED; attempt={back}/5; phase={phase}");
                    return true;
                }
                // The Find people page is gone, but this is an unverified
                // destination. Do NOT keep pressing Back from what might be
                // Home. Escalate to a single non-destructive app relaunch.
                _log($"NAV_RECOVERY_UNKNOWN_DESTINATION: afterBack={back}/5; " +
                    "next=RESTART_TIKTOK_KEEP_DATA");
                break;
            }
        }
        ct.ThrowIfCancellationRequested();
        string current=await _session.CurrentPackageAsync(ct);
        if(current!=TikTokPackage || LiveSearchEngine.IsSearchRecoveryProtectedUi(xml))
        {
            _log($"NAV_RECOVERY_BLOCKED: restart=false; foreground={current}; protectedUi=true");
            return false;
        }
        _log($"NAV_RECOVERY_APP_RESTART: phase={phase}; attempt=1/1; pmClear=false");
        await _adb.ForceStopTikTokAsync(_serial,ct);
        await Task.Delay(850,ct);
        await _adb.LaunchTikTokAsync(_serial,ct);
        for(int check=1;check<=12;check++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(check==1?1350:1100,ct);
            current=await _session.CurrentPackageAsync(ct);
            xml=await _session.SourceAsync(ct);
            if(current!=TikTokPackage)continue;
            if(LiveSearchEngine.IsSearchRecoveryProtectedUi(xml))
            {
                _log($"NAV_RECOVERY_BLOCKED: phase={phase}; protected UI after relaunch");
                return false;
            }
            if(IsSafeSearchCheckpoint(xml))
            {
                _log($"NAV_RECOVERY_SUCCESS: method=RESTART; check={check}/12; phase={phase}");
                return true;
            }
        }
        _log($"NAV_RECOVERY_EXHAUSTED: phase={phase}; back=5/5; restart=1/1; no pm clear");
        await SaveDiagnosticsAsync(xml,phase,"exhausted",ct);
        return false;
    }

    async Task SaveDiagnosticsAsync(string xml,string phase,string tag,CancellationToken ct)
    {
        try
        {
            var folder=Path.Combine(Storage.Folder,"diagnostics",
                _serial.Replace('.','_').Replace(':','_'),"nav-find-people");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+tag);
            await File.WriteAllTextAsync(path+".xml",xml,ct);
            await File.WriteAllTextAsync(path+".txt","Phase="+phase+Environment.NewLine+
                "Screen=FIND_PEOPLE"+Environment.NewLine+"Recovery="+tag,ct);
            // UI dumps are mandatory; screenshots are best-effort only and
            // must not stop recovery when UiAutomator2 is temporarily slow.
            _log($"NAV_RECOVERY_DIAGNOSTIC_SAVED: {path}.xml");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {_log($"NAV_RECOVERY_DIAGNOSTIC_WARNING: {ex.GetType().Name}");}
    }
}

/// <summary>Signals that a stale LIVE/room checkpoint was safely discarded.</summary>
public sealed class TikTokNavigationCheckpointException : Exception
{
    public TikTokNavigationCheckpointException():base("Find people recovered; resume at LIVE Search Phase 1 checkpoint."){}
}
