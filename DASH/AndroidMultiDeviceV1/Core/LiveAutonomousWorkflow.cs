namespace AndroidMultiDeviceV1.Core;

public sealed class LiveKeywordLruQueue
{
    sealed class Entry(string keyword,int order)
    {
        public string Keyword { get; }=keyword;
        public int Order { get; }=order;
        public DateTime LastUsed { get; set; }=DateTime.MinValue;
    }

    readonly object _gate=new();
    readonly List<Entry> _entries;

    public LiveKeywordLruQueue(IEnumerable<string> keywords)
    {
        _entries=keywords.Where(k=>!string.IsNullOrWhiteSpace(k))
            .Select((k,index)=>new Entry(k.Trim(),index)).ToList();
        if(_entries.Count==0)throw new InvalidOperationException("Keyword Queue trống.");
    }

    public int Count => _entries.Count;

    public (string Keyword,DateTime PreviousLastUsed) TakeLeastRecentlyUsed()
    {
        lock(_gate)
        {
            Entry selected=_entries.OrderBy(e=>e.LastUsed).ThenBy(e=>e.Order).First();
            DateTime previous=selected.LastUsed;
            selected.LastUsed=DateTime.Now;
            return (selected.Keyword,previous);
        }
    }
}

/// <summary>
/// Owns keyword rotation and bounded grid refreshes for one Appium session.
/// It returns only after Phase 3 accepts a room; cancellation and technical
/// failures are deliberately allowed to escape to the session supervisor.
/// </summary>
public sealed class LiveSearchCycle
{
    readonly AppiumSession _session;
    readonly LiveManagerSettings _config;
    readonly Action<string> _log;
    readonly Action<LiveSearchCard> _onCard;
    readonly AdbClient _adb;
    readonly string _deviceSerial;
    readonly LiveKeywordLruQueue _keywordQueue;

    public LiveSearchCycle(AppiumSession session,LiveManagerSettings config,
        Action<string> log,Action<LiveSearchCard> onCard,AdbClient adb,string deviceSerial,
        LiveKeywordLruQueue keywordQueue)
    {
        _session=session;
        _config=config;
        _log=log;
        _onCard=onCard;
        _adb=adb;
        _deviceSerial=deviceSerial;
        _keywordQueue=keywordQueue;
    }

    public async Task<LiveSearchReport> FindAcceptedRoomAsync(CancellationToken ct)
    {
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            var selection=_keywordQueue.TakeLeastRecentlyUsed();
            string keyword=selection.Keyword;
            string previous=selection.PreviousLastUsed==DateTime.MinValue
                ? "NEVER"
                : selection.PreviousLastUsed.ToString("yyyy-MM-dd HH:mm:ss");
            _log($"KEYWORD_LRU_SELECTED: keyword=“{keyword}”; previousLastUsed={previous}; " +
                $"queueSize={_keywordQueue.Count}; LastUsed updated=NOW.");
            if(_config.LiveSourceMode=="ChromeTikTokWeb")
            {
                var webSource=new TikTokChromeLiveSource(_config,_log);
                var webCandidates=await webSource.FindCandidatesAsync(keyword,ct);
                if(webCandidates.Count==0)
                {
                    _log($"CHROME_CDP_EMPTY: keyword=“{keyword}”; không có LIVE đạt ngưỡng SVG người; " +
                        "next=LEAST_RECENTLY_USED.");
                    await Task.Delay(1200,ct);
                    continue;
                }
                _log($"CHROME_CDP_QUEUE_START: sourceKeyword=“{keyword}”; " +
                    $"items={webCandidates.Count}; androidMode=SEARCH_USERNAME_PICK_CARD_1.");
                for(int index=0;index<webCandidates.Count;index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var candidate=webCandidates[index];
                    int rank=index+1;
                    _onCard(new LiveSearchCard(keyword,candidate.Title,candidate.Username,
                        candidate.Viewers,"ChromeWebVerified",
                        "chrome-web|"+candidate.Username.ToLowerInvariant(),
                        $"CDP queue {rank}/{webCandidates.Count}; Viewer từ SVG người; " +
                        "Android tìm username và chọn card 1"));
                    _log($"CHROME_CDP_QUEUE_ITEM: rank={rank}/{webCandidates.Count}; " +
                        $"host=@{candidate.Username}; webViewer={candidate.Viewers:N0}; " +
                        "androidPickCard=1; scanMoreCards=false.");
                    var report=await RunEngineWithRecoveryAsync(keyword,candidate.Username,
                        candidate.Username,allowGridRefresh:false,ct);
                    if(IsAccepted(report))return report;
                    _log($"CHROME_CDP_QUEUE_NEXT: rejected=@{candidate.Username}; " +
                        $"reason={Compact(report.FinishReason)}; remaining={webCandidates.Count-rank}.");
                    await Task.Delay(700,ct);
                }
                _log($"CHROME_CDP_QUEUE_EXHAUSTED: sourceKeyword=“{keyword}”; " +
                    $"tried={webCandidates.Count}; next=LEAST_RECENTLY_USED.");
            }
            else
            {
                var report=await RunEngineWithRecoveryAsync(keyword,keyword,null,
                    allowGridRefresh:true,ct);
                if(IsAccepted(report))return report;
                _log($"KEYWORD_LRU_ROTATE: exhausted=“{keyword}”; " +
                    $"reason={Compact(report.FinishReason)}; next=LEAST_RECENTLY_USED.");
            }
            await Task.Delay(1200,ct);
        }
    }

    static bool IsAccepted(LiveSearchReport report)=>
        report.FinishReason.Contains("LIVE_ACCEPTED",StringComparison.OrdinalIgnoreCase);

    async Task<LiveSearchReport> RunEngineWithRecoveryAsync(string sourceKeyword,
        string engineKeyword,string? expectedHostUsername,bool allowGridRefresh,CancellationToken ct)
    {
        var engine=new LiveSearchEngine(_session,_config,_log,_onCard,_adb,_deviceSerial);
        LiveSearchReport report=await engine.RunAsync(ct,engineKeyword,expectedHostUsername);
        if(IsAccepted(report))return report;
        if(report.FinishReason.Contains("FIND_PEOPLE_RECOVERY_FAILED",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FIND_PEOPLE_RECOVERY_FAILED: verified LIVE checkpoint unavailable.");

        if(RequiresAppRestart(report))
        {
            _log($"FALLBACK_APP_RESTART: keyword=“{sourceKeyword}”; engineKeyword=“{engineKeyword}”; " +
                $"reason={Compact(report.FinishReason)}; rotateKeyword=false.");
            if(!await RestartTikTokForProtectedSearchAsync(ct))
                throw new InvalidOperationException(
                    "FALLBACK_APP_RESTART_FAILED: TikTok UI vẫn bị chặn sau restart; không xoay từ khóa.");
            _log($"FALLBACK_APP_RESTART_SUCCESS: engineKeyword=“{engineKeyword}”; " +
                "resume=SAME_QUEUE_ITEM; rotateKeyword=false.");
            var retryEngine=new LiveSearchEngine(
                _session,_config,_log,_onCard,_adb,_deviceSerial);
            report=await retryEngine.RunAsync(ct,engineKeyword,expectedHostUsername);
            if(IsAccepted(report))return report;
            if(RequiresAppRestart(report))
                throw new InvalidOperationException(
                    "FALLBACK_APP_RESTART_RECHECK_FAILED: protected CAPTCHA/security UI returned; không xoay từ khóa.");
        }

        if(allowGridRefresh && RequiresGridRefresh(report))
        {
            bool refreshed=await TryRefreshGridAsync(ct);
            _log($"GRID_REFRESH_RESULT: keyword=“{sourceKeyword}”; refreshed={refreshed}; " +
                "action=RESCAN_PHASE1_SAME_KEYWORD.");
            if(refreshed)
            {
                var retryEngine=new LiveSearchEngine(
                    _session,_config,_log,_onCard,_adb,_deviceSerial);
                report=await retryEngine.RunAsync(ct,engineKeyword,expectedHostUsername);
            }
        }
        return report;
    }

    static bool RequiresGridRefresh(LiveSearchReport report)
    {
        string reason=report.FinishReason;
        return reason.Contains("Vị trí card XML không khớp",StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Không định vị chắc chắn card",StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Không có LIVE nào được xác minh",StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("ALL_CANDIDATES_EXHAUSTED",StringComparison.OrdinalIgnoreCase);
    }

    static bool RequiresAppRestart(LiveSearchReport report)
    {
        string reason=report.FinishReason;
        return reason.Contains("LOGIN_OR_SECURITY_OR_ONBOARDING",StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("CAPTCHA_DISMISS_FAILED",StringComparison.OrdinalIgnoreCase);
    }

    async Task<bool> RestartTikTokForProtectedSearchAsync(CancellationToken ct)
    {
        const string package=TikTokForegroundProbe.TikTokPackage;
        ct.ThrowIfCancellationRequested();
        await _adb.RunAsync(ct,"-s",_deviceSerial,"shell","am","force-stop",package);
        await Task.Delay(2000,ct);
        string launch=await _adb.LaunchTikTokAsync(_deviceSerial,ct);
        if(launch.Contains("Events injected: 0",StringComparison.OrdinalIgnoreCase) ||
           launch.Contains("monkey aborted",StringComparison.OrdinalIgnoreCase))
        {
            _log("FALLBACK_APP_RESTART_FAILED: monkey did not launch TikTok.");
            return false;
        }

        for(int check=1;check<=15;check++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(check==1?1500:1000,ct);
            string foreground=await _session.CurrentPackageAsync(ct);
            string source=await _session.SourceAsync(ct);
            var captcha=await LivePopupHandler.DismissVerifyCaptchaAsync(
                _session,_log,ct,source,"FALLBACK_APP_RESTART");
            if(captcha.Status==CaptchaDismissStatus.Failed)
            {
                _log("FALLBACK_APP_RESTART_FAILED: captcha vẫn tồn tại sau click X/relaunch.");
                return false;
            }
            source=captcha.Source;
            if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(_session,_log,ct,source))
                continue;
            if(foreground==package && TikTokForegroundProbe.SourceHasTikTokUi(source) &&
               !LiveSearchEngine.IsSearchRecoveryProtectedUi(source))
            {
                _log($"FALLBACK_APP_RESTART_READY: check={check}/15; TikTok UI clean=true.");
                return true;
            }
        }
        _log("FALLBACK_APP_RESTART_FAILED: timeout waiting for clean TikTok Home/Search UI.");
        return false;
    }

    async Task<bool> TryRefreshGridAsync(CancellationToken ct)
    {
        string xml=await _session.SourceAsync(ct);
        if(!LiveSearchEngine.IsLiveSearchGridXml(xml))
        {
            _log("GRID_REFRESH_SKIPPED: current UI is not a verified LIVE search grid.");
            return false;
        }
        _log("GRID_REFRESH_SWIPE_DOWN: verifiedGrid=true; direction=DOWN; attempt=1/1.");
        await _session.SwipeLiveSearchDownAsync(ct,Math.Min(65,_config.SearchSwipePercent+20));
        await Task.Delay(Math.Max(1500,_config.SearchAfterSwipeWaitMilliseconds),ct);
        string refreshed=await _session.SourceAsync(ct);
        return LiveSearchEngine.IsLiveSearchGridXml(refreshed);
    }

    static string Compact(string value)
    {
        value=value.Replace('\r',' ').Replace('\n',' ').Trim();
        return value.Length<=180?value:value[..180];
    }
}

/// <summary>
/// Supervises the complete 24/7 workflow. A failed Appium session is never
/// recovered inside a hot UI loop: it is disposed, TikTok is restarted, and a
/// fresh session is created. User cancellation is the only normal exit.
/// </summary>
public sealed class LiveAutonomousWorkflow
{
    readonly string _appiumUrl;
    readonly DeviceRecord _device;
    readonly LiveManagerSettings _config;
    readonly bool _safeIdleRequested;
    readonly AdbClient _adb;
    readonly Action<string> _log;
    readonly Action<LiveSearchCard> _onCard;
    readonly AppiumSessionFactory? _sessionFactory;
    readonly DeviceContext? _context;
    readonly int _maxSessionRecoveryAttempts;
    readonly Func<int>? _sessionRecoveryLimitProvider;
    readonly Func<int>? _roomDeviceLimitProvider;
    int CurrentRecoveryLimit => Math.Clamp(
        _sessionRecoveryLimitProvider?.Invoke()??_maxSessionRecoveryAttempts,0,10);
    readonly GlobalRoomSlotManager _roomSlots;
    readonly int _maxDevicesPerRoom;

    public LiveAutonomousWorkflow(string appiumUrl,DeviceRecord device,
        LiveManagerSettings config,bool safeIdleRequested,AdbClient adb,
        Action<string> log,Action<LiveSearchCard> onCard,
        AppiumSessionFactory? sessionFactory=null,DeviceContext? context=null,
        int maxSessionRecoveryAttempts=3,GlobalRoomSlotManager? roomSlots=null,
        int maxDevicesPerRoom=5,Func<int>? roomDeviceLimitProvider=null,
        Func<int>? sessionRecoveryLimitProvider=null)
    {
        _appiumUrl=appiumUrl;
        _device=device;
        _config=config;
        _safeIdleRequested=safeIdleRequested;
        _adb=adb;
        _log=log;
        _onCard=onCard;
        _sessionFactory=sessionFactory;
        _context=context;
        _maxSessionRecoveryAttempts=Math.Clamp(maxSessionRecoveryAttempts,0,10);
        _roomDeviceLimitProvider=roomDeviceLimitProvider;
        _sessionRecoveryLimitProvider=sessionRecoveryLimitProvider;
        _roomSlots=roomSlots??GlobalRoomSlotManager.Shared;
        _maxDevicesPerRoom=Math.Clamp(maxDevicesPerRoom,1,100);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        int restartAttempt=0;
        bool restartTikTok=false;
        // Lives above Appium sessions so reconnects do not reset keyword history.
        var keywordQueue=new LiveKeywordLruQueue(_config.Keywords);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var online=await _adb.DevicesAsync(ct);
                if(!online.Contains(_device.Serial,StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Thiết bị không còn Online trong adb devices -l.");

                if(restartTikTok)
                {
                    _log($"GLOBAL_RECOVERY_RESTART_APP: attempt={restartAttempt}; package=com.ss.android.ugc.trill.");
                    await _adb.RestartTikTokAsync(_device.Serial,ct);
                }

                _log($"GLOBAL_SESSION_CREATE: attempt={restartAttempt+1}; device={_device.Name}.");
                AppiumSession session;
                IAsyncDisposable sessionOwner;
                if(_sessionFactory is not null && _context is not null)
                {
                    var lease=await _sessionFactory.CreateAsync(_context,ct);
                    session=lease.Session;sessionOwner=lease;
                }
                else
                {
                    session=new AppiumSession(_appiumUrl);
                    await session.CreateAsync(_device,ct);
                    sessionOwner=session;
                }
                await using(sessionOwner)
                {
                session.Performance=new AppiumPerformanceMonitor(_log,Storage.Folder,
                    _device.Name+"-"+session.DeviceKey);
                LiveSearchIdleSafety? idleScope=null;
                try
                {
                    if(_safeIdleRequested)
                        idleScope=await LiveSearchIdleSafety.EnableAsync(session,_log,ct);
                    void EngineLog(string message)
                    {
                        idleScope?.RecordEvidence(message);
                        _log(message);
                    }

                    var searchCycle=new LiveSearchCycle(
                        session,_config,EngineLog,_onCard,_adb,_device.Serial,keywordQueue);
                    LiveSearchReport report=await searchCycle.FindAcceptedRoomAsync(ct);
                    if(idleScope is not null)
                    {
                        bool valid=await idleScope.ValidateAcceptedRoomAsync(_config,ct);
                        if(!valid)
                            throw new InvalidOperationException(
                                "SAFE_IDLE validation failed after Phase 3 accepted a room.");
                        await idleScope.VerifyPhase5IdleAsync(ct);
                        EngineLog("SAFE_IDLE_FLOW_HANDOFF: PHASE1-3 completed; room validated; " +
                            "idle kept at 1000ms for Phase 5 optimization.");
                    }

                    EngineLog($"GLOBAL_ROOM_ACCEPTED: {report.SelectedCandidate?.Title??"LIVE"}; " +
                        "handoff=CONTINUOUS_COORDINATOR.");
                    restartAttempt=0;
                    restartTikTok=false;
                    var drafts=_config.CommentDrafts
                        .Where(value=>!string.IsNullOrWhiteSpace(value))
                        .Select(value=>value.Trim()).ToList();
                    var coordinator=new LiveRoomContinuousCoordinator(
                        session,_config,drafts,EngineLog,_onCard,_adb,_device.Serial,
                        searchCycle.FindAcceptedRoomAsync,roomSlots:_roomSlots,
                        slotDeviceKey:_device.DeviceId.ToString("N"),
                        maxDevicesPerRoom:_maxDevicesPerRoom,
                        roomDeviceLimitProvider:_roomDeviceLimitProvider);
                    await coordinator.RunAsync(ct);
                    throw new InvalidOperationException(
                        "Continuous coordinator returned without cancellation.");
                }
                finally
                {
                    if(idleScope is not null)
                    {
                        _log("SAFE_IDLE_TEARDOWN_START: restoring original Appium idle setting.");
                        await idleScope.RestoreAsync();
                    }
                }
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested)
            {
                _log("GLOBAL_WORKFLOW_STOP_REQUESTED: cancellation observed; no restart.");
                throw;
            }
            catch(Exception ex)
            {
                restartAttempt++;
                bool fatalSession=AppiumSessionFactory.IsFatalSessionException(ex) ||
                    ex.Message.Contains("không còn Online",StringComparison.OrdinalIgnoreCase);
                bool uiAutomatorHardReset=
                    AppiumFailureClassifier.IsUiAutomatorHardResetFailure(ex);
                bool serverConnectionRefused=
                    AppiumFailureClassifier.IsConnectionRefusedFailure(ex);
                if(serverConnectionRefused)
                {
                    _log("APPIUM_SERVER_CONNECTION_REFUSED: watchdog=START; sessionRetry=PAUSED.");
                    bool serverReady=await AppiumServerWatchdog.EnsureRunningAsync(
                        _appiumUrl,_log,ct);
                    if(serverReady)
                    {
                        restartAttempt=0;
                        _log("APPIUM_SERVER_RECOVERED: retryBudgetReset=true; " +
                            "next=GLOBAL_SESSION_CREATE.");
                    }
                }
                if(uiAutomatorHardReset)
                {
                    _log("UIAUTOMATOR2_HARD_RESET: threshold=3; " +
                        "oldSessionReleased=true; forceStopTikTok=true; " +
                        "next=createFreshSessionAndRestartSearch.");
                    await _adb.ForceStopTikTokAsync(_device.Serial,ct);
                    _log("TIKTOK_FORCE_STOPPED_FOR_UI_STALL: deviceScoped=true.");
                    try
                    {
                        await _adb.RestartUiAutomator2DriverAsync(_device.Serial,ct);
                        _log("UIAUTOMATOR2_DRIVER_STOPPED: deviceScoped=true; " +
                            "desktopAppiumServerUntouched=true.");
                    }
                    catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                    catch(Exception driverEx)
                    {
                        _log("UIAUTOMATOR2_DRIVER_STOP_WARNING: " +
                            driverEx.GetType().Name+"; fresh session creation will still be attempted.");
                    }
                }
                bool navigationRecoveryExhausted=ex.Message.Contains(
                    "FIND_PEOPLE_RECOVERY_FAILED",StringComparison.OrdinalIgnoreCase);
                if((fatalSession || navigationRecoveryExhausted) &&
                   restartAttempt>CurrentRecoveryLimit)
                {
                    _log($"GLOBAL_SESSION_RECOVERY_EXHAUSTED: attempts={CurrentRecoveryLimit}; " +
                        $"navigationRecovery={navigationRecoveryExhausted}; {ex.Message}");
                    throw;
                }
                restartTikTok=true;
                int delay=1500*Math.Min(10,restartAttempt);
                _log($"GLOBAL_WORKFLOW_RECOVERY: {ex.GetType().Name}: {ex.Message}; " +
                    $"disposeSession=true; restartTikTok=true; retryMs={delay}.");
                await Task.Delay(delay,ct);
            }
        }
    }
}
