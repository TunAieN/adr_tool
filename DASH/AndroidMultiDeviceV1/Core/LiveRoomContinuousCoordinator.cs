using System.Xml.Linq;

namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Owns the long-running LIVE lifecycle after Phase 1-3 accepted a room.
/// Phase 5 itself remains bounded per room; its normal time limit is the signal
/// to validate and enter another room. Only caller cancellation ends this loop.
/// </summary>
public sealed class LiveRoomContinuousCoordinator
{
    const int ConsecutiveInvalidLimit=10;
    const int SwipeSettleMilliseconds=1800;
    readonly AppiumSession _session;
    readonly LiveManagerSettings _config;
    readonly List<string> _drafts;
    readonly Func<IReadOnlyList<string>> _draftProvider;
    readonly Action<string> _log;
    readonly Action<LiveSearchCard> _onCard;
    readonly AdbClient _adb;
    readonly string _deviceSerial;
    readonly Func<CancellationToken,Task<LiveSearchReport>>? _searchAgain;
    readonly GlobalRoomSlotManager _roomSlots;
    readonly string _slotDeviceKey;
    readonly int _maxDevicesPerRoom;
    readonly Func<int>? _roomDeviceLimitProvider;
    int CurrentRoomDeviceLimit=>Math.Clamp(_roomDeviceLimitProvider?.Invoke()??_maxDevicesPerRoom,1,100);
    string? _occupiedRoomHost;
    int _swipeAttempt;
    int _fallbackUiNotCleanStreak;

    sealed record RoomEvaluation(bool Accepted,string Reason,LiveRoomFlowGuard? Guard=null);

    public LiveRoomContinuousCoordinator(AppiumSession session,LiveManagerSettings config,
        List<string> drafts,Action<string> log,Action<LiveSearchCard> onCard,
        AdbClient adb,string deviceSerial,
        Func<CancellationToken,Task<LiveSearchReport>>? searchAgain=null,
        Func<IReadOnlyList<string>>? draftProvider=null,
        GlobalRoomSlotManager? roomSlots=null,string? slotDeviceKey=null,
        int maxDevicesPerRoom=5,Func<int>? roomDeviceLimitProvider=null)
    {
        _session=session;
        _config=config;
        _drafts=drafts;
        _draftProvider=draftProvider ?? (()=>LiveManagerSettingsStore.Load().CommentDrafts);
        _log=log;
        _onCard=onCard;
        _adb=adb;
        _deviceSerial=deviceSerial;
        _searchAgain=searchAgain;
        _roomSlots=roomSlots??GlobalRoomSlotManager.Shared;
        _slotDeviceKey=string.IsNullOrWhiteSpace(slotDeviceKey)?deviceSerial:slotDeviceKey;
        _maxDevicesPerRoom=Math.Clamp(maxDevicesPerRoom,1,100);
        _roomDeviceLimitProvider=roomDeviceLimitProvider;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        int consecutiveInvalidLives=0;
        bool currentRoomWasAcceptedBySearch=true;
        LiveRoomFlowGuard? preparedGuard=null;

        _log($"LIVE_LOOP_START: limitPerRoom={_config.SessionDurationSeconds}s; " +
            $"invalidLimit={ConsecutiveInvalidLimit}; roomSlotLimit={CurrentRoomDeviceLimit}; " +
            "roomSlotTtl=10m; stopCondition=USER_CANCELLATION_ONLY");

        try
        {
            while(true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                if(currentRoomWasAcceptedBySearch)
                {
                    var guard=preparedGuard ?? new LiveRoomFlowGuard(_session,_config,_log);
                    if(preparedGuard is null)
                    {
                        string? preflight=await guard.InitializeAsync(ct);
                        if(preflight is not null)
                        {
                            consecutiveInvalidLives++;
                            currentRoomWasAcceptedBySearch=false;
                            _log($"LIVE_REJECTED: reason={preflight}; source=ROOM_PREFLIGHT");
                            LogInvalidStreak(consecutiveInvalidLives);
                        }
                    }

                    if(currentRoomWasAcceptedBySearch)
                    {
                        if(!TryAcquireRoomSlot(guard))
                        {
                            currentRoomWasAcceptedBySearch=false;
                            preparedGuard=null;
                        }
                    }

                    if(currentRoomWasAcceptedBySearch)
                    {
                        preparedGuard=null;
                        consecutiveInvalidLives=0;
                        LiveCommentAutoSendResult phase5;
                        try
                        {
                            phase5=await LiveCommentAutoSender.ExecuteAsync(
                                _session,_drafts,_config.SessionDurationSeconds,_log,ct,
                                token=>CheckRoomAndHeartbeatAsync(guard,token),
                                ()=>RecordVisitedRoom(guard,"COMMENT_SENT"),
                                _draftProvider);
                        }
                        finally {ReleaseCurrentRoomSlot("PHASE5_EXIT");}

                        currentRoomWasAcceptedBySearch=false;
                        RecordVisitedRoom(guard,"ROOM_EXIT_"+phase5.FinishReason);
                        if(phase5.FinishReason=="AUTO_END_TIME_REACHED")
                        {
                            _log($"SWIPE_TO_NEXT_LIVE_TRIGGER: reason={phase5.FinishReason}; " +
                                $"sent={phase5.SentCount}; complete=false");
                        }
                        else if(phase5.FinishReason=="ROOM_MUTED_CONFIRMED")
                        {
                            _log("ROOM_MUTED_SWIPE_TRIGGER: stop Phase 5 immediately; " +
                                "swipe to next LIVE; invalidStreak unchanged.");
                        }
                        else
                        {
                            consecutiveInvalidLives++;
                            _log($"LIVE_REJECTED: reason={phase5.FinishReason}; source=PHASE5_ROOM_GUARD");
                            LogInvalidStreak(consecutiveInvalidLives);
                        }
                    }
                }

                if(consecutiveInvalidLives>=ConsecutiveInvalidLimit)
                {
                    ReleaseCurrentRoomSlot("KEYWORD_SEARCH_FALLBACK");
                    bool searchAccepted=await ReturnToKeywordSearchAsync(ct);
                    if(searchAccepted)
                    {
                        consecutiveInvalidLives=0;
                        currentRoomWasAcceptedBySearch=true;
                        preparedGuard=null;
                        _log("KEYWORD_SEARCH_FALLBACK_ACCEPTED: phòng mới đã qua Phase 1-3; tiếp tục vòng LIVE.");
                    }
                    else
                    {
                        // A bounded search attempt may legitimately find no room.
                        // Keep retrying until Stop, without spinning tightly.
                        _log("KEYWORD_SEARCH_FALLBACK_RETRY: chưa có LIVE đạt điều kiện; chờ rồi tìm lại.");
                        await Task.Delay(1500,ct);
                    }
                    continue;
                }

                ReleaseCurrentRoomSlot("SWIPE_TO_NEXT_LIVE");
                RoomEvaluation next=await SwipeAndEvaluateAsync(ct);
                if(next.Accepted)
                {
                    consecutiveInvalidLives=0;
                    currentRoomWasAcceptedBySearch=true;
                    preparedGuard=next.Guard;
                    _log("LIVE_ACCEPTED_AFTER_SWIPE: INVALID_STREAK reset=0/10; " +
                        $"roomTimer={_config.SessionDurationSeconds}s");
                }
                else
                {
                    consecutiveInvalidLives++;
                    _log($"LIVE_REJECTED: reason={next.Reason}; source=SWIPE_EVALUATION");
                    LogInvalidStreak(consecutiveInvalidLives);
                }
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested)
                {
                    _log("LIVE_LOOP_STOP_REQUESTED: cancellation observed; no further UI action.");
                    throw;
                }
                catch(TikTokNavigationCheckpointException)
                {
                    ReleaseCurrentRoomSlot("FIND_PEOPLE_CHECKPOINT");
                    currentRoomWasAcceptedBySearch=false;
                    preparedGuard=null;
                    _log("NAV_RECOVERY_CHECKPOINT: stale room discarded; resume=PHASE1_SEARCH; invalidStreak=UNCHANGED");
                    bool accepted=await ReturnToKeywordSearchAsync(ct);
                    if(accepted)
                    {
                        consecutiveInvalidLives=0;
                        currentRoomWasAcceptedBySearch=true;
                        _log("WORKFLOW_RESUMED: phase=LIVE_SEARCH_ACCEPTED; reason=FIND_PEOPLE");
                    }
                    else
                    {
                        consecutiveInvalidLives=ConsecutiveInvalidLimit;
                        _log("NAV_RECOVERY_CHECKPOINT_RETRY: search not accepted; retry with bounded delay");
                        await Task.Delay(1500,ct);
                    }
                    continue;
                }
                catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex))
                {
                    _log($"LIVE_LOOP_FATAL_SESSION: {ex.GetType().Name}: {ex.Message}; " +
                        "recovery=SKIPPED; invalidStreak=UNCHANGED; workflow=TERMINATED");
                    throw;
                }
                catch(Exception ex)
                {
                // Isolation is deliberately inside the infinite loop: one slow
                // or stale Appium response must not tear down the coordinator.
                ReleaseCurrentRoomSlot("LOOP_EXCEPTION");
                _log($"LIVE_LOOP_APPIUM_WARNING: {ex.GetType().Name}: {ex.Message}; " +
                    "recover UI and attempt another room.");
                currentRoomWasAcceptedBySearch=false;
                preparedGuard=null;
                try
                {
                    await RecoverLiveUiAsync(ct);
                    RoomEvaluation recovered=await SwipeAndEvaluateAsync(ct);
                    if(recovered.Accepted)
                    {
                        consecutiveInvalidLives=0;
                        currentRoomWasAcceptedBySearch=true;
                        preparedGuard=recovered.Guard;
                        _log("LIVE_ACCEPTED_AFTER_RECOVERY: INVALID_STREAK reset=0/10.");
                    }
                    else
                    {
                        consecutiveInvalidLives++;
                        _log($"LIVE_REJECTED: reason={recovered.Reason}; source=EXCEPTION_RECOVERY");
                        LogInvalidStreak(consecutiveInvalidLives);
                    }
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested)
                {
                    _log("LIVE_LOOP_STOP_REQUESTED: cancellation observed during recovery.");
                    throw;
                }
                catch(Exception recoveryEx) when(
                    AppiumFailureClassifier.IsFatalSessionFailure(recoveryEx))
                {
                    _log($"LIVE_LOOP_FATAL_SESSION: {recoveryEx.GetType().Name}: " +
                        $"{recoveryEx.Message}; recovery=ABORTED; invalidStreak=UNCHANGED; " +
                        "workflow=TERMINATED");
                    throw;
                }
                catch(Exception recoveryEx)
                {
                    consecutiveInvalidLives++;
                    _log($"LIVE_LOOP_RECOVERY_WARNING: {recoveryEx.GetType().Name}: " +
                        $"{recoveryEx.Message}");
                    LogInvalidStreak(consecutiveInvalidLives);
                    await Task.Delay(1000,ct);
                }
                }
            }
        }
        finally {ReleaseCurrentRoomSlot("COORDINATOR_EXIT");}
    }

    void LogInvalidStreak(int value) =>
        _log($"INVALID_STREAK: {Math.Min(value,ConsecutiveInvalidLimit)}/{ConsecutiveInvalidLimit}");

    bool TryAcquireRoomSlot(LiveRoomFlowGuard guard)
    {
        string host=LiveRoomHistory.Normalize(guard.HostUsername??string.Empty);
        if(host.Length==0)
        {
            _log("ROOM_SLOT_REJECTED: host username chưa được xác minh; không chạy Phase 5.");
            return false;
        }
        RoomSlotDecision decision=_roomSlots.TryAcquire(
            host,_slotDeviceKey,CurrentRoomDeviceLimit);
        if(!decision.Acquired)
        {
            _log($"ROOM_SLOT_EXCEEDED: Phòng @{host} đã đủ giới hạn " +
                $"{decision.Limit} thiết bị; active={decision.ActiveCount}; tiến hành chuyển lướt.");
            return false;
        }
        _occupiedRoomHost=host;
        _log($"ROOM_SLOT_ACQUIRED: host=@{host}; active={decision.ActiveCount}/" +
            $"{decision.Limit}; device={_slotDeviceKey}.");
        return true;
    }

    async Task<string?> CheckRoomAndHeartbeatAsync(LiveRoomFlowGuard guard,CancellationToken ct)
    {
        string? result=await guard.CheckAsync(ct);
        if(result is not null)return result;
        string host=_occupiedRoomHost??LiveRoomHistory.Normalize(guard.HostUsername??string.Empty);
        if(host.Length==0 || !_roomSlots.Heartbeat(host,_slotDeviceKey))
        {
            _log($"ROOM_SLOT_HEARTBEAT_LOST: host=@{host}; stop current Phase 5 and change room.");
            return "ROOM_SLOT_LEASE_EXPIRED";
        }
        return null;
    }

    void ReleaseCurrentRoomSlot(string reason)
    {
        RoomSlotRelease release=_roomSlots.Release(_slotDeviceKey);
        _occupiedRoomHost=null;
        if(release.Released)
            _log($"ROOM_SLOT_RELEASED: host=@{release.Host}; remaining={release.RemainingCount}; " +
                $"reason={reason}.");
    }

    void RecordVisitedRoom(LiveRoomFlowGuard guard,string reason)
    {
        string? host=guard.HostUsername;
        if(string.IsNullOrWhiteSpace(host))return;
        LiveRoomHistory.MarkVisited(host,_config.RoomCooldownMinutes);
        _log($"ROOM_COOLDOWN_RECORDED: host=@{LiveRoomHistory.Normalize(host)}; " +
            $"reason={reason}; cooldown={_config.RoomCooldownMinutes}m.");
    }

    async Task<RoomEvaluation> SwipeAndEvaluateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string beforeXml=await _session.SourceAsync(ct);
        await RestoreFindPeopleCheckpointAsync(beforeXml,ct,"LIVE_SWIPE");
        var before=LiveRoomUiReader.Parse(beforeXml);
        if(IsEndedSurface(before,beforeXml))
        {
            string afterEndedSwipe=await SwipePastEndedRoomAsync(beforeXml,ct);
            return await EvaluateCurrentRoomAsync(ct,afterEndedSwipe);
        }

        await CloseKeyboardBestEffortAsync(ct);
        if(!before.IsLive || before.IsSearchGrid)
        {
            before=await RecoverLiveUiAsync(ct);
            if(!before.IsLive || before.IsSearchGrid)
                return new RoomEvaluation(false,"LIVE_SURFACE_UNVERIFIED_BEFORE_SWIPE");
        }

        int attempt=Interlocked.Increment(ref _swipeAttempt);
        _log($"SWIPE_ATTEMPT: {attempt}; direction=UP; gesture=W3C_TOUCH_FLICK; " +
            "durationMs=180; xPercent=85");
        await _session.SwipeLiveRoomUpAsync(ct);
        await Task.Delay(SwipeSettleMilliseconds,ct);
        return await EvaluateCurrentRoomAsync(ct);
    }

    async Task<RoomEvaluation> EvaluateCurrentRoomAsync(CancellationToken ct,string? initialXml=null)
    {
        string xml=initialXml??await _session.SourceAsync(ct);
        await RestoreFindPeopleCheckpointAsync(xml,ct,"LIVE_EVALUATION");
        if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(_session,_log,ct,xml))
            xml=await _session.SourceAsync(ct);
        var captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
            _session,_log,ct,xml,"SWIPE_EVALUATION");
        if(captcha.Status==CaptchaDismissStatus.Failed)
            return new RoomEvaluation(false,"CAPTCHA_DISMISS_FAILED");
        xml=captcha.Source;
        xml=await DismissFunctionMenuIfPresentAsync(xml,ct);
        var state=LiveRoomUiReader.Parse(xml);
        if(IsEndedSurface(state,xml))
        {
            xml=await SwipePastEndedRoomAsync(xml,ct);
            if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(_session,_log,ct,xml))
                xml=await _session.SourceAsync(ct);
            captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                _session,_log,ct,xml,"SWIPE_EVALUATION");
            if(captcha.Status==CaptchaDismissStatus.Failed)
                return new RoomEvaluation(false,"CAPTCHA_DISMISS_FAILED");
            xml=captcha.Source;
            xml=await DismissFunctionMenuIfPresentAsync(xml,ct);
            state=LiveRoomUiReader.Parse(xml);
            if(IsEndedSurface(state,xml))
                return new RoomEvaluation(false,"LIVE_ENDED_AFTER_DIRECT_SWIPE");
        }
        if(!state.IsLive || state.IsSearchGrid || !state.Viewer.HasValue)
            state=await RecoverLiveUiAsync(ct);

        if(state.IsMuted || state.IsAccessRestricted)
            return new RoomEvaluation(false,"LIVE_RESTRICTION_CONFIRMED");
        if(!state.IsLive || state.IsSearchGrid)
            return new RoomEvaluation(false,"LIVE_UI_UNVERIFIED_AFTER_RECOVERY");
        if(!state.Viewer.HasValue)
            return new RoomEvaluation(false,"VIEWER_UNVERIFIED_AFTER_RECOVERY");

        bool viewerAccepted=state.Viewer.Value>=_config.MinimumViewers &&
            (_config.MaximumViewers==0 || state.Viewer.Value<=_config.MaximumViewers);
        if(!viewerAccepted)
        {
            _log($"LIVE_VIEWER_REJECTED: viewer={state.Viewer.Value}; " +
                $"range=[{_config.MinimumViewers},{(_config.MaximumViewers==0?"INF":_config.MaximumViewers.ToString())}]");
            return new RoomEvaluation(false,"VIEWER_OUTSIDE_RANGE");
        }

        // This reuses the same k1s/k13 -> user_name -> q2e/q16 verification
        // already protecting the existing Phase 5 handoff.
        var guard=new LiveRoomFlowGuard(_session,_config,_log);
        string? validation=await guard.InitializeAsync(ct);
        if(validation is not null)
        {
            string currentXml=await _session.SourceAsync(ct);
            var currentState=LiveRoomUiReader.Parse(currentXml);
            if(IsEndedSurface(currentState,currentXml))
            {
                await SwipePastEndedRoomAsync(currentXml,ct);
                return new RoomEvaluation(false,"LIVE_ENDED_DURING_HOST_VALIDATION");
            }
            await RecoverLiveUiAsync(ct);
            return new RoomEvaluation(false,validation);
        }

        _log($"LIVE_ROOM_VALIDATED: viewer={state.Viewer.Value}; hostProfile=VERIFIED; " +
            "INVALID_STREAK will reset.");
        return new RoomEvaluation(true,"LIVE_ACCEPTED",guard);
    }

    static bool HasFunctionMenu(string xml)
    {
        try
        {
            var matches=XDocument.Parse(xml).Descendants().Where(node=>
            {
                if(string.Equals((string?)node.Attribute("displayed"),"false",
                    StringComparison.OrdinalIgnoreCase))return false;
                string text=((string?)node.Attribute("text")??"").Trim();
                string description=((string?)node.Attribute("content-desc")??"").Trim();
                return text is "Translate" or "Copy to send" or "Report" ||
                    description is "Translate" or "Copy to send" or "Report";
            }).ToArray();
            if(matches.Any(node=>
                string.Equals((string?)node.Attribute("text"),"Copy to send",StringComparison.Ordinal) ||
                string.Equals((string?)node.Attribute("content-desc"),"Copy to send",StringComparison.Ordinal)))
                return true;
            // Translate/Report are less distinctive in chat content. Require
            // both menu actions to avoid tapping because of one ordinary comment.
            bool translate=matches.Any(node=>
                string.Equals((string?)node.Attribute("text"),"Translate",StringComparison.Ordinal) ||
                string.Equals((string?)node.Attribute("content-desc"),"Translate",StringComparison.Ordinal));
            bool report=matches.Any(node=>
                string.Equals((string?)node.Attribute("text"),"Report",StringComparison.Ordinal) ||
                string.Equals((string?)node.Attribute("content-desc"),"Report",StringComparison.Ordinal));
            return translate && report;
        }
        catch(System.Xml.XmlException){return false;}
    }

    async Task<string> DismissFunctionMenuIfPresentAsync(string xml,CancellationToken ct)
    {
        if(!HasFunctionMenu(xml))return xml;
        ct.ThrowIfCancellationRequested();
        var (x,y,width,height)=await _session.WindowRectAsync(ct);
        int tapX=x+Math.Clamp(50,12,Math.Max(12,width/4));
        int tapY=y+Math.Clamp(150,70,Math.Max(70,height/4));
        _log($"FUNCTION_MENU_DETECTED: actions=Translate|Copy to send|Report; " +
            $"cleanupTap=({tapX},{tapY}).");
        _log($"UI_RECOVERY_TAP: x={tapX}; y={tapY}; zone=TOP_EDGE; purpose=DISMISS_FUNCTION_MENU");
        await _session.TapUiPointAsync(tapX,tapY,ct);
        await Task.Delay(500,ct);
        string after=await _session.SourceAsync(ct);
        _log($"FUNCTION_MENU_DISMISSED: confirmed={!HasFunctionMenu(after)}; continue=SWIPE_EVALUATION.");
        return after;
    }

    async Task RestoreFindPeopleCheckpointAsync(string xml,CancellationToken ct,string phase)
    {
        if(!TikTokNavigationRecovery.IsFindPeopleScreen(xml))return;
        var nav=new TikTokNavigationRecovery(_session,_adb,_deviceSerial,_log);
        if(!await nav.RecoverFindPeopleAsync(ct,phase))
            throw new InvalidOperationException("FIND_PEOPLE_RECOVERY_FAILED: cannot resume safe LIVE checkpoint.");
        // Do NOT swipe from the former room or trust old element coordinates.
        // The coordinator owns the safe Phase 1 restart after this signal.
        throw new TikTokNavigationCheckpointException();
    }

    async Task<LiveRoomUiState> RecoverLiveUiAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string beforeRecoveryXml=await _session.SourceAsync(ct);
        await RestoreFindPeopleCheckpointAsync(beforeRecoveryXml,ct,"LIVE_UI_RECOVERY");
        var beforeRecovery=LiveRoomUiReader.Parse(beforeRecoveryXml);
        if(IsEndedSurface(beforeRecovery,beforeRecoveryXml))
        {
            string afterEndedSwipe=await SwipePastEndedRoomAsync(beforeRecoveryXml,ct);
            return LiveRoomUiReader.Parse(afterEndedSwipe);
        }

        var (x,y,width,height)=await _session.WindowRectAsync(ct);
        // Keep recovery away from the center/avatar/follow controls.  The
        // point remains inside the app viewport on small and large devices.
        int tapX=x+Math.Clamp(50,12,Math.Max(12,width/4));
        int tapY=y+Math.Clamp(150,70,Math.Max(70,height/4));
        _log($"UI_RECOVERY_TAP: x={tapX}; y={tapY}; zone=TOP_EDGE; purpose=DISMISS_OVERLAY");
        await _session.TapUiPointAsync(tapX,tapY,ct);
        await Task.Delay(450,ct);

        string afterTapXml=await _session.SourceAsync(ct);
        var afterTap=LiveRoomUiReader.Parse(afterTapXml);
        if(afterTap.IsLive && !afterTap.IsSearchGrid && afterTap.Viewer.HasValue)
            return afterTap;

        _log("UI_RECOVERY_BACK: attempt=1/1; reason=LIVE_NODES_STILL_UNAVAILABLE");
        await _session.BackAsync(ct);
        await Task.Delay(650,ct);
        return LiveRoomUiReader.Parse(await _session.SourceAsync(ct));
    }

    static bool IsEndedSurface(LiveRoomUiState state,string xml) =>
        state.IsEnded ||
        xml.Contains("LIVE has ended",StringComparison.OrdinalIgnoreCase) ||
        xml.Contains("This LIVE has ended",StringComparison.OrdinalIgnoreCase) ||
        xml.Contains("LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase) ||
        xml.Contains("Buổi LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase) ||
        xml.Contains("Phiên LIVE đã kết thúc",StringComparison.OrdinalIgnoreCase);

    async Task<string> SwipePastEndedRoomAsync(string xml,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _log("LIVE_ENDED_DETECTED: bỏ qua UI_RECOVERY_TAP/UI_RECOVERY_BACK; " +
            "swipe trực tiếp sang phòng kế tiếp.");
        int attempt=Interlocked.Increment(ref _swipeAttempt);
        _log($"SWIPE_ATTEMPT: {attempt}; direction=UP; gesture=W3C_TOUCH_FLICK; " +
            "durationMs=180; xPercent=85; reason=LIVE_ENDED");
        await _session.SwipeLiveRoomUpAsync(ct);
        await Task.Delay(SwipeSettleMilliseconds,ct);
        return await _session.SourceAsync(ct);
    }

    async Task CloseKeyboardBestEffortAsync(CancellationToken ct)
    {
        try
        {
            bool requested=await _session.HideKeyboardAsync(ct);
            if(requested)_log("SWIPE_PREP_KEYBOARD_CLOSED: requested=true");
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex) when(AppiumFailureClassifier.IsFatalSessionFailure(ex)){throw;}
        catch(Exception ex)
        {
            _log($"SWIPE_PREP_KEYBOARD_WARNING: {ex.GetType().Name}; continue with verified LIVE UI.");
        }
    }

    async Task<bool> ReturnToKeywordSearchAsync(CancellationToken ct)
    {
        _log("KEYWORD_SEARCH_FALLBACK: INVALID_STREAK=10/10; cleanly exit LIVE before Phase 1-2.");
        bool clean=false;
        for(int back=0;back<=4;back++)
        {
            ct.ThrowIfCancellationRequested();
            string xml=await _session.SourceAsync(ct);
            if(TikTokNavigationRecovery.IsFindPeopleScreen(xml))
                await RestoreFindPeopleCheckpointAsync(xml,ct,"KEYWORD_FALLBACK");
            var state=LiveRoomUiReader.Parse(xml);
            bool liveSurface=state.IsLive || HasAny(xml,
                "com.ss.android.ugc.trill:id/tv_online_audience_num",
                "com.ss.android.ugc.trill:id/k1s",
                "com.ss.android.ugc.trill:id/k13");
            bool profileOverlay=!state.IsSearchGrid && HasAny(xml,
                "com.ss.android.ugc.trill:id/user_name",
                "com.ss.android.ugc.trill:id/q2e",
                "com.ss.android.ugc.trill:id/q16");

            bool cleanSearchOrHome=IsSearchOrHomeSurface(xml,state);
            if(cleanSearchOrHome && !liveSurface && !profileOverlay)
            {
                clean=true;
                _fallbackUiNotCleanStreak=0;
                _log($"FALLBACK_UI_CLEAN: backCount={back}; " +
                    $"destination={(state.IsSearchGrid?"SEARCH_GRID":"SEARCH_OR_HOME")}");
                break;
            }
            // Back is allowed only while fresh XML still proves a LIVE or its
            // host-profile overlay. Do not Back blindly from login/OAuth/Home.
            if(back==4 || (!liveSurface && !profileOverlay))
            {
                _log($"FALLBACK_BACK_BLOCKED: backCount={back}; liveSurface={liveSurface}; " +
                    $"profileOverlay={profileOverlay}; cleanSearchOrHome={cleanSearchOrHome}");
                break;
            }

            _log($"FALLBACK_ANDROID_BACK: {back+1}/4; " +
                $"liveSurface={liveSurface}; profileOverlay={profileOverlay}");
            await _session.BackAsync(ct);
            await Task.Delay(700,ct);
        }

        if(!clean)
        {
            _fallbackUiNotCleanStreak++;
            _log($"FALLBACK_UI_NOT_CLEAN: streak={_fallbackUiNotCleanStreak}/5; " +
                "còn dấu hiệu LIVE/profile sau chuỗi Back có điều kiện.");
            if(_fallbackUiNotCleanStreak<5)return false;

            _log("FALLBACK_FORCE_EXIT: dirtyStreak=5/5; bypassUiGuards=true; " +
                "AndroidBack=3; delayMs=500.");
            for(int forceBack=1;forceBack<=3;forceBack++)
            {
                ct.ThrowIfCancellationRequested();
                await _session.BackAsync(ct);
                _log($"FALLBACK_FORCE_BACK: {forceBack}/3");
                if(forceBack<3)await Task.Delay(500,ct);
            }
            await Task.Delay(500,ct);
            string foreground=await _session.CurrentPackageAsync(ct);
            if(!foreground.Equals("com.ss.android.ugc.trill",StringComparison.OrdinalIgnoreCase))
            {
                _log($"FALLBACK_FORCE_FOREGROUND_RECOVERY: currentPackage={foreground}; launchTikTok=true.");
                await _adb.LaunchTikTokAsync(_deviceSerial,ct);
                await Task.Delay(1800,ct);
            }
            _fallbackUiNotCleanStreak=0;
            clean=true;
            _log("FALLBACK_FORCE_EXIT_COMPLETE: stateReset=true; startKeywordQueue=true.");
        }

        ct.ThrowIfCancellationRequested();
        LiveSearchReport report;
        if(_searchAgain is not null)
        {
            _log("KEYWORD_SEARCH_FALLBACK_QUEUE: delegate=AUTONOMOUS_KEYWORD_CYCLE.");
            report=await _searchAgain(ct);
        }
        else
        {
            var engine=new LiveSearchEngine(_session,_config,_log,_onCard,_adb,_deviceSerial);
            report=await engine.RunAsync(ct);
        }
        bool accepted=report.FinishReason.Contains("LIVE_ACCEPTED",StringComparison.OrdinalIgnoreCase);
        _log($"KEYWORD_SEARCH_FALLBACK_RESULT: accepted={accepted}; reason={report.FinishReason}");
        return accepted;
    }

    static bool HasAny(string xml,params string[] values) =>
        values.Any(value=>xml.Contains(value,StringComparison.Ordinal));

    static bool IsSearchOrHomeSurface(string xml,LiveRoomUiState state) =>
        state.IsSearchGrid ||
        HasAny(xml,
            "resource-id=\"com.ss.android.ugc.trill:id/ka6\"",
            "resource-id=\"com.ss.android.ugc.trill:id/k_g\"",
            "resource-id=\"com.ss.android.ugc.trill:id/hwj\"",
            "resource-id=\"com.ss.android.ugc.trill:id/hvp\"",
            "content-desc=\"Home\"",
            "content-desc=\"Trang chủ\"",
            "text=\"Home\"",
            "text=\"Trang chủ\"");
}
