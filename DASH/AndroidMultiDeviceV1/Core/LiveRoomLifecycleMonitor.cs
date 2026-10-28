using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

// V2.1.61: Independent LIVE room observation/navigation mode. This module NEVER
// invokes LiveCommentOneShot or LiveCommentAutoSender, and is not subscribed to
// any of their success/failure callbacks. Existing Phase 1-5 code is unchanged.
public sealed record LiveRoomUiState(
    bool IsLive, bool IsSearchGrid, bool IsMuted, bool IsAccessRestricted,
    bool IsEnded, int? Viewer, string ViewerRaw, string Notice,
    string? InlineUsername);

public sealed record LiveRoomIdentity(string Username, string DisplayName);

public sealed class LiveRoomTimer
{
    readonly TimeSpan _limit;
    readonly Stopwatch _elapsed = new();
    public string? Host { get; private set; }
    public TimeSpan Elapsed => _elapsed.Elapsed;
    public TimeSpan Limit => _limit;
    public bool IsExpired => _elapsed.IsRunning && _elapsed.Elapsed >= _limit;
    public LiveRoomTimer(int sessionSeconds) => _limit=TimeSpan.FromSeconds(sessionSeconds);
    public bool StartIfNew(string host)
    {
        if(string.IsNullOrWhiteSpace(host))return false;
        string normalized=host.Trim().TrimStart('@').ToLowerInvariant();
        if(string.Equals(Host,normalized,StringComparison.Ordinal))return false;
        Host=normalized;
        _elapsed.Restart(); // IMPORTANT: time is PER CONFIRMED ROOM, never accumulated.
        return true;
    }
    public void Pause() => _elapsed.Stop();
}

public static class LiveRoomUiReader
{
    public const string AudienceId="com.ss.android.ugc.trill:id/tv_online_audience_num";
    public const string HostUsernameId="com.ss.android.ugc.trill:id/user_name";
    static string Attr(XElement e,string key)=>e.Attribute(key)?.Value??"";
    static bool Visible(XElement e)=>Attr(e,"displayed")!="false";
    public static string? ValueById(string xml,string resourceId)
    {
        try
        {
            return XDocument.Parse(xml).Descendants().Where(e=>Visible(e) &&
                Attr(e,"resource-id")==resourceId)
                .Select(e=>Attr(e,"text")).FirstOrDefault(t=>!string.IsNullOrWhiteSpace(t));
        }
        catch(System.Xml.XmlException){return null;}
    }
    public static int? ParseAudience(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return null;
        var match=Regex.Match(raw.Trim(),@"^(?<amount>\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*(?<unit>[KkMm])?\+?$",RegexOptions.CultureInvariant);
        if(!match.Success)return null;
        string numeric=match.Groups["amount"].Value;
        bool hasUnit=match.Groups["unit"].Success;
        if(!hasUnit && Regex.IsMatch(numeric,@"^\d{1,3}(?:[.,]\d{3})+$"))
            numeric=numeric.Replace(".","").Replace(",","");
        else numeric=numeric.Replace(',','.');
        if(!double.TryParse(numeric,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double number))return null;
        if(hasUnit) number*=match.Groups["unit"].Value.Equals("M",StringComparison.OrdinalIgnoreCase)?1_000_000:1000;
        return number>=0 && number<=100_000_000?(int)Math.Round(number):null;
    }
    public static LiveRoomUiState Parse(string xml)
    {
        try
        {
            var doc=XDocument.Parse(xml);
            var visible=doc.Descendants().Where(Visible).ToArray();
            bool HasId(string id)=>visible.Any(e=>Attr(e,"resource-id").EndsWith(":id/"+id,StringComparison.Ordinal));
            var displayedTexts=visible.Select(e=>Attr(e,"text")).Where(t=>t.Length>0).ToArray();
            // Restriction notices must be explicit; absent composer/keyboard is not mute evidence.
            // Toasts are normally on the upper part of the screen; don't treat
            // similarly worded messages in the scrolling comment area as bans.
            // Only top-of-screen UI notices count. Messages inside LIVE chat
            // must never be mistaken for a restriction issued by TikTok.
            bool IsTopNoticeNode(XElement e)
            {
                var b=Regex.Match(Attr(e,"bounds"),@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
                return b.Success && int.TryParse(b.Groups[2].Value,out int top) &&
                    int.TryParse(b.Groups[4].Value,out int bottom) &&
                    top<200 && bottom<=260;
            }
            bool IsRestrictionNotice(string t)=>
                t.Contains("muted for the entire LIVE",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("you've been muted",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("you are muted",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("đã bị tắt tiếng",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("your comment wasn't posted because you're muted",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("you've been blocked from",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("you are blocked from",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("cannot watch this LIVE",StringComparison.OrdinalIgnoreCase);
            string notice=visible.Where(IsTopNoticeNode)
                .SelectMany(e=>new[]{Attr(e,"text"),Attr(e,"content-desc")})
                .FirstOrDefault(t=>!string.IsNullOrWhiteSpace(t) && IsRestrictionNotice(t))??"";
            bool muted=notice.Contains("muted",StringComparison.OrdinalIgnoreCase) ||
                notice.Contains("tắt tiếng",StringComparison.OrdinalIgnoreCase);
            bool restricted=!muted && notice.Length>0;
            string audience=visible.Where(e=>Attr(e,"resource-id")==AudienceId)
                .Select(e=>Attr(e,"text")).FirstOrDefault()??"";
            bool grid=visible.Any(e=>Attr(e,"class")=="android.widget.GridView") &&
                visible.Any(e=>Attr(e,"resource-id")=="com.ss.android.ugc.trill:id/q1m" ||
                    (Attr(e,"content-desc")=="LIVE" && Attr(e,"selected")=="true"));
            bool hostButton=HasId("k1s")||HasId("k13");
            bool live=!grid && (hostButton || visible.Any(e=>Attr(e,"resource-id")==AudienceId));
            bool ended=displayedTexts.Any(t=>t.Contains("LIVE has ended",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("This LIVE has ended",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Buổi LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Phiên LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase));
            string? inlineUsername=visible.Where(e=>Attr(e,"resource-id")==HostUsernameId)
                .Select(e=>Attr(e,"text")).FirstOrDefault(t=>!string.IsNullOrWhiteSpace(t));
            return new LiveRoomUiState(live,grid,muted,restricted,ended,
                ParseAudience(audience),audience,notice,inlineUsername);
        }
        catch(System.Xml.XmlException)
        {
            return new LiveRoomUiState(false,false,false,false,false,null,"","BAD_XML",null);
        }
    }
    public static (int X,int Y,string Selector)? HostButton(string xml)
    {
        try
        {
            var nodes=XDocument.Parse(xml).Descendants().Where(Visible);
            foreach(string id in new[]{"k1s","k13"})
            {
                XElement? node=nodes.FirstOrDefault(e=>Attr(e,"resource-id")=="com.ss.android.ugc.trill:id/"+id);
                if(node is null)continue;
                var match=Regex.Match(Attr(node,"bounds"),@"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
                if(!match.Success)continue;
                int x1=int.Parse(match.Groups[1].Value),y1=int.Parse(match.Groups[2].Value);
                int x2=int.Parse(match.Groups[3].Value),y2=int.Parse(match.Groups[4].Value);
                if(x2-x1<8 || y2-y1<8 || x1<0 || y1<0 || x2>1440 || y2>3200)continue;
                return ((x1+x2)/2,(y1+y2)/2,id);
            }
            return null;
        }
        catch(System.Xml.XmlException){return null;}
    }
}

public sealed class LiveRoomLifecycleMonitor
{
    readonly AppiumSession _session;
    readonly LiveManagerSettings _settings;
    readonly Action<string> _log;
    readonly AdbClient _adb;
    readonly string _deviceSerial;
    readonly string _diagnostics;
    const int ConsecutiveInvalidLimit=10;

    public LiveRoomLifecycleMonitor(AppiumSession session,LiveManagerSettings settings,
        Action<string> log,AdbClient adb,string deviceSerial)
    {
        _session=session;_settings=settings;_log=log;_adb=adb;_deviceSerial=deviceSerial;
        _diagnostics=Path.Combine(Storage.Folder,"diagnostics",
            "live-lifecycle-"+session.DeviceKey+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }

    async Task StoreAsync(string label,string xml,CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_diagnostics);
            string path=Path.Combine(_diagnostics,label);
            await File.WriteAllTextAsync(path+".xml",xml,ct);
            await File.WriteAllBytesAsync(path+".png",await _session.ScreenshotPngAsync(ct),ct);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){_log("ROOM_MONITOR_DIAGNOSTICS_ERROR: "+ex.Message);}
    }

    async Task<LiveRoomIdentity?> ReadHostAsync(string currentXml,CancellationToken ct)
    {
        var button=LiveRoomUiReader.HostButton(currentXml);
        if(button is null)return null;
        await _session.TapUiPointAsync(button.Value.X,button.Value.Y,ct);
        string profileXml="";
        string? username=null;
        string? name=null;
        for(int i=0;i<3;i++)
        {
            await Task.Delay(i==0?450:550,ct);
            profileXml=await _session.SourceAsync(ct);
            username=LiveRoomUiReader.ValueById(profileXml,LiveRoomUiReader.HostUsernameId);
            name=LiveRoomUiReader.ValueById(profileXml,"com.ss.android.ugc.trill:id/q2e") ??
                LiveRoomUiReader.ValueById(profileXml,"com.ss.android.ugc.trill:id/q16");
            if(!string.IsNullOrWhiteSpace(username))break;
        }
        if(string.IsNullOrWhiteSpace(username))
        {
            _log("ROOM_MONITOR_PROFILE_UNVERIFIED: chưa thấy username; không Back phỏng đoán.");
            await StoreAsync("profile-unverified",profileXml,ct);
            return null;
        }
        await _session.BackAsync(ct); // CLOSE ONLY a profile panel we positively observed
        bool returnedLive=false;
        for(int i=0;i<3;i++)
        {
            await Task.Delay(i==0?350:600,ct);
            var returned=LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
            if(returned.IsLive && !returned.IsSearchGrid){returnedLive=true;break;}
        }
        if(!returnedLive)
        {
            _log("ROOM_MONITOR_PROFILE_RETURN_UNVERIFIED: đã Back nhưng không thấy LIVE.");
            return null;
        }
        return new LiveRoomIdentity(username.Trim().TrimStart('@').ToLowerInvariant(),name??"?");
    }

    // A swipe is content navigation only: never run when a profile/modal is open.
    // No callback into any comment-sending functionality is made.
    async Task<bool> MoveToNextDistinctRoomAsync(string previousHost,CancellationToken ct)
    {
        for(int gesture=1;gesture<=2;gesture++)
        {
            var pre=LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
            if(!pre.IsLive || pre.IsSearchGrid || pre.IsAccessRestricted || pre.IsMuted)
                return false;
            _log($"ROOM_BROWSE_SWIPE_REQUESTED: {gesture}/2; from=@{previousHost}");
            await _session.SwipeLiveRoomUpAsync(ct);
            await Task.Delay(1800,ct);
            for(int check=1;check<=3;check++)
            {
                string xml=await _session.SourceAsync(ct);
                var state=LiveRoomUiReader.Parse(xml);
                if(state.IsMuted || state.IsAccessRestricted)
                {
                    _log("ROOM_RESTRICTION_DETECTED: không thực hiện hành động bình luận hoặc chuyển tiếp dựa trên hạn chế.");
                    await StoreAsync("restriction",xml,ct);
                    return false;
                }
                if(!state.IsLive || !state.Viewer.HasValue)
                {
                    await Task.Delay(1000,ct);
                    continue;
                }
                var identity=await ReadHostAsync(xml,ct);
                if(identity is null)return false;
                if(!string.Equals(identity.Username,previousHost,StringComparison.OrdinalIgnoreCase))
                {
                    // Second independent identity read to rule out transitions/loading overlays.
                    await Task.Delay(550,ct);
                    string verifyXml=await _session.SourceAsync(ct);
                    var confirmed=await ReadHostAsync(verifyXml,ct);
                    if(confirmed is not null && confirmed.Username==identity.Username)
                    {
                        _log($"ROOM_CHANGE_CONFIRMED: old=@{previousHost}; new=@{identity.Username}; stableReads=2");
                        return true;
                    }
                    _log("ROOM_CHANGE_UNVERIFIED: tên chủ phòng không ổn định; dừng an toàn.");
                    await StoreAsync("room-change-unverified",verifyXml,ct);
                    return false;
                }
                _log("ROOM_CHANGE_NOT_PROVEN: username không đổi, không reset thời gian.");
                break;
            }
        }
        return false;
    }

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var total=Stopwatch.StartNew();
        var roomTimer=new LiveRoomTimer(_settings.SessionDurationSeconds);
        int invalidStreak=0;
        int roomSerial=0;
        _log($"ROOM_MONITOR_START: limitPerRoom={_settings.SessionDurationSeconds}s; " +
             $"maxInvalidInRow={ConsecutiveInvalidLimit}; mode=READ_ONLY_BROWSE; commentModule=UNTOUCHED");
        _log("ROOM_MONITOR_NOTICE: Mute/chặn -> ghi nhận và dừng; không tự lách hạn chế để bình luận.");
        // An explicit user-started LIVE view is required. Never tap a random grid card.
        while(total.Elapsed<TimeSpan.FromSeconds(Math.Max(60,_settings.TotalSearchTimeoutSeconds)))
        {
            ct.ThrowIfCancellationRequested();
            string xml=await _session.SourceAsync(ct);
            var state=LiveRoomUiReader.Parse(xml);
            if(state.IsMuted || state.IsAccessRestricted)
            {
                roomTimer.Pause();
                _log($"ROOM_RESTRICTION_CONFIRMED: notice={state.Notice}; no automated re-entry/comment continuation.");
                await StoreAsync("restriction-confirmed",xml,ct);
                return "ROOM_RESTRICTION_CONFIRMED";
            }
            if(state.IsEnded)
            {
                roomTimer.Pause();
                _log("ROOM_ENDED: phòng đã kết thúc; chờ chuyển LIVE tự nhiên có xác minh.");
                await Task.Delay(1100,ct);
                continue;
            }
            if(!state.IsLive || state.IsSearchGrid)
            {
                _log("ROOM_MONITOR_NOT_IN_LIVE: không thao tác nhầm vào Search/video.");
                await StoreAsync("not-live",xml,ct);
                return "ROOM_MONITOR_NOT_IN_LIVE";
            }
            if(!state.Viewer.HasValue)
            {
                _log("ROOM_VIEWER_PENDING: chờ UI tải, không kết luận dưới ngưỡng.");
                for(int probe=2;probe<=5 && !state.Viewer.HasValue;probe++)
                {
                    await Task.Delay(probe<=3?1100:1600,ct);
                    xml=await _session.SourceAsync(ct);
                    state=LiveRoomUiReader.Parse(xml);
                    _log($"ROOM_VIEWER_RETRY: {probe}/5; raw='{state.ViewerRaw}'; parsed={state.Viewer?.ToString()??"?"}");
                    if(state.IsMuted || state.IsAccessRestricted)
                    {
                        await StoreAsync("restriction-during-viewer",xml,ct);
                        return "ROOM_RESTRICTION_CONFIRMED";
                    }
                    if(!state.IsLive || state.IsSearchGrid)return "ROOM_VIEWER_SCREEN_CHANGED";
                }
                if(!state.Viewer.HasValue)
                {
                    await StoreAsync("viewer-unverified",xml,ct);
                    return "ROOM_VIEWER_UNVERIFIED";
                }
            }
            var identity=await ReadHostAsync(xml,ct);
            if(identity is null)
            {
                _log("ROOM_IDENTITY_UNVERIFIED: dừng, không reset timer hoặc vuốt mò.");
                return "ROOM_IDENTITY_UNVERIFIED";
            }
            // Recheck after opening/closing profile, so viewer and identity
            // correspond to the CURRENT room rather than a prior screen.
            var after=LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
            if(!after.IsLive || !after.Viewer.HasValue)
                return "ROOM_STATE_CHANGED_DURING_VALIDATION";
            bool qualifies=after.Viewer.Value>=_settings.MinimumViewers &&
                (_settings.MaximumViewers==0 || after.Viewer.Value<=_settings.MaximumViewers);
            if(!qualifies)
            {
                invalidStreak++;
                roomTimer.Pause();
                _log($"ROOM_INVALID: host=@{identity.Username}; viewer={after.Viewer}; " +
                    $"range=[{_settings.MinimumViewers},{(_settings.MaximumViewers==0?"INF":_settings.MaximumViewers.ToString())}]; " +
                    $"consecutive={invalidStreak}/{ConsecutiveInvalidLimit}");
                if(invalidStreak>=ConsecutiveInvalidLimit)
                {
                    _log("ROOM_INVALID_LIMIT_REACHED: 10/10 -> quay về luồng Search theo từ khóa.");
                    var engine=new LiveSearchEngine(_session,_settings,_log,_=>{},_adb,_deviceSerial);
                    var report=await engine.RunAsync(ct); // EXISTING search, OCR, and 4+2+2 preserved.
                    if(!report.FinishReason.Contains("LIVE_ACCEPTED",StringComparison.OrdinalIgnoreCase))
                    {
                        _log("ROOM_KEYWORD_SEARCH_DONE: no accepted LIVE; "+report.FinishReason);
                        return "ROOM_SEARCH_NO_ACCEPTED_LIVE";
                    }
                    invalidStreak=0;
                    roomTimer=new LiveRoomTimer(_settings.SessionDurationSeconds);
                    _log("ROOM_KEYWORD_SEARCH_ACCEPTED: kiểm tra lại phòng và đồng hồ riêng.");
                    continue;
                }
                if(!await MoveToNextDistinctRoomAsync(identity.Username,ct))
                    return "ROOM_NEXT_NOT_VERIFIED";
                roomTimer=new LiveRoomTimer(_settings.SessionDurationSeconds);
                continue;
            }
            invalidStreak=0;
            // Begin the per-room residence clock ONLY after viewer/identity
            // acceptance; validation and loading do not consume the residence.
            if(!roomTimer.StartIfNew(identity.Username))
                _log($"ROOM_SAME_HOST: @{identity.Username}; elapsed={roomTimer.Elapsed.TotalSeconds:F0}s; no reset.");
            else
            {
                roomSerial++;
                _log($"ROOM_TIMER_RESET: room={roomSerial}; host=@{identity.Username}; " +
                    $"duration={_settings.SessionDurationSeconds}s; elapsed=0s");
            }
            _log($"ROOM_ACCEPTED: host=@{identity.Username}; name={identity.DisplayName}; " +
                 $"viewer={after.Viewer}; elapsed={roomTimer.Elapsed.TotalSeconds:F1}/{roomTimer.Limit.TotalSeconds:F0}s");
            // We are in browse/observation mode. Absolutely NO comment calls.
            // Poll through the room stay; a changed username starts a fresh
            // timer only after the next two-source verification above.
            // UI/notice polling is fast, but opening the host profile is
            // intrusive. Revalidate both username and audience periodically.
            TimeSpan nextRoomAudit=TimeSpan.FromSeconds(60);
            while(!roomTimer.IsExpired)
            {
                ct.ThrowIfCancellationRequested();
                if(total.Elapsed>=TimeSpan.FromSeconds(Math.Max(60,_settings.TotalSearchTimeoutSeconds)))
                    return "ROOM_MONITOR_TOTAL_DEADLINE";
                // Best effort to observe short-lived notices without tapping
                // the comment field or interfering with another module.
                await Task.Delay(Math.Clamp(_settings.UiPollMilliseconds,400,1000),ct);
                string pollingXml=await _session.SourceAsync(ct);
                var poll=LiveRoomUiReader.Parse(pollingXml);
                if(poll.IsMuted || poll.IsAccessRestricted)
                {
                    roomTimer.Pause();
                    _log("ROOM_RESTRICTION_CONFIRMED: " + poll.Notice);
                    await StoreAsync("restriction-during-stay",pollingXml,ct);
                    return "ROOM_RESTRICTION_CONFIRMED";
                }
                if(poll.IsEnded || !poll.IsLive)
                {
                    roomTimer.Pause();
                    _log("ROOM_UNEXPECTED_TRANSITION: LIVE ended/moved; wait for destination UI before host check.");
                    // TikTok's automatic next-room transition may temporarily
                    // have no audience or host node. Wait for the destination
                    // to render instead of mistaking this gap for a new room.
                    for(int settle=1;settle<=6;settle++)
                    {
                        await Task.Delay(750,ct);
                        var settleXml=await _session.SourceAsync(ct);
                        var settled=LiveRoomUiReader.Parse(settleXml);
                        if(settled.IsMuted || settled.IsAccessRestricted)
                        {
                            _log("ROOM_RESTRICTION_CONFIRMED: " + settled.Notice);
                            await StoreAsync("restriction-during-transition",settleXml,ct);
                            return "ROOM_RESTRICTION_CONFIRMED";
                        }
                        if(settled.IsLive && !settled.IsSearchGrid)
                        {
                            _log($"ROOM_TRANSITION_LIVE_VISIBLE: settle={settle}/6; " +
                                "revalidate Viewer and host, no premature timer reset.");
                            break;
                        }
                    }
                    break;
                }
                // A natural transition may take place without any swipe by
                // our worker. Even if the new LIVE UI is otherwise identical,
                // perform a 60-second identity and viewer revalidation.
                if(roomTimer.Elapsed>=nextRoomAudit)
                {
                    nextRoomAudit=roomTimer.Elapsed+TimeSpan.FromSeconds(60);
                    _log($"ROOM_PERIODIC_CHECK_START: every=60s; host=@{roomTimer.Host}; " +
                        $"elapsed={roomTimer.Elapsed.TotalSeconds:F0}s");
                    var nowIdentity=await ReadHostAsync(pollingXml,ct);
                    if(nowIdentity is null)
                    {
                        _log("ROOM_PERIODIC_HOST_UNVERIFIED: dừng, không đoán phòng mới.");
                        await StoreAsync("periodic-host-unverified",pollingXml,ct);
                        return "ROOM_IDENTITY_UNVERIFIED";
                    }
                    var afterCheckXml=await _session.SourceAsync(ct);
                    var afterCheck=LiveRoomUiReader.Parse(afterCheckXml);
                    _log($"ROOM_PERIODIC_CHECK_RESULT: host=@{nowIdentity.Username}; " +
                        $"viewer={afterCheck.Viewer?.ToString()??"?"}; " +
                        $"expectedHost=@{roomTimer.Host}; live={afterCheck.IsLive}");
                    if(!afterCheck.IsLive || !afterCheck.Viewer.HasValue)
                    {
                        _log("ROOM_PERIODIC_STATE_UNVERIFIED: ảnh/XML phòng đã thay đổi.");
                        await StoreAsync("periodic-state-unverified",afterCheckXml,ct);
                        return "ROOM_PERIODIC_STATE_UNVERIFIED";
                    }
                    if(nowIdentity.Username!=roomTimer.Host)
                    {
                        var confirm=await ReadHostAsync(afterCheckXml,ct);
                        if(confirm?.Username==nowIdentity.Username)
                        {
                            _log($"ROOM_AUTO_CHANGE_CONFIRMED: old=@{roomTimer.Host}; new=@{nowIdentity.Username}; " +
                                "source=PERIODIC_CHECK; stableReads=2");
                            roomTimer.Pause();
                            break;
                        }
                        _log("ROOM_AUTO_CHANGE_UNVERIFIED: không reset timer tùy tiện.");
                        await StoreAsync("periodic-change-unverified",afterCheckXml,ct);
                        return "ROOM_AUTO_CHANGE_UNVERIFIED";
                    }
                    bool withinRange=afterCheck.Viewer.Value>=_settings.MinimumViewers &&
                        (_settings.MaximumViewers==0 || afterCheck.Viewer.Value<=_settings.MaximumViewers);
                    if(!withinRange)
                    {
                        // Re-read, since one in-room viewer value can update
                        // during a LIVE. Never reject on one transient value.
                        await Task.Delay(650,ct);
                        var secondXml=await _session.SourceAsync(ct);
                        var second=LiveRoomUiReader.Parse(secondXml);
                        bool confirmedInvalid=second.IsLive && second.Viewer.HasValue &&
                            second.Viewer.Value<_settings.MinimumViewers ||
                            (second.IsLive && second.Viewer.HasValue &&
                             _settings.MaximumViewers>0 && second.Viewer.Value>_settings.MaximumViewers);
                        if(confirmedInvalid)
                        {
                            _log($"ROOM_PERIODIC_VIEWER_OUT_OF_RANGE: host=@{roomTimer.Host}; " +
                                $"read1={afterCheck.Viewer}; read2={second.Viewer}; " +
                                "return to normal room validation; timer NOT reset");
                            roomTimer.Pause();
                            break;
                        }
                    }
                }
            }
            if(roomTimer.IsExpired)
            {
                // Critical race: TikTok could have switched LIVE between two
                // polling cycles. Never swipe out of a newly changed room based
                // solely on the old room's expired stopwatch.
                var expiryXml=await _session.SourceAsync(ct);
                var expiryState=LiveRoomUiReader.Parse(expiryXml);
                if(!expiryState.IsLive || expiryState.IsSearchGrid)
                {
                    _log("ROOM_EXPIRY_UI_CHANGED: do not swipe without a verified LIVE.");
                    await StoreAsync("expiry-ui-changed",expiryXml,ct);
                    return "ROOM_EXPIRY_UI_CHANGED";
                }
                var expiryHost=await ReadHostAsync(expiryXml,ct);
                if(expiryHost is null)return "ROOM_EXPIRY_HOST_UNVERIFIED";
                if(expiryHost.Username!=roomTimer.Host)
                {
                    var expiryConfirmed=await ReadHostAsync(await _session.SourceAsync(ct),ct);
                    if(expiryConfirmed?.Username!=expiryHost.Username)
                        return "ROOM_EXPIRY_CHANGE_UNVERIFIED";
                    _log($"ROOM_AUTO_CHANGE_CONFIRMED: old=@{roomTimer.Host}; " +
                        $"new=@{expiryHost.Username}; source=EXPIRY_RECHECK; stableReads=2");
                    roomTimer.Pause();
                    roomTimer=new LiveRoomTimer(_settings.SessionDurationSeconds);
                    continue;
                }
                _log($"ROOM_SESSION_TIME_REACHED: host=@{roomTimer.Host}; " +
                    $"elapsed={roomTimer.Elapsed.TotalSeconds:F1}s; next room gets independent timer.");
                if(!await MoveToNextDistinctRoomAsync(identity.Username,ct))
                    return "ROOM_NEXT_NOT_VERIFIED";
            }
            roomTimer.Pause();
            roomTimer=new LiveRoomTimer(_settings.SessionDurationSeconds);
        }
        _log("ROOM_MONITOR_TOTAL_DEADLINE: exhausted overall search budget.");
        return "ROOM_MONITOR_TOTAL_DEADLINE";
    }
}
