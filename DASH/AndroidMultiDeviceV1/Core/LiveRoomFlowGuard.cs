using System.Diagnostics;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// V2.1.63: a defensive, read-only room-state checkpoint attached to the
/// EXISTING Phase 1-5 workflow. Uses the SAME Appium session, sequentially,
/// never an overlapping worker and never invokes any comment action.
/// A room switch or a confirmed restriction stops the caller; it never
/// triggers automatic posting in a different LIVE.
/// </summary>
public sealed class LiveRoomFlowGuard
{
    readonly AppiumSession _session;
    readonly LiveManagerSettings _config;
    readonly Action<string> _log;
    readonly LiveRoomTimer _roomTimer;
    readonly string _diagnostics;
    string? _host;
    TimeSpan _nextHostAudit = TimeSpan.FromSeconds(60);
    int _viewerMissingStreak;
    bool _initialized;

    public string? HostUsername => _host;

    public LiveRoomFlowGuard(AppiumSession session,LiveManagerSettings config,Action<string> log)
    {
        _session=session;_config=config;_log=log;
        _roomTimer=new LiveRoomTimer(config.SessionDurationSeconds);
        _diagnostics=Path.Combine(Storage.Folder,"diagnostics",
            "live-flow-watchdog-"+session.DeviceKey+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    }

    async Task SaveAsync(string label,string xml,CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_diagnostics);
            string basename=Path.Combine(_diagnostics,label);
            await File.WriteAllTextAsync(basename+".xml",xml,ct);
            await File.WriteAllBytesAsync(basename+".png",await _session.ScreenshotPngAsync(ct),ct);
            _log($"ROOM_FLOW_DIAGNOSTIC_SAVED: {basename}");
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){_log($"ROOM_FLOW_DIAGNOSTIC_ERROR: {ex.GetType().Name}");}
    }

    async Task<(LiveRoomIdentity Host,LiveRoomUiState LiveState)?> ReadHostAsync(string xml,CancellationToken ct)
    {
        var button=LiveRoomUiReader.HostButton(xml);
        if(button is null)return null;
        await _session.TapUiPointAsync(button.Value.X,button.Value.Y,ct);
        string? username=null;
        string? name=null;
        for(int i=0;i<3;i++)
        {
            await Task.Delay(i==0?350:500,ct);
            var profile=await _session.SourceAsync(ct);
            username=LiveRoomUiReader.ValueById(profile,LiveRoomUiReader.HostUsernameId);
            name=LiveRoomUiReader.ValueById(profile,"com.ss.android.ugc.trill:id/q2e") ??
                 LiveRoomUiReader.ValueById(profile,"com.ss.android.ugc.trill:id/q16");
            if(!string.IsNullOrWhiteSpace(username))break;
        }
        if(string.IsNullOrWhiteSpace(username))
        {
            // Do not Back blindly if the profile did not open; if the
            // profile panel is visible, a single Back is appropriate.
            _log("ROOM_FLOW_HOST_PROFILE_UNVERIFIED: no username; stop checkpoint.");
            return null;
        }
        await _session.BackAsync(ct);
        for(int i=0;i<3;i++)
        {
            await Task.Delay(i==0?300:500,ct);
            var returned=LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
            if(returned is {IsLive:true,IsSearchGrid:false})
                return (new LiveRoomIdentity(username.Trim().TrimStart('@').ToLowerInvariant(),name??"?"),returned);
        }
        _log("ROOM_FLOW_HOST_RETURN_UNVERIFIED: profile Back did not return to LIVE.");
        return null;
    }

    bool WithinRange(int viewers) => viewers>=_config.MinimumViewers &&
        (_config.MaximumViewers==0 || viewers<=_config.MaximumViewers);

    // Called immediately after Phase 3 accepts the room, BEFORE Phase 5.
    public async Task<string?> InitializeAsync(CancellationToken ct)
    {
        string xml=await _session.SourceAsync(ct);
        var state=LiveRoomUiReader.Parse(xml);
        if(state.IsMuted || state.IsAccessRestricted)
        {
            await SaveAsync("initial-restriction",xml,ct);
            return "ROOM_FLOW_RESTRICTION_CONFIRMED";
        }
        if(!state.IsLive || state.IsSearchGrid || !state.Viewer.HasValue)
        {
            await SaveAsync("initial-state-unverified",xml,ct);
            return "ROOM_FLOW_INITIAL_STATE_UNVERIFIED";
        }
        var identity=await ReadHostAsync(xml,ct);
        if(identity is null)
        {
            await SaveAsync("initial-host-unverified",xml,ct);
            return "ROOM_FLOW_INITIAL_HOST_UNVERIFIED";
        }
        // ReadHostAsync already verified the LIVE page AFTER closing profile.
        // Reuse this fresh return-state instead of requesting another entire XML.
        var after=identity.Value.LiveState;
        if(after.IsMuted || after.IsAccessRestricted)
        {
            _log("ROOM_FLOW_INITIAL_RESTRICTION: after host profile close; stop current room actions.");
            return "ROOM_FLOW_RESTRICTION_CONFIRMED";
        }
        if(!after.IsLive || !after.Viewer.HasValue || !WithinRange(after.Viewer.Value))
        {
            _log($"ROOM_FLOW_INITIAL_VIEWER_CHECK_FAILED: viewers={after.Viewer?.ToString()??"?"}");
            return "ROOM_FLOW_INITIAL_VIEWER_UNVERIFIED";
        }
        _host=identity.Value.Host.Username;
        if(LiveRoomHistory.TryGetActive(_host,_config.RoomCooldownMinutes,out TimeSpan cooldownAge))
        {
            _log($"ROOM_COOLDOWN_ACTIVE: Phòng @{_host} mới tương tác " +
                $"{cooldownAge.TotalMinutes:F1} phút trước, bỏ qua.");
            return "ROOM_COOLDOWN_ACTIVE";
        }
        _initialized=true;
        _roomTimer.StartIfNew(_host);
        _nextHostAudit=TimeSpan.FromSeconds(60);
        _log($"ROOM_FLOW_WATCHDOG_AUTO_START: host=@{_host}; viewers={after.Viewer.Value}; " +
            $"periodicHostAudit=60s; limitPerRoom={_config.SessionDurationSeconds}s; " +
            "mode=READ_ONLY; commentCode=UNMODIFIED");
        _log($"ROOM_FLOW_TIMER_RESET: host=@{_host}; elapsed=0s; limit={_config.SessionDurationSeconds}s");
        return null;
    }

    // Called serially before each existing Phase 5 attempt. It does not
    // enter or navigate rooms, type or send comments, or bypass restrictions.
    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        if(!_initialized)return "ROOM_FLOW_NOT_INITIALIZED";
        string xml=await _session.SourceAsync(ct);
        var state=LiveRoomUiReader.Parse(xml);
        if(state.IsMuted || state.IsAccessRestricted)
        {
            _roomTimer.Pause();
            _log($"ROOM_FLOW_RESTRICTION_CONFIRMED: host=@{_host}; notice={state.Notice}; " +
                "stop interacting with this room.");
            await SaveAsync("restriction",xml,ct);
            return "ROOM_FLOW_RESTRICTION_CONFIRMED";
        }
        if(state.IsEnded || state.IsSearchGrid || !state.IsLive)
        {
            // The UI may briefly rerender; do not infer a new room.
            await Task.Delay(600,ct);
            string secondXml=await _session.SourceAsync(ct);
            var second=LiveRoomUiReader.Parse(secondXml);
            if(second.IsLive && !second.IsSearchGrid && !second.IsEnded)return null;
            _roomTimer.Pause();
            _log($"ROOM_FLOW_TRANSITION_DETECTED: old=@{_host}; no longer verified LIVE; " +
                "stop current room actions.");
            await SaveAsync("transition-unverified",secondXml,ct);
            return "ROOM_FLOW_TRANSITION_DETECTED";
        }
        if(state.Viewer.HasValue)
        {
            _viewerMissingStreak=0;
            if(!WithinRange(state.Viewer.Value))
            {
                await Task.Delay(500,ct);
                var second=LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
                if(second.Viewer.HasValue && !WithinRange(second.Viewer.Value))
                {
                    _log($"ROOM_FLOW_VIEWERS_OUTSIDE_RANGE: host=@{_host}; " +
                        $"viewer={second.Viewer}; stop current room actions.");
                    return "ROOM_FLOW_VIEWERS_OUTSIDE_RANGE";
                }
            }
        }
        else if(++_viewerMissingStreak>=3)
        {
            await SaveAsync("viewer-unverified",xml,ct);
            return "ROOM_FLOW_VIEWER_UNVERIFIED";
        }
        if(_roomTimer.Elapsed<_nextHostAudit)return null;
        // Anchor checks to the room's clock, not to completion time; a delayed
        // Appium query must not silently shift the schedule by a full minute.
        _nextHostAudit=TimeSpan.FromSeconds(
            (Math.Floor(_roomTimer.Elapsed.TotalSeconds/60)+1)*60);
        _log($"ROOM_FLOW_PERIODIC_CHECK_START: elapsed={_roomTimer.Elapsed.TotalSeconds:F0}s; host=@{_host}");
        var identity=await ReadHostAsync(xml,ct);
        if(identity is null)
        {
            await SaveAsync("periodic-host-unverified",xml,ct);
            return "ROOM_FLOW_HOST_UNVERIFIED";
        }
        var after=identity.Value.LiveState;
        _log($"ROOM_FLOW_PERIODIC_CHECK_RESULT: host=@{identity.Value.Host.Username}; " +
            $"viewer={after.Viewer?.ToString()??"?"}; expectedHost=@{_host}");
        if(!after.IsLive || !after.Viewer.HasValue)return "ROOM_FLOW_STATE_UNVERIFIED";
        if(identity.Value.Host.Username!=_host)
        {
            // Host profile is opened twice to avoid mistaking overlays and
            // transient transitions for a room switch.
            var confirmed=await ReadHostAsync(await _session.SourceAsync(ct),ct);
            if(confirmed?.Host.Username!=identity.Value.Host.Username)
            {
                await SaveAsync("room-change-unverified",xml,ct);
                return "ROOM_FLOW_CHANGE_UNVERIFIED";
            }
            string old=_host??"?";
            _host=identity.Value.Host.Username;
            _roomTimer.StartIfNew(_host);
            _nextHostAudit=TimeSpan.FromSeconds(60);
            _log($"ROOM_FLOW_AUTO_CHANGE_CONFIRMED: old=@{old}; new=@{_host}; stableReads=2");
            _log($"ROOM_FLOW_TIMER_RESET: host=@{_host}; elapsed=0s; limit={_config.SessionDurationSeconds}s");
            _log("ROOM_FLOW_NEW_ROOM_PAUSED: a different host needs a separate accepted-room workflow; " +
                "do not continue the previous room actions automatically.");
            return "ROOM_FLOW_NEW_ROOM_PAUSED";
        }
        _log($"ROOM_FLOW_SAME_HOST: host=@{_host}; elapsed={_roomTimer.Elapsed.TotalSeconds:F0}s; no reset");
        return null;
    }
}
