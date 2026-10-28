using System.Collections.Concurrent;

namespace AndroidMultiDeviceV1.Core;

public sealed class DeviceOrchestrator : IDisposable
{
    readonly Settings _settings;
    readonly AccountVault _vault;
    readonly AdbClient _adb;
    readonly UiSelectors _selectors;
    readonly DeviceConcurrencyManager _parallel;
    readonly AppiumSessionFactory _sessions=new();
    // Serializes Start and destructive account removal. A worker must be
    // registered before the account can be considered safe for deletion.
    readonly object _lifecycleGate=new();
    readonly DeviceWorkerManager _workers=new();
    // At most one Appium session per phone, including read-only diagnostics.
    readonly ConcurrentDictionary<string,byte> _deviceSessionLocks=new(StringComparer.OrdinalIgnoreCase);
    // Prevent another Start/StartAll from racing an in-progress account switch.
    readonly ConcurrentDictionary<string,byte> _pendingSwitches=new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string,byte> _pendingDeviceRemovals=new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string,byte> _operatorStopRequests=new(StringComparer.OrdinalIgnoreCase);
    // Increment on every explicit operator Stop, including while a manual account
    // switch is waiting for the previous worker to finish. A later Stop must
    // cancel that pending manual Start; an old Stop must not block a NEW Start.
    readonly ConcurrentDictionary<string,long> _operatorStopVersions=new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string,DeviceAccountClock> _activeClocks=new(StringComparer.OrdinalIgnoreCase);
    readonly Action<string> _log;
    readonly Action _notify;
    readonly SessionCheckpointStore _checkpoints=new();
    readonly ProfileUpdateLedger _profileLedger=new();
    public DeviceOrchestrator(Settings settings,AccountVault vault,Action<string> log,Action notify)
    {
        _settings=settings;_vault=vault;_log=log;_notify=notify;
        _adb=new AdbClient(()=>_settings.AdbPath);
        _selectors=UiSelectors.Load();
        _parallel=new DeviceConcurrencyManager(settings.MaxParallel);
        _vault.SingleUseForSerial=serial=>_settings.AccountPolicyFor(serial).NeverReuseUsedAccounts;
    }
    void State(DeviceRecord d,string stage,string detail="")
    {
        d.Stage=stage;d.Detail=detail;_notify();
    }
    public async Task RefreshAsync(CancellationToken ct=default)
    {
        var connected=await _adb.DevicesAsync(ct);
        foreach(var discoveredSerial in connected)
        {
            string serial=DeviceIdentity.NormalizeSerial(discoveredSerial);
            if(serial.Length==0)continue;
            if(_settings.RemovedDeviceSerials.Contains(serial,StringComparer.OrdinalIgnoreCase))
                continue;
            DeviceRecord? existing=_settings.Devices.FirstOrDefault(d=>d.Serial.Equals(serial,StringComparison.OrdinalIgnoreCase));
            if(existing is null)
            {
                int port=8201;
                while(_settings.Devices.Any(d=>d.SystemPort==port))port++;
                existing=new DeviceRecord{Serial=serial,Name=DeviceIdentity.AllocateName(_settings.Devices),SystemPort=port};
                _settings.Devices.Add(existing);
                _log($"Thêm thiết bị {existing.Name} ({serial}), systemPort={port}");
            }
        }
        // Repair legacy/hand-edited duplicate ports before any worker starts.
        // Runtime reservation in AppiumSessionFactory remains the second guard.
        var assignedPorts=new HashSet<int>();
        foreach(var device in _settings.Devices)
        {
            if(device.SystemPort is >=8200 and <=8999 && assignedPorts.Add(device.SystemPort))continue;
            int previous=device.SystemPort,available=8200;
            while(assignedPorts.Contains(available))available++;
            device.SystemPort=available;assignedPorts.Add(available);
            new DeviceContext(_settings,device,_adb,_log).Log(
                $"SYSTEM_PORT_REALLOCATED: previous={previous}; current={available}");
        }
        foreach(var d in _settings.Devices)
        {
            d.Connection=connected.Contains(d.Serial,StringComparer.OrdinalIgnoreCase)?"ONLINE":"OFFLINE";
            if(d.Connection=="ONLINE")
            {
                d.LastSeenAt=DateTimeOffset.Now;
                try{d.Model=await _adb.ModelAsync(d.Serial,ct);}catch{d.Model="Android";}
            }
            if(d.Connection=="OFFLINE" && !_workers.Contains(d.Serial))
            {
                d.WorkerState=DeviceWorkerState.Disconnected.ToString();
                State(d,"OFFLINE");
            }
        }
        Storage.SaveSettings(_settings);_notify();
    }
    public async Task ConnectAsync(string endpoint,CancellationToken ct=default)
    {
        endpoint=DeviceIdentity.NormalizeSerial(endpoint);
        _settings.RemovedDeviceSerials.RemoveAll(serial=>
            serial.Equals(endpoint,StringComparison.OrdinalIgnoreCase));
        Storage.SaveSettings(_settings);
        var reply=await _adb.ConnectAsync(endpoint,ct);
        _log($"ADB connect {endpoint}: {reply}");
        await RefreshAsync(ct);
    }
    /// <summary>
    /// Rebind a rented ADB endpoint to the SAME logical DeviceId. The encrypted
    /// account pool remains owned by this phone, including usage history and
    /// lifecycle policy. Never delete/recreate a device to update its port.
    /// </summary>
    public async Task<(bool Success,string Detail)> RebindEndpointAsync(
        DeviceRecord device,string newEndpoint,CancellationToken ct=default)
    {
        newEndpoint=DeviceIdentity.NormalizeSerial(newEndpoint);
        string oldEndpoint=device.Serial;
        if(newEndpoint.Equals(oldEndpoint,StringComparison.OrdinalIgnoreCase))
            return (true,"Địa chỉ ADB không thay đổi.");
        if(!System.Text.RegularExpressions.Regex.IsMatch(newEndpoint,
            @"^[A-Za-z0-9_.-]+:[0-9]{1,5}$"))
            return (false,"ADB mới phải có dạng host:port.");
        if(!int.TryParse(newEndpoint[(newEndpoint.LastIndexOf(':')+1)..],out int tcpPort) ||
            tcpPort is < 1 or > 65535)
            return (false,"Cổng Remote ADB phải nằm trong khoảng 1–65535.");

        bool IsBusy(string serial)=>_workers.Contains(serial) ||
            _deviceSessionLocks.ContainsKey(serial) ||
            _pendingSwitches.ContainsKey(serial) ||
            _pendingDeviceRemovals.ContainsKey(serial);
        lock(_lifecycleGate)
        {
            if(!_settings.Devices.Contains(device) || IsBusy(oldEndpoint) || IsBusy(newEndpoint))
                return (false,"Thiết bị đang có worker/Appium/chuyển tài khoản; hãy Stop và chờ hoàn tất.");
            var other=_settings.Devices.FirstOrDefault(d=>d!=device &&
                d.Serial.Equals(newEndpoint,StringComparison.OrdinalIgnoreCase));
            if(other is not null && (other.WorkerState is "Running" or "Starting"))
                return (false,"ADB mới đang được một điện thoại khác sử dụng.");
        }
        // Verify the new endpoint BEFORE mutating the vault or settings.
        try { await _adb.ConnectAsync(newEndpoint,ct); }
        catch(Exception ex){return (false,"Không kết nối được ADB mới: "+ex.Message);}
        var online=await _adb.DevicesAsync(ct);
        if(!online.Contains(newEndpoint,StringComparer.OrdinalIgnoreCase))
            return (false,"ADB mới chưa ONLINE; cấu hình và kho cũ được giữ nguyên.");
        lock(_lifecycleGate)
        {
            if(IsBusy(oldEndpoint) || IsBusy(newEndpoint) || !_settings.Devices.Contains(device) ||
                !device.Serial.Equals(oldEndpoint,StringComparison.OrdinalIgnoreCase))
                return (false,"Worker hoặc thông tin máy đã thay đổi trong khi kết nối; không cập nhật.");
            var duplicate=_settings.Devices.FirstOrDefault(d=>d!=device &&
                d.Serial.Equals(newEndpoint,StringComparison.OrdinalIgnoreCase));
            if(duplicate is not null &&
                (_workers.Contains(duplicate.Serial) ||
                 _vault.Snapshot(newEndpoint).Count>0))
                return (false,"ADB mới đã có máy/kho tài khoản riêng; không tự gộp dữ liệu.");
            bool vaultMoved=false;
            string previousConnection=device.Connection;
            string previousWorkerState=device.WorkerState;
            string previousAppiumStatus=device.AppiumStatus;
            var previousRemovedSerials=_settings.RemovedDeviceSerials.ToList();
            int duplicateIndex=duplicate is null?-1:_settings.Devices.IndexOf(duplicate);
            try
            {
                _vault.ChangeDeviceSerial(oldEndpoint,newEndpoint);
                vaultMoved=true;
                device.Serial=newEndpoint;
                device.Connection="ONLINE";
                device.AppiumStatus="DISCONNECTED";
                device.WorkerState=DeviceWorkerState.Idle.ToString();
                if(duplicate is not null)_settings.Devices.Remove(duplicate);
                if(!_settings.RemovedDeviceSerials.Contains(oldEndpoint,StringComparer.OrdinalIgnoreCase))
                    _settings.RemovedDeviceSerials.Add(oldEndpoint);
                _settings.RemovedDeviceSerials.RemoveAll(serial=>
                    serial.Equals(newEndpoint,StringComparison.OrdinalIgnoreCase));
                Storage.SaveSettings(_settings);
                // Old ADB no longer has a worker. Settle orphaned ACTIVE/RESERVED
                // from the previous endpoint without treating them as INVALID.
                int orphaned=_vault.ReconcileOrphanedAfterEndpointRebind(newEndpoint);
                _log($"ADB_REBIND_STALE_SESSION_RECONCILED: phone={device.Name}; count={orphaned}; " +
                    "ownerMustStillBeVerified=true; credentialsPreserved=true.");
                _log($"ADB_ENDPOINT_REBOUND: phone={device.Name}; deviceId={device.DeviceId}; " +
                    $"old={oldEndpoint}; new={newEndpoint}; systemPort={device.SystemPort}; " +
                    "accountVaultMigrated=true; historyAndLifecyclePreserved=true.");
                _notify();
                return (true,$"Đã chuyển {device.Name} sang {newEndpoint}; giữ nguyên kho và cấu hình.");
            }
            catch(Exception ex)
            {
                // Roll back BOTH encrypted pool routing and settings on a write
                // failure. Never leave the UI advertising a half-migrated phone.
                device.Serial=oldEndpoint;
                device.Connection=previousConnection;
                device.WorkerState=previousWorkerState;
                device.AppiumStatus=previousAppiumStatus;
                if(duplicate is not null && !_settings.Devices.Contains(duplicate))
                    _settings.Devices.Insert(Math.Clamp(duplicateIndex,0,_settings.Devices.Count),duplicate);
                _settings.RemovedDeviceSerials=previousRemovedSerials;
                var rollbackIssues=new List<string>();
                if(vaultMoved)
                {
                    try {_vault.ChangeDeviceSerial(newEndpoint,oldEndpoint);}
                    catch(Exception rollbackEx){rollbackIssues.Add("vault:"+rollbackEx.GetType().Name);}
                }
                try {Storage.SaveSettings(_settings);}
                catch(Exception rollbackEx){rollbackIssues.Add("settings:"+rollbackEx.GetType().Name);}
                _log($"ADB_ENDPOINT_REBIND_FAILED: phone={device.Name}; "+ex.GetType().Name+
                    "; rollbackIssues="+string.Join(",",rollbackIssues));
                return (false,"Không hoàn tất đổi ADB: "+ex.Message+
                    (rollbackIssues.Count>0
                        ? ". Phục hồi lưu trữ chưa trọn vẹn; hãy dùng bản sao lưu vault/settings và không Start máy này."
                        : ". Đã phục hồi cấu hình và kho về endpoint cũ."));
            }
        }
    }

    public bool IsRunning(string serial)=>_workers.Contains(serial);
    public void ApplyAccountPolicy() =>
        _vault.SingleUseForSerial=serial=>_settings.AccountPolicyFor(serial).NeverReuseUsedAccounts;

    /// <summary>Configuration may be saved while a worker is alive. Explicitly
    /// request ApplyNow to re-arm a *verified* account clock; otherwise defer.</summary>
    public (bool Applied,string Detail) ApplyLifecycleToRunning(DeviceRecord device,bool applyNow)
    {
        var context=new DeviceContext(_settings,device,_adb,_log);
        if(!applyNow)
        {
            context.Log("ACCOUNT_LIFECYCLE_POLICY_DEFERRED: current session unchanged; next account only.");
            return (false,"Đã lưu. Giới hạn mới áp dụng từ tài khoản tiếp theo.");
        }
        if(!_activeClocks.TryGetValue(device.Serial,out var clock))
        {
            context.Log("ACCOUNT_LIFECYCLE_APPLY_NOT_ACTIVE: no verified session timer; no blind pm clear.");
            return (false,"Máy chưa có phiên tài khoản ĐÃ XÁC MINH được quản lý. " +
                "Nếu đang Start LIVE từ V4, hãy Stop rồi Start LIVE lại để xác minh và bật bộ đếm.");
        }
        try
        {
            var policy=_settings.AccountPolicyFor(device).Copy();
            if(!clock.ApplyNow(policy,out var deadline))
                return (false,"Phiên đã hết giờ hoặc đang kết thúc; không thể sửa hạn của phiên cũ.");
            context.Log($"ACCOUNT_LIFECYCLE_APPLIED_NOW: accountId={clock.AccountId}; " +
                $"originalStartUtc={clock.StartedUtc:O}; limitMinutes={policy.SessionLimitMinutes}; " +
                $"deadlineUtc={deadline:O}; expired={deadline<=DateTimeOffset.UtcNow}.");
            return (true,$"Đã áp dụng. Hạn phiên hiện tại: {deadline.LocalDateTime:dd/MM/yyyy HH:mm:ss}.");
        }
        catch(Exception ex)
        {
            context.Log("ACCOUNT_LIFECYCLE_APPLY_FAILED: "+ex.GetType().Name+"; "+ex.Message);
            return (false,"Không thay đổi được thời hạn đang chạy: "+ex.Message);
        }
    }

    private void DetachClock(DeviceRecord device,DeviceAccountClock? clock)
    {
        if(clock is null)return;
        if(_activeClocks.TryGetValue(device.Serial,out var current) &&
           ReferenceEquals(current,clock))
            _activeClocks.TryRemove(device.Serial,out _);
        clock.Close();
    }

    // Restore the existing V4 LIVE popup recovery BEFORE the V5.2 identity gate.
    // The original LIVE workflow already calls LivePopupHandler in its Search
    // and fallback paths, but those paths are unreachable until owner verification
    // succeeds. Reuse the same handler here; do not implement a second CAPTCHA
    // algorithm and NEVER clear TikTok/GMS while owner identity is unknown.
    private async Task<TikTokObservation> DetectLiveOwnerWithV4PopupRecoveryAsync(
        AppiumSession session,DeviceContext context,IReadOnlyList<ManagedAccount> approved,
        CancellationToken ct)
    {
        var detector=new TikTokStateDetector(_selectors);
        var expected=approved[0].ExpectedTikTokHandle;
        var approvedHandles=approved.Select(a=>a.ExpectedTikTokHandle).ToArray();
        bool restarted=false;
        // The owner UI may not be ready on the first LIVE Start, especially
        // while TikTok shows an onboarding overlay. Only one navigation-only
        // relaunch is permitted; it never clears TikTok or Google app data.
        bool navigationRecoveryAttempted=false;
        for(int attempt=1;attempt<=5;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var source=await session.SourceAsync(ct);
            // The owner verification can itself start on TikTok's Find people
            // subpage. Reuse the same bounded device-scoped recovery before
            // querying Profile, rather than waiting for generic five Back retries.
            if(TikTokNavigationRecovery.IsFindPeopleScreen(source))
            {
                var nav=new TikTokNavigationRecovery(session,_adb,context.Device.Serial,context.Log);
                bool recovered=await nav.RecoverFindPeopleAsync(ct,"LIVE_OWNER_VERIFY");
                if(!recovered)
                    return new TikTokObservation(TikTokUiState.Unknown,
                        "FIND_PEOPLE_RECOVERY_FAILED: chưa về được giao diện TikTok hợp lệ; " +
                        "giữ nguyên phiên và dữ liệu tài khoản.");
                context.Log("WORKFLOW_RESUMED: phase=LIVE_OWNER_VERIFY; checkpoint=PROFILE_RECHECK");
                continue;
            }
            if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(
                session,context.Log,ct,source))
            {
                context.Log($"LIVE_OWNER_V4_POLICY_DISMISSED: attempt={attempt}/5; retry identity verification.");
                continue;
            }
            var before=await LivePopupHandler.DismissVerifyCaptchaAsync(
                session,context.Log,ct,source,"LIVE_OWNER_V4");
            if(before.Status==CaptchaDismissStatus.Dismissed)
            {
                context.Log($"LIVE_OWNER_V4_CAPTCHA_DISMISSED: attempt={attempt}/5; reread UI before Profile.");
                continue;
            }
            if(before.Status==CaptchaDismissStatus.Failed)
            {
                if(!restarted && !navigationRecoveryAttempted)
                {
                    restarted=true;
                    context.Log("LIVE_OWNER_V4_CAPTCHA_RESTART: original V4 non-destructive LIVE fallback; pmClear=false.");
                    await _adb.RestartTikTokAsync(context.Device.Serial,ct);
                    continue;
                }
                context.Log("LIVE_OWNER_V4_CAPTCHA_UNRESOLVED: popup remained after original V4 dismissal and one relaunch; no pm clear.");
                return new TikTokObservation(TikTokUiState.SecurityChallenge,
                    "Không đóng được popup xác minh bằng luồng LIVE V4 sau các lần kiểm tra.");
            }

            // A popup can appear while Profile navigation is in progress.
            var seen=await detector.DetectAsync(session,expected,approvedHandles,ct,
                message=>context.Log("LIVE_OWNER_VERIFY: "+message));
            if(seen.State==TikTokUiState.Unknown &&
               seen.Detail.Contains("PROFILE_NAVIGATION_NOT_CONFIRMED",StringComparison.Ordinal))
            {
                // DetectAsync used its five careful Back attempts, but the
                // Profile tab may appear only after an in-app popup renders.
                // Re-check late UI with the exact existing V4 close handler.
                source=await session.SourceAsync(ct);
                if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(
                    session,context.Log,ct,source))
                {
                    context.Log($"LIVE_OWNER_PROFILE_LATE_POLICY_DISMISSED: attempt={attempt}/5; retry Profile.");
                    continue;
                }
                var late=await LivePopupHandler.DismissVerifyCaptchaAsync(
                    session,context.Log,ct,source,"LIVE_OWNER_V4_LATE");
                if(late.Status==CaptchaDismissStatus.Dismissed)
                {
                    context.Log($"LIVE_OWNER_PROFILE_LATE_CAPTCHA_DISMISSED: attempt={attempt}/5; retry Profile.");
                    continue;
                }
                if(late.Status==CaptchaDismissStatus.Failed)
                {
                    context.Log("LIVE_OWNER_PROFILE_RECOVERY_BLOCKED: verification popup still present; preserve current account.");
                    return new TikTokObservation(TikTokUiState.SecurityChallenge,
                        "Popup xác minh chưa xử lý xong; không chạy lại hoặc xóa dữ liệu.");
                }
                if(restarted || navigationRecoveryAttempted)
                {
                    context.Log("LIVE_OWNER_PROFILE_RECOVERY_EXHAUSTED: one bounded relaunch already attempted; no pm clear.");
                    return seen;
                }
                navigationRecoveryAttempted=true;
                context.Log("LIVE_OWNER_PROFILE_RECOVERY_START: no verified Profile after 5 Back; " +
                    "using existing navigation recovery in SAME Start LIVE; forceRestartFirst=true; pmClear=false.");
                var recovery=new TikTokStateRecovery(_adb,
                    message=>context.Log("LIVE_OWNER_PROFILE_RECOVERY: "+message));
                bool ready;
                try
                {
                    ready=await recovery.RecoverNavigationAsync(session,context.Device,ct,
                        forceRestartFirst:true);
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                catch(Exception ex) when(!AppiumFailureClassifier.IsFatalSessionFailure(ex))
                {
                    context.Log($"LIVE_OWNER_PROFILE_RECOVERY_ERROR: {ex.GetType().Name}; " +
                        "preserve account and app data.");
                    return seen;
                }
                context.Log($"LIVE_OWNER_PROFILE_RECOVERY_RESULT: ready={ready}; " +
                    "recheckV4PopupAndProfile="+ready+"; pmClear=false.");
                if(ready)continue;
                return seen;
            }
            if(seen.State!=TikTokUiState.SecurityChallenge)
                return seen;

            source=await session.SourceAsync(ct);
            var after=await LivePopupHandler.DismissVerifyCaptchaAsync(
                session,context.Log,ct,source,"LIVE_OWNER_V4_POST_DETECT");
            if(after.Status==CaptchaDismissStatus.Dismissed)
            {
                context.Log($"LIVE_OWNER_V4_CAPTCHA_DISMISSED_AFTER_DETECT: attempt={attempt}/5; retry Profile.");
                continue;
            }
            if(after.Status==CaptchaDismissStatus.Failed && !restarted && !navigationRecoveryAttempted)
            {
                restarted=true;
                context.Log("LIVE_OWNER_V4_CAPTCHA_RESTART_AFTER_DETECT: one non-destructive relaunch; pmClear=false.");
                await _adb.RestartTikTokAsync(context.Device.Serial,ct);
                continue;
            }
            // An actual unclosable security challenge is not bypassed.
            context.Log("LIVE_OWNER_V4_SECURITY_UNRESOLVED: no V4-dismissible popup; preserve app data and account.");
            return seen;
        }
        context.Log("LIVE_OWNER_V4_CAPTCHA_RETRY_EXHAUSTED: bounded five UI checks; no pm clear.");
        return new TikTokObservation(TikTokUiState.SecurityChallenge,
            "Popup xác minh xuất hiện lặp lại, chưa thể đọc Profile sau 5 lượt; không xóa dữ liệu.");
    }

    // LIVE direct may start with an already authenticated profile. The owner
    // is verified via the existing strict owner-profile detector, NEVER inferred
    // from a comment-room host, OCR card, or last used account in the pool.
    private async Task<ManagedAccount?> VerifyLiveOwnerAsync(DeviceContext context,CancellationToken ct,
        Guid? manualResumeAccountId=null)
    {
        var device=context.Device;
        var pool=_vault.Snapshot(device.Serial);
        if(pool.Count==0)
        {
            context.Log("LIVE_LIFECYCLE_UNMANAGED: no accounts assigned; LIVE remains standalone with no timer.");
            return null;
        }
        var approved=pool.Where(a=>TikTokStateDetector.IsValidHandle(a.ExpectedTikTokHandle)).ToList();
        if(approved.Count==0)
            throw new InvalidOperationException("LIVE_IDENTITY_UNVERIFIED: kho thiếu TikTok username hợp lệ; không tự xóa dữ liệu.");
        State(device,"LIVE_VERIFY_ACCOUNT","Đọc Profile chính chủ trước khi bắt đầu bộ đếm vòng đời.");
        await using(var lease=await _sessions.CreateAsync(context,ct))
        {
            var seen=await DetectLiveOwnerWithV4PopupRecoveryAsync(
                lease.Session,context,approved,ct);
            if(seen.State is not (TikTokUiState.ExpectedAccount or TikTokUiState.OtherApprovedAccount) ||
                !TikTokStateDetector.IsValidHandle(seen.VerifiedHandle))
                throw new InvalidOperationException("LIVE_IDENTITY_UNVERIFIED: "+seen.State+"; "+seen.Detail+
                    ". Dừng an toàn, không pm clear.");
            var normalized=TikTokStateDetector.NormalizeHandle(seen.VerifiedHandle);
            var matched=approved.Where(a=>
                TikTokStateDetector.NormalizeHandle(a.ExpectedTikTokHandle)==normalized).ToList();
            if(matched.Count!=1)
                throw new InvalidOperationException("LIVE_IDENTITY_AMBIGUOUS: username không trùng DUY NHẤT một tài khoản trong kho máy; không pm clear.");
            var candidate=matched.Single();
            // V5.9: a re-bound endpoint or an earlier crashed session can leave
            // a stale ACTIVE/RESERVED flag. Correct it only after exact, unique
            // username proof, within this sole worker for the phone.
            if(candidate.State is "ACTIVE" or "RESERVED" &&
                _vault.ReconcileVerifiedOrphanedOwner(device.Serial,candidate.Id))
            {
                context.Log("LIVE_OWNER_ORPHANED_SESSION_RECONCILED: accountId="+candidate.Id+
                    "; strictProfileMatch=true; retainedAccumulatedTime=true; pmClear=false.");
                candidate=_vault.Snapshot(device.Serial).Single(a=>a.Id==candidate.Id);
            }
            if(manualResumeAccountId.HasValue)
            {
                if(candidate.Id!=manualResumeAccountId.Value)
                    throw new InvalidOperationException("MANUAL_RESUME_IDENTITY_MISMATCH: đang đăng nhập @"+
                        normalized+", không phải tài khoản đã chọn; không xóa dữ liệu.");
                if(_vault.PreviewManualResume(device.Serial,candidate.Id,
                    _settings.AccountPolicyFor(device).SessionLimitMinutes) is null)
                    throw new InvalidOperationException("MANUAL_RESUME_NOT_ELIGIBLE: " +
                        "tài khoản hết giờ/chưa biết thời lượng hoặc cần kiểm tra.");
            }
            else
            {
                var decision=_vault.AssessVerifiedLiveOwner(device.Serial,candidate.Id,
                    _settings.AccountPolicyFor(device).SessionLimitMinutes);
                if(decision==AccountVault.LiveOwnerDecision.Blocked)
                {
                    string reason=candidate.State is "INVALID" or "RESTRICTED" or "DISABLED" or
                        "ACCOUNT_CHECKPOINT" ? "ACCOUNT_RESTRICTION" :
                        candidate.State=="NEEDS_REVIEW" ?
                            (AccountVault.IsDocumentedTechnicalUiFailure(candidate.LastResult)
                                ? "REVIEW_STATE_CHANGED" : "SECURITY_OR_IDENTITY_REVIEW") :
                        candidate.State is "ACTIVE" or "RESERVED" ? "RESERVATION_IN_PROGRESS" :
                        "INELIGIBLE_STATE";
                    context.Log($"LIVE_OWNER_ELIGIBILITY_REJECTED: accountId={candidate.Id}; " +
                        $"state={candidate.State}; reason={reason}; " +
                        "verifiedProfile=true; savedCredentialsUnchanged=true.");
                    throw new InvalidOperationException("LIVE_OWNER_NOT_ELIGIBLE: @"+normalized+
                        "; state="+candidate.State+"; reason="+reason+
                        "; giữ nguyên dữ liệu, không tự đổi tài khoản.");
                }
                if(decision==AccountVault.LiveOwnerDecision.RotateToFresh)
                    context.Log("LIVE_OWNER_TIME_EXHAUSTED: @"+normalized+
                        "; chuyển sang luồng Start tài khoản MỚI của chính máy; không mở lại tài khoản cũ.");
                else if(candidate.State=="NEEDS_REVIEW")
                    context.Log("LIVE_OWNER_TECH_REVIEW_RESUMED: @"+normalized+
                        "; identity=STRICT_PROFILE_MATCH; reason=TECHNICAL_UI_ONLY; pmClear=false.");
            }
            context.Log("LIVE_OWNER_VERIFIED: @"+normalized+"; accountId="+candidate.Id+
                "; mode="+(manualResumeAccountId.HasValue?"MANUAL_RESUME":"VERIFIED_CURRENT_OWNER"));
            // Return only an identity; reservation happens after the Appium
            // verification session has been fully disposed.
            return candidate;
        }
    }
    // Workers registered but waiting for a permit do NOT count as running.
    public int RunningCount=>_workers.Snapshot().Count(w=>w.State==DeviceWorkerState.Running);
    public int WaitingCount=>_parallel.WaitingCount;
    public int ActiveSlotCount=>_parallel.ActiveCount;
    public int RegisteredWorkerCount=>_workers.RunningCount;
    public int ConcurrencyLimit=>_parallel.Limit;

    public void UpdateConcurrencyLimit(int newLimit)
    {
        var before=_parallel.Snapshot();
        var after=_parallel.UpdateLimit(newLimit);
        _log($"CONCURRENCY_LIMIT_UPDATED: oldLimit={before.Limit}; newLimit={after.NewLimit}; " +
            $"active={after.Active}; waiting={after.Waiting}; " +
            $"mode={(after.Active>after.NewLimit?"GRACEFUL_DRAIN":"IMMEDIATE")}");
        _notify();
    }

    async Task<IAsyncDisposable> AcquireWorkerSlotAsync(DeviceWorker worker)
    {
        var context=worker.Context;
        worker.Token.ThrowIfCancellationRequested();
        worker.SetState(DeviceWorkerState.WaitingForSlot);
        var before=_parallel.Snapshot();
        if(before.Active>=before.Limit || before.Waiting>0)
        {
            State(context.Device,"WAIT_CONCURRENCY_SLOT","Đang chờ suất chạy song song.");
            context.Log($"CONCURRENCY_SLOT_WAIT: active={before.Active}; waiting={before.Waiting}; limit={before.Limit}");
        }
        else context.Log($"CONCURRENCY_SLOT_REQUEST: active={before.Active}; limit={before.Limit}");
        IAsyncDisposable lease;
        try {lease=await _parallel.AcquireAsync(worker.Token);}
        catch(OperationCanceledException) when(worker.Token.IsCancellationRequested)
        {
            context.Log("CONCURRENCY_SLOT_WAIT_CANCELLED: stop requested before slot granted.");
            throw;
        }
        if(worker.Token.IsCancellationRequested)
        {
            await lease.DisposeAsync();
            worker.Token.ThrowIfCancellationRequested();
        }
        worker.SetState(DeviceWorkerState.Running);
        if(worker.Token.IsCancellationRequested)
        {
            await lease.DisposeAsync();
            worker.Token.ThrowIfCancellationRequested();
        }
        var after=_parallel.Snapshot();
        context.Log($"CONCURRENCY_SLOT_GRANTED: active={after.Active}; waiting={after.Waiting}; limit={after.Limit}");
        _notify();
        return lease;
    }

    public int DeleteAccounts(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock(_lifecycleGate)
        {
            return _vault.DeleteAccounts(ids, serial =>
                _workers.Contains(serial) || _deviceSessionLocks.ContainsKey(serial));
        }
    }

    public bool Start(DeviceRecord device)
    {
        lock(_lifecycleGate) return StartUnderLock(device);
    }

    /// <summary>Starts the existing Phase 1-5 LIVE workflow inside a normal isolated worker.</summary>
    public bool StartLive(DeviceRecord device,LiveManagerSettings config,bool safeIdleRequested,
        Action<LiveSearchCard>? onCard=null,Guid? manualResumeAccountId=null)
    {
        lock(_lifecycleGate)
        {
            if(_pendingDeviceRemovals.ContainsKey(device.Serial) ||
               !_settings.Devices.Any(d=>ReferenceEquals(d,device) ||
                   d.Serial.Equals(device.Serial,StringComparison.OrdinalIgnoreCase)))
            {State(device,"REMOVING","Thiết bị đang được gỡ khỏi danh sách.");return false;}
            if(_pendingSwitches.ContainsKey(device.Serial) || _workers.Contains(device.Serial))
            {State(device,"BUSY","Thiết bị đã có worker đang chạy.");return false;}
            if(device.Connection!="ONLINE")
            {State(device,"OFFLINE","ADB chưa Online.");return false;}
            if(manualResumeAccountId.HasValue && _vault.PreviewManualResume(device.Serial,
                manualResumeAccountId.Value,_settings.AccountPolicyFor(device).SessionLimitMinutes) is null)
            {
                State(device,"MANUAL_RESUME_BLOCKED","Tài khoản đã chọn không đủ điều kiện: sai máy, " +
                    "thiếu thời lượng đã chạy, hết giờ hoặc đang bị hạn chế.");
                return false;
            }
            // A new operator Start clears any previous Stop intent.
            _operatorStopRequests.TryRemove(device.Serial,out _);
            EnsureUniqueSystemPort(device);
            if(!_deviceSessionLocks.TryAdd(device.Serial,0))
            {State(device,"BUSY","Thiết bị đã có Appium session.");return false;}

            var context=new DeviceContext(_settings,device,_adb,_log);
            var worker=new DeviceWorker(context);
            if(!_workers.TryAdd(worker))
            {_deviceSessionLocks.TryRemove(device.Serial,out _);worker.Dispose();return false;}
            device.WorkerStartedAt=DateTimeOffset.Now;device.LastError="";
            var work=Task.Run(async()=>
            {
                DeviceAccountClock? clock=null;
                ManagedAccount? liveAccount=null;
                bool sessionExpired=false;
                bool clearSucceeded=false;
                bool startNext=false;
                AccountLifecyclePolicy effectivePolicy=_settings.AccountPolicyFor(device).Copy();
                using var timedOut=new CancellationTokenSource();
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(worker.Token,timedOut.Token);
                try
                {
                    worker.SetState(DeviceWorkerState.Starting);
                    await using(await AcquireWorkerSlotAsync(worker))
                    {
                        var assigned=_vault.Snapshot(device.Serial);
                        if(assigned.Count>0)
                        {
                            var verified=await VerifyLiveOwnerAsync(context,worker.Token,
                                manualResumeAccountId);
                            if(verified is null)
                                throw new InvalidOperationException("Không xác minh được tài khoản chính chủ.");
                            // Re-read settings at VERIFIED session start (a UI save may have
                            // occurred while Profile verification was still in progress).
                            effectivePolicy=_settings.AccountPolicyFor(device).Copy();
                            // Verified CURRENT owner can be resumed on Start LIVE
                            // even if previously used. AUTO picking OTHER accounts
                            // still only accepts fresh accounts in ReserveNext().
                            var ownerDecision=_vault.AssessVerifiedLiveOwner(device.Serial,
                                verified.Id,effectivePolicy.SessionLimitMinutes);
                            if(!manualResumeAccountId.HasValue &&
                               ownerDecision==AccountVault.LiveOwnerDecision.RotateToFresh)
                            {
                                startNext=true;
                                context.Log("LIVE_OWNER_EXPIRED_AUTO_FRESH_HANDOFF: đã xác minh chủ máy; " +
                                    "nhường chỗ cho Start thường đối chiếu rồi chọn TK CHƯA DÙNG; " +
                                    "không tự pm clear trước Preflight.");
                                State(device,"ACCOUNT_ROTATION_PENDING","Tài khoản hiện tại đã hết thời gian; " +
                                    "chuyển sang tài khoản mới bằng Start thường.");
                            }
                            else
                            {
                            liveAccount=manualResumeAccountId.HasValue
                                ? _vault.ReserveManualResume(device.Serial,verified.Id,
                                    effectivePolicy.SessionLimitMinutes)
                                : _vault.ReserveVerifiedLiveOwner(device.Serial,verified.Id,
                                    effectivePolicy.SessionLimitMinutes);
                            if(liveAccount is null)
                                throw new InvalidOperationException("Không thể giữ chỗ tài khoản đã xác minh; kiểm tra trạng thái kho.");
                            var deadline=_vault.StartVerifiedTimedSession(liveAccount.Id,
                                effectivePolicy.SessionLimitMinutes);
                            var started=_vault.Snapshot(device.Serial).Single(a=>a.Id==liveAccount.Id)
                                .SessionStartedAtUtc!.Value;
                            clock=new DeviceAccountClock(device,_vault,liveAccount.Id,started,deadline,
                                effectivePolicy,timedOut,verified.ExpectedTikTokHandle,"START_LIVE",_notify);
                            if(!_activeClocks.TryAdd(device.Serial,clock))
                                throw new InvalidOperationException("Máy đã có một bộ đếm vòng đời khác.");
                            device.AccountEmail=liveAccount.Email;
                            var tracked=_vault.Snapshot(device.Serial).Single(a=>a.Id==liveAccount.Id);
                            string currentOwnerMode = manualResumeAccountId.HasValue
                                ? "MANUAL_RESUME"
                                : liveAccount.EverUsed ? "CURRENT_OWNER_RESUME" : "CURRENT_OWNER_FRESH";
                            context.Log($"LIVE_ACCOUNT_SESSION_STARTED: accountId={liveAccount.Id}; " +
                                $"startedUtc={started:O}; deadlineUtc={deadline:O}; " +
                                $"accumulatedSeconds={tracked.AccumulatedRunSeconds:F1}; " +
                                $"mode={currentOwnerMode}; " +
                                $"legacyHistoryUnknown={tracked.LegacyHistoryUnverified}; " +
                                $"limitMinutes={effectivePolicy.SessionLimitMinutes}; deviceId={device.DeviceId}.");
                            if(tracked.LegacyHistoryUnverified)
                                context.Log("ACCOUNT_LEGACY_HISTORY_UNKNOWN: lịch sử trước V5.2 không có đồng hồ " +
                                    "đáng tin cậy; thời lượng mới chỉ tính từ mốc xác minh, KHÔNG phải tổng thời gian lịch sử.");
                            // The owner detector left the phone on Profile/Edit profile.
                            // Non-destructive app restart returns to the feed for LIVE Search.
                            await _adb.RestartTikTokAsync(device.Serial,linked.Token);
                            }
                        }
                        else
                            device.LifecycleRuntimeMode="CHUA_KICH_HOAT";
                        if(!startNext)
                        {
                        State(device,"LIVE_RUNNING",liveAccount is null
                            ? "Start LIVE riêng, không có tài khoản trong kho: không có bộ hẹn giờ."
                            : "LIVE đang chạy với tài khoản xác minh và bộ đếm riêng máy.");
                        var workflow=new LiveAutonomousWorkflow(_settings.AppiumUrl,device,config,
                            safeIdleRequested,_adb,context.Log,onCard??(_=>{}),_sessions,context,
                            _settings.MaxSessionRecoveryAttempts,GlobalRoomSlotManager.Shared,
                            _settings.MaxDevicesPerLiveRoom,
                            roomDeviceLimitProvider:()=>_settings.MaxDevicesPerLiveRoom,
                            sessionRecoveryLimitProvider:()=>_settings.MaxSessionRecoveryAttempts);
                        await workflow.RunAsync(linked.Token);
                        context.Log("LIVE_WORKFLOW_RETURNED_BEFORE_DEADLINE: no automatic rotation.");
                        }
                    }
                }
                catch(OperationCanceledException) when(timedOut.IsCancellationRequested && !worker.Token.IsCancellationRequested)
                {
                    sessionExpired=liveAccount is not null;
                    context.Log("LIVE_ACCOUNT_SESSION_LIMIT_REACHED: verified session timed out; Appium workflow cancelled.");
                }
                catch(OperationCanceledException) when(worker.Token.IsCancellationRequested)
                {context.Log("LIVE_WORKER_STOPPED: user cancellation acknowledged; no automatic rotation.");}
                catch(Exception ex)
                {
                    worker.SetState(DeviceWorkerState.Error);device.LastError=ex.Message;
                    State(device,"ERROR",ex.Message);context.Log("LIVE_WORKER_ERROR: "+ex.Message);
                }
                finally
                {
                    effectivePolicy=clock?.Policy ?? effectivePolicy;
                    DetachClock(device,clock);
                    if(liveAccount is not null)
                    {
                        if(sessionExpired && !worker.Token.IsCancellationRequested)
                        {
                            _vault.EndUsedSession(liveAccount.Id,
                                "Hết hạn phiên LIVE đã xác minh; chờ xóa dữ liệu TikTok/GMS",
                                effectivePolicy.PurgeUsedAfterHours);
                            _checkpoints.Record(device.Serial,liveAccount.Id,"SESSION_TIME_LIMIT_REACHED");
                            State(device,"ACCOUNT_ROTATION_CLEAR_DATA","Phiên LIVE đã đủ giờ; kết thúc và xóa dữ liệu.");
                            try
                            {
                                // Run only after workflow + Appium session are disposed.
                                await _adb.ClearTikTokAppDataAsync(device.Serial,worker.Token);
                                await _adb.ClearGooglePlayServicesDataAsync(device.Serial,worker.Token);
                                clearSucceeded=true;
                                _vault.ScheduleCompletedPurge(liveAccount.Id,effectivePolicy.PurgeUsedAfterHours);
                                context.Log("LIVE_ACCOUNT_ROTATION_CLEAR_OK: pm clear TikTok + GMS; account="+liveAccount.Id);
                            }
                            catch(OperationCanceledException) when(worker.Token.IsCancellationRequested)
                            { context.Log("LIVE_ACCOUNT_CLEAR_CANCELLED: operator Stop; no handoff."); }
                            catch(Exception ex)
                            {
                                device.LastError=ex.Message;
                                State(device,"WAIT_RECOVERY","Hết hạn nhưng pm clear thất bại; không đổi tài khoản.");
                                context.Log("LIVE_ACCOUNT_ROTATION_CLEAR_FAILED: "+ex.Message);
                            }
                            startNext=clearSucceeded && effectivePolicy.AutoRotateAccounts &&
                                !worker.Token.IsCancellationRequested;
                        }
                        else
                        {
                            // An interrupted verified LIVE account must never return to the
                            // AVAILABLE queue when Single Use is enabled.
                            _vault.PauseVerifiedSession(liveAccount.Id,
                                "Dừng Start LIVE trước hạn; cộng dồn thời gian; chỉ chạy tiếp thủ công.");
                        }
                    }
                    device.AccountEmail="";
                    _workers.TryRemove(device.Serial,out _);
                    _deviceSessionLocks.TryRemove(device.Serial,out _);
                    device.AppiumStatus="DISCONNECTED";device.WorkerStartedAt=null;
                    if(device.Stage is not ("ERROR" or "WAIT_RECOVERY") && !startNext)
                    {device.WorkerState=DeviceWorkerState.Idle.ToString();
                     State(device,"STOPPED",sessionExpired?"Đã kết thúc phiên và xóa dữ liệu.":"LIVE worker đã dừng.");}
                    worker.Dispose();_notify();
                }
                if(startNext)
                {
                    // The previous worker + Appium lease have been fully released.
                    // Re-enter the original normal Start account flow, not a duplicate
                    // Google login implementation. A Stop after deadline blocks handoff.
                    lock(_lifecycleGate)
                    {
                        if(device.Connection=="ONLINE" &&
                           !_operatorStopRequests.ContainsKey(device.Serial) &&
                           !_pendingDeviceRemovals.ContainsKey(device.Serial))
                        {
                            bool started=StartUnderLock(device,fromSwitch:true);
                            context.Log($"LIVE_ACCOUNT_ROTATION_HANDOFF: nextWorkerStarted={started}; " +
                                "path=NORMAL_ACCOUNT_START; sameDevice=true.");
                        }
                    }
                }
            });
            worker.Attach(work);_notify();
            context.Log($"LIVE_WORKER_STARTING: systemPort={device.SystemPort}; maxConcurrent={_parallel.Limit}");
            return true;
        }
    }

    bool StartUnderLock(DeviceRecord device,Guid? onlyAccountId=null,bool fromSwitch=false,
        bool manualResume=false,bool operatorInitiated=false)
    {
        // Automatic expiry handoffs must ALWAYS obey Stop. A new, explicit
        // account-selection Start may replace a Stop from an earlier session.
        if(fromSwitch && !operatorInitiated && _operatorStopRequests.ContainsKey(device.Serial))
        { State(device,"STOPPED","Đã nhận lệnh Stop của người vận hành; không tự chuyển tài khoản.");return false; }
        if(_pendingDeviceRemovals.ContainsKey(device.Serial) ||
           !_settings.Devices.Any(d=>ReferenceEquals(d,device) ||
               d.Serial.Equals(device.Serial,StringComparison.OrdinalIgnoreCase)))
        { State(device,"REMOVING","Thiết bị đang được gỡ khỏi danh sách.");return false; }
        if(!fromSwitch && _pendingSwitches.ContainsKey(device.Serial))
        { State(device,"STOPPING","Đang chuyển tài khoản; đợi worker cũ kết thúc.");return false; }
        if(_workers.Contains(device.Serial))
        { State(device,"BUSY","Điện thoại vẫn đang chạy. Hãy Stop hoặc đợi chuyển phiên xong.");return false; }
        if(device.Connection!="ONLINE")
        { State(device,"OFFLINE","ADB chưa Online. Hãy kết nối thiết bị trước.");_log($"{device.Name}: {device.Detail}");return false; }
        EnsureUniqueSystemPort(device);
        if(manualResume && !onlyAccountId.HasValue)
            throw new InvalidOperationException("Manual Resume cần ID tài khoản cụ thể.");
        var next=manualResume && onlyAccountId.HasValue
            ? _vault.PreviewManualResume(device.Serial,onlyAccountId.Value,
                _settings.AccountPolicyFor(device).SessionLimitMinutes)
            : onlyAccountId.HasValue
            ? _vault.PreviewSpecific(device.Serial,onlyAccountId.Value)
            : _vault.PreviewNext(device.Serial);
        if(next is null)
        {
            var assigned=_vault.Snapshot().Where(a=>a.DeviceSerial==device.Serial).ToList();
            string reason=onlyAccountId.HasValue
                ? "Tài khoản tick không thuộc máy này hoặc chưa đủ điều kiện chạy (đang chạy, đang chờ retry, hoặc bị hạn chế). Không tự chọn tài khoản khác."
                : assigned.Count==0
                ? "Chưa có tài khoản nào được gán cho điện thoại này."
                : assigned.Any(a=>a.State=="RETRY_PENDING")
                    ? "Tài khoản lỗi kỹ thuật đang đợi thời gian thử lại (10–30 giây). " +
                      "Không cần nhập tài khoản mới."
                    : assigned.All(a=>a.State is "RESTRICTED" or "DISABLED")
                        ? "Các tài khoản được gán đều bị hạn chế hoặc vô hiệu hóa; không tự động đăng nhập lại."
                        : "Chưa có tài khoản sẵn sàng. Tài khoản NEEDS_REVIEW do xác minh/bảo mật " +
                          "cần được kiểm tra trước khi Đặt lại; không cần nhập tài khoản mới.";
            State(device,assigned.Any(a=>a.State=="RETRY_PENDING")?"WAIT_RETRY":"QUEUE_EMPTY",reason);
            _log($"{device.Name}: {reason}");return false;
        }
        if(!TikTokStateDetector.IsValidHandle(next.ExpectedTikTokHandle))
        { State(device,"WAIT_REVIEW","Thiếu TikTok handle dự kiến. Chọn tài khoản và nhập TikTok handle chính xác.");
          _log($"{device.Name}: {device.Detail}");return false; }
        if(!_deviceSessionLocks.TryAdd(device.Serial,0))
        { State(device,"BUSY","Đang có phiên Appium khác trên thiết bị.");_log($"{device.Name}: {device.Detail}");return false; }
        if(AccountVault.IsRecoverableInterruption(next))
            _log($"{device.Name}: phát hiện phiên gián đoạn trước đó; tự kiểm tra danh tính TikTok khi Start. Không tự coi login thành công.");
        var context=new DeviceContext(_settings,device,_adb,_log);
        var handle=new DeviceWorker(context);
        if(!_workers.TryAdd(handle))
        { _deviceSessionLocks.TryRemove(device.Serial,out _);handle.Dispose();return false;}
        // Clear the OLD Stop only after this explicit Start is accepted and
        // the worker + per-device Appium reservation have been registered.
        // Auto rotation (fromSwitch, without operatorInitiated) never clears it.
        if(!fromSwitch || operatorInitiated)
        {
            bool hadOldStop=_operatorStopRequests.TryRemove(device.Serial,out _);
            if(operatorInitiated)
                context.Log($"MANUAL_ACCOUNT_START_ACCEPTED: accountId={next.Id}; " +
                    $"oldStopCleared={hadOldStop}; workerRegistered=true; " +
                    "autoRotation=false; systemPort="+device.SystemPort);
        }
        device.WorkerStartedAt=DateTimeOffset.Now;
        device.LastError="";
        var work=Task.Run(async()=>
        {
            try
            {
                int recoveryAttempt=0;
                while(true)
                {
                    try
                    {
                        handle.SetState(DeviceWorkerState.Starting);
                        await RunDeviceLoopAsync(context,handle,onlyAccountId,manualResume);
                        break;
                    }
                    catch(Exception ex) when(!handle.Token.IsCancellationRequested &&
                        AppiumSessionFactory.IsFatalSessionException(ex))
                    {
                        recoveryAttempt++;
                        bool uiAutomatorHardReset=
                            AppiumFailureClassifier.IsUiAutomatorHardResetFailure(ex);
                        bool serverConnectionRefused=
                            AppiumFailureClassifier.IsConnectionRefusedFailure(ex);
                        device.LastError=ex.Message;
                        device.AppiumStatus="RECOVERING";
                        State(device,"RECOVERING_SESSION",$"Khôi phục Appium {recoveryAttempt}/{_settings.MaxSessionRecoveryAttempts}");
                        context.Log($"APPIUM_SESSION_LOST: attempt={recoveryAttempt}/{_settings.MaxSessionRecoveryAttempts}; {ex.Message}");

                        if(serverConnectionRefused)
                        {
                            State(device,"RECOVERING_SERVER",
                                "Appium 4723 từ chối kết nối; đang tự khởi động lại server.");
                            context.Log("APPIUM_SERVER_CONNECTION_REFUSED: watchdog=START; " +
                                "sessionRetry=PAUSED.");
                            bool serverReady=await AppiumServerWatchdog.EnsureRunningAsync(
                                _settings.AppiumUrl,context.Log,handle.Token);
                            if(serverReady)
                            {
                                // A verified server restart earns a fresh session
                                // recovery budget. Do not exhaust retries using
                                // attempts made while no server was listening.
                                recoveryAttempt=0;
                                context.Log("APPIUM_SERVER_RECOVERED: retryBudgetReset=true; " +
                                    "next=GLOBAL_SESSION_CREATE.");
                            }
                            else
                                context.Log("APPIUM_SERVER_RECOVERY_NOT_READY: " +
                                    "bounded session recovery will continue.");
                        }
                        if(recoveryAttempt>Math.Clamp(_settings.MaxSessionRecoveryAttempts,0,10))throw;
                        bool online=await EnsureDeviceOnlineAsync(device,handle.Token);
                        if(!online)
                        {
                            context.Log("ADB_RECONNECT_FAILED: thiết bị chưa online; không gửi thêm lệnh UI.");
                            if(recoveryAttempt>=_settings.MaxSessionRecoveryAttempts)
                                throw new InvalidOperationException("ADB mất kết nối sau các lần phục hồi Appium.",ex);
                        }
                        if(uiAutomatorHardReset && online)
                        {
                            // The await-using lease in RunDeviceLoopAsync has
                            // already disposed the dead session and released its
                            // systemPort before control reaches this handler.
                            context.Log("UIAUTOMATOR2_HARD_RESET: threshold=3; " +
                                "oldSessionReleased=true; forceStopTikTok=true; " +
                                "next=createFreshSessionAndRestartSearch.");
                            await _adb.ForceStopTikTokAsync(device.Serial,handle.Token);
                            context.Log("TIKTOK_FORCE_STOPPED_FOR_UI_STALL: deviceScoped=true.");
                            try
                            {
                                await _adb.RestartUiAutomator2DriverAsync(
                                    device.Serial,handle.Token);
                                context.Log("UIAUTOMATOR2_DRIVER_STOPPED: deviceScoped=true; " +
                                    "desktopAppiumServerUntouched=true.");
                            }
                            catch(OperationCanceledException) when(handle.Token.IsCancellationRequested){throw;}
                            catch(Exception driverEx)
                            {
                                context.Log("UIAUTOMATOR2_DRIVER_STOP_WARNING: " +
                                    driverEx.GetType().Name+"; fresh session creation will still be attempted.");
                            }
                            await _adb.RestartTikTokAsync(device.Serial,handle.Token);
                            State(device,"RECOVERING_SESSION",
                                "UiAutomator2 đã crash; đã mở lại TikTok, chuẩn bị tạo Appium session mới.");
                        }
                        // AccountVault applies a 10s/30s technical backoff. Wait for
                        // that same checkpoint before re-entering the unchanged flow.
                        int waitSeconds=recoveryAttempt<=1?11:31;
                        await Task.Delay(TimeSpan.FromSeconds(waitSeconds),handle.Token);
                    }
                }
            }
            catch(OperationCanceledException){_log($"{device.Name}: đã dừng theo yêu cầu.");}
            catch(Exception ex) when(AppiumSessionFactory.IsFatalSessionException(ex))
            {
                handle.SetState(DeviceWorkerState.Error);
                device.LastError=ex.Message;
                State(device,"ERROR","Appium session đã bị hủy. Dừng Inspector/worker khác cùng thiết bị, " +
                    "kiểm tra Appium Server rồi Start lại; kho tài khoản được giữ nguyên.");
                context.Log($"APPIUM_RECOVERY_EXHAUSTED: {ex.Message}");
            }
            catch(Exception ex)
            {
                handle.SetState(DeviceWorkerState.Error);device.LastError=ex.Message;
                State(device,"ERROR",ex.Message);context.Log($"WORKER_ERROR: {ex.Message}");
            }
            finally
            {
                _workers.TryRemove(device.Serial,out _);
                _deviceSessionLocks.TryRemove(device.Serial,out _);
                if(device.Stage is "RUNNING" or "STOPPING" or "WAIT_VERIFY" or "VERIFYING" or "PREFLIGHT")State(device,"STOPPED","Đã dừng phiên.");
                if(device.Stage!="ERROR")device.WorkerState=DeviceWorkerState.Idle.ToString();
                device.WorkerStartedAt=null;
                handle.Dispose();_notify();
            }
        });
        handle.Attach(work);
        if(onlyAccountId.HasValue)
            _log(manualResume
                ? $"{device.Name}: MANUAL_RESUME_LOGIN: chỉ tài khoản @{next.ExpectedTikTokHandle}; " +
                    "Google/Preflight hiện có, đồng hồ cộng dồn; sau khi hết giờ AUTO chọn tài khoản mới."
                : $"{device.Name}: chế độ TEST 1 tài khoản: chọn đúng {next.ExpectedTikTokHandle} ({next.Email}), không lấy từ hàng đợi.");
        _notify();return true;
    }

    /// <summary>
    /// Cancel an existing worker, WAIT until its Appium session and reservation
    /// have been released, then run exactly the selected account. Never overlap
    /// two sessions on one phone and never fall back to queue selection.
    /// </summary>
    public async Task<bool> SwitchToSelectedAccountAsync(DeviceRecord device,Guid accountId,
        bool manualResume=false)
    {
        if(!_pendingSwitches.TryAdd(device.Serial,0))
        {
            State(device,"BUSY","Đang thực hiện một yêu cầu chuyển tài khoản khác.");
            return false;
        }
        try
        {
            DeviceWorker? old;
            long stopVersionAtRequest;
            lock(_lifecycleGate)
            {
                var selected=_vault.Snapshot().FirstOrDefault(a=>a.Id==accountId);
                if(selected is null || !selected.DeviceSerial.Equals(device.Serial,StringComparison.OrdinalIgnoreCase))
                {
                    State(device,"WAIT_REVIEW","Tài khoản được tick chưa gán cho máy này.");
                    return false;
                }
                if(selected.State is "RESTRICTED" or "DISABLED")
                {
                    State(device,"WAIT_REVIEW","Tài khoản bị hạn chế; không tự thử đăng nhập lại.");
                    return false;
                }
                var permitted=manualResume
                    ? _vault.PreviewManualResume(device.Serial,accountId,
                        _settings.AccountPolicyFor(device).SessionLimitMinutes)
                    : _vault.PreviewSpecific(device.Serial,accountId);
                if(permitted is null)
                {
                    State(device,"WAIT_REVIEW",manualResume
                        ? "Chạy tiếp thủ công bị chặn: chưa xác nhận thời lượng, hết giờ hoặc tài khoản bị lỗi."
                        : "Tài khoản tick chỉ được Start lại khi lỗi UI kỹ thuật được nhận diện. " +
                          "Trường hợp khác cần kiểm tra; không dừng worker cũ.");
                    return false;
                }
                // Observe the latest Stop before the asynchronous wait. An
                // additional user Stop DURING that wait must veto this Start.
                stopVersionAtRequest=_operatorStopVersions.GetValueOrDefault(device.Serial);
                _log($"{device.Name}: MANUAL_ACCOUNT_START_REQUESTED: accountId={accountId}; " +
                    $"manualResume={manualResume}; stopVersion={stopVersionAtRequest}.");
                if(!manualResume && selected.State is "NEEDS_REVIEW" or "RETRY_PENDING")
                    _log($"{device.Name}: EXPLICIT_TECH_UI_RETRY_ACCEPTED: accountId={accountId}; " +
                        "onlySelected=true; autoQueueUnchanged=true; identityCheckRequired=true.");
                _workers.TryGet(device.Serial,out old);
                if(old is not null)
                {
                    _log($"{device.Name}: yêu cầu chuyển tài khoản -> dừng worker cũ rồi mới mở tài khoản tick.");
                    old.Cancel.Cancel();
                    State(device,"STOPPING","Chờ Appium đóng và giải phóng phiên cũ trước khi chạy tài khoản được tick.");
                }
            }
            if(old is not null)
            {
                // A still-running Appium call might take time to honour
                // cancellation. Timeout means leave old worker untouched.
                var finished=await Task.WhenAny(old.Work,Task.Delay(TimeSpan.FromSeconds(60)));
                if(finished!=old.Work || _workers.Contains(device.Serial) ||
                   _deviceSessionLocks.ContainsKey(device.Serial))
                {
                    State(device,"STOPPING","Worker cũ chưa thoát hết sau 60 giây. KHÔNG tạo Appium session thứ hai.");
                    _log($"{device.Name}: chưa thể chuyển tài khoản vì phiên cũ chưa giải phóng xong.");
                    return false;
                }
                await old.Work; // Surface unexpected worker errors if any.
                _log($"{device.Name}: worker cũ đã kết thúc; giữ nguyên tài khoản cũ ở trạng thái gián đoạn, không xóa kho.");
            }
            lock(_lifecycleGate)
            {
                // The old worker has already released its Appium reservation.
                // Stop issued after the manual request started takes precedence.
                long currentStopVersion=_operatorStopVersions.GetValueOrDefault(device.Serial);
                if(currentStopVersion!=stopVersionAtRequest)
                {
                    State(device,"STOPPED","Đã nhận lệnh Stop mới trong khi chờ chuyển tài khoản; " +
                        "đã hủy yêu cầu Start để tránh tự chạy ngoài ý muốn.");
                    _log($"{device.Name}: MANUAL_ACCOUNT_START_CANCELLED_BY_NEW_STOP: " +
                        $"before={stopVersionAtRequest}; now={currentStopVersion}.");
                    return false;
                }
                // Explicit selection is not the automatic expiry handoff.
                return StartUnderLock(device,accountId,fromSwitch:true,
                    manualResume:manualResume,operatorInitiated:true);
            }
        }
        finally { _pendingSwitches.TryRemove(device.Serial,out _); }
    }

    public async Task<TikTokObservation?> CheckSelectedStateAsync(DeviceRecord device,CancellationToken ct=default)
    {
        if(device.Connection!="ONLINE")throw new InvalidOperationException("ADB chưa Online.");
        if(!_deviceSessionLocks.TryAdd(device.Serial,0))
        {
            _log($"{device.Name}: đang có phiên Appium trên máy này; không kiểm tra chồng chéo.");
            return null;
        }
        try
        {
        var expected=_vault.Snapshot().FirstOrDefault(a=>a.DeviceSerial==device.Serial &&
            a.State is not ("RESTRICTED" or "DISABLED") &&
            TikTokStateDetector.IsValidHandle(a.ExpectedTikTokHandle));
        if(expected is null)
        {
            State(device,"WAIT_REVIEW","Chưa có tài khoản được gán với TikTok handle hợp lệ.");
            return null;
        }
        await using(await _parallel.AcquireAsync(ct))
        {
            var context=new DeviceContext(_settings,device,_adb,_log);
            await using var lease=await _sessions.CreateAsync(context,ct);
            var session=lease.Session;
            var currentPackage=await session.CurrentPackageAsync(ct);
            if(currentPackage!="com.ss.android.ugc.trill" &&
               currentPackage is not ("com.google.android.gms" or "com.google.android.gsf.login" or
                                      "com.android.chrome" or "com.google.android.googlequicksearchbox"))
            {
                await _adb.LaunchTikTokAsync(device.Serial,ct);
                _log($"{device.Name}: đã gửi lệnh mở TikTok; chờ UI Profile/Login tải xong (tối đa 90 giây).");
                if(!await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,120,
                    msg=>_log($"{device.Name}: {msg}"),
                    firstLaunchOnboarding:true,firstLaunchSelectors:_selectors))
                {
                    State(device,"WAIT_REVIEW","Chưa hoàn tất TikTok Home/Profile/Login sau 120 giây; " +
                        "kiểm tra màn hình đầu tiên trên Cloud Phone.");
                    return null;
                }
            }
            if(!await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                session,_selectors,ct,msg=>_log($"{device.Name}: {msg}")))
            {
                State(device,"WAIT_REVIEW","TikTok còn ở bước thiết lập ban đầu. " +
                    "Hoàn tất màn hình trên điện thoại rồi chạy kiểm tra lại; không xóa dữ liệu.");
                return null;
            }
            State(device,"PREFLIGHT","Đang kiểm tra Profile...");
            var flow=new LoginWorkflow(_selectors,msg=>_log($"{device.Name}: {msg}"),
                _adb,device.Serial);
            var reconciler=new SessionReconciler(new TikTokStateDetector(_selectors),
                new TikTokStateRecovery(_adb,msg=>_log($"{device.Name}: {msg}")),
                flow,_vault,msg=>_log($"{device.Name}: {msg}"));
            var observation=await reconciler.InspectAsync(session,device,expected,ct);
            State(device,observation.State.ToString().ToUpperInvariant(),observation.Detail);
            _log($"{device.Name} - TikTok State: {observation.State}: {observation.Detail}");
            return observation;
        }
        }
        finally{_deviceSessionLocks.TryRemove(device.Serial,out _);}
    }
    public void Stop(string serial)
    {
        // Serialize with registration of a new worker. A Stop issued while a
        // manual switch awaits teardown increments the version and invalidates
        // only that pending Start. Other devices remain independent.
        lock(_lifecycleGate)
        {
            _operatorStopRequests[serial]=0;
            _operatorStopVersions.AddOrUpdate(serial,1,(_,previous)=>previous+1);
            if(_workers.TryGet(serial,out var handle) && handle is not null)
            {
                handle.Stop();
                var d=_settings.Devices.FirstOrDefault(x=>x.Serial==serial);
                if(d is not null)State(d,"STOPPING","Chờ đóng phiên Appium.");
            }
        }
    }
    public void StopAll(){foreach(var d in _settings.Devices)Stop(d.Serial);}

    public async Task<(bool Success,string Detail)> RemoveDeviceAsync(
        DeviceRecord device,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(device);
        string serial=device.Serial;
        if(!_pendingDeviceRemovals.TryAdd(serial,0))
            return (false,"Thiết bị đang được gỡ bởi một thao tác khác.");
        try
        {
            DeviceWorker? worker=null;
            lock(_lifecycleGate)
            {
                _workers.TryGet(serial,out worker);
                worker?.Stop();
                State(device,"REMOVING","Đang dừng worker và giải phóng Appium session.");
            }
            if(worker is not null)
            {
                contextLog($"REMOVE_DEVICE_STOP_WORKER: serial={serial}; requested=true.");
                Task finished=await Task.WhenAny(worker.Work,
                    Task.Delay(TimeSpan.FromSeconds(60),ct));
                if(finished!=worker.Work)
                    return (false,"Worker chưa giải phóng Appium session sau 60 giây; chưa xóa thiết bị.");
                await worker.Work;
            }

            var deadline=DateTimeOffset.UtcNow.AddSeconds(15);
            while((_workers.Contains(serial) || _deviceSessionLocks.ContainsKey(serial) ||
                   _pendingSwitches.ContainsKey(serial)) && DateTimeOffset.UtcNow<deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(250,ct);
            }
            if(_workers.Contains(serial) || _deviceSessionLocks.ContainsKey(serial) ||
               _pendingSwitches.ContainsKey(serial))
                return (false,"Appium session hoặc thao tác chuyển tài khoản chưa giải phóng; chưa xóa thiết bị.");

            if(serial.Contains(':'))
            {
                try
                {
                    string reply=await _adb.DisconnectAsync(serial,ct);
                    contextLog($"REMOVE_DEVICE_ADB_DISCONNECT: serial={serial}; reply={reply}.");
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
                catch(Exception ex)
                {
                    // The endpoint may already be offline. Persisting its removal
                    // is still safe after the worker/session are fully gone.
                    contextLog($"REMOVE_DEVICE_ADB_DISCONNECT_WARNING: serial={serial}; " +
                        $"error={ex.GetType().Name}.");
                }
            }

            lock(_lifecycleGate)
            {
                if(_workers.Contains(serial) || _deviceSessionLocks.ContainsKey(serial))
                    return (false,"Thiết bị vừa phát sinh session mới; hủy thao tác xóa an toàn.");
                _settings.Devices.RemoveAll(d=>
                    d.Serial.Equals(serial,StringComparison.OrdinalIgnoreCase));
                if(!_settings.RemovedDeviceSerials.Contains(serial,StringComparer.OrdinalIgnoreCase))
                    _settings.RemovedDeviceSerials.Add(serial);
                Storage.SaveSettings(_settings);
            }
            contextLog($"REMOVE_DEVICE_COMPLETED: serial={serial}; settingsSaved=true; " +
                "accountPoolPreserved=true.");
            _notify();
            return (true,"Đã gỡ thiết bị khỏi danh sách quản lý và lưu cấu hình.");
        }
        finally {_pendingDeviceRemovals.TryRemove(serial,out _);}

        void contextLog(string message) =>
            _log($"[{serial}] [{device.Name}] {message}");
    }
    async Task RunDeviceLoopAsync(DeviceContext context,DeviceWorker worker,Guid? onlyAccountId=null,
        bool manualResume=false)
    {
        CancellationToken workerToken=worker.Token;
        DeviceRecord d=context.Device;
        bool rotatingAfterInvalid=false;
        await using(await AcquireWorkerSlotAsync(worker))
        {
            while(!workerToken.IsCancellationRequested)
            {
                // Separate deadline cancellation for each account; never cancel
                // another phone's worker or the parent stop token.
                using var sessionDeadline=new CancellationTokenSource();
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(workerToken,sessionDeadline.Token);
                CancellationToken ct=linked.Token;
                ManagedAccount? account=rotatingAfterInvalid
                    ? _vault.ReserveNextAvailable(d.Serial)
                    : manualResume && onlyAccountId.HasValue
                    ? _vault.ReserveManualResume(d.Serial,onlyAccountId.Value,
                        _settings.AccountPolicyFor(d).SessionLimitMinutes)
                    : onlyAccountId.HasValue
                    ? _vault.ReserveSpecific(d.Serial,onlyAccountId.Value)
                    : _vault.ReserveNext(d.Serial);
                if(account is null)
                {
                    if(rotatingAfterInvalid)
                    {
                        const string exhausted="Thiết bị hết tài khoản hợp lệ sau khi tài khoản cũ lỗi; dừng worker.";
                        State(d,"ACCOUNT_POOL_EXHAUSTED",exhausted);
                        context.Log("ACCOUNT_POOL_EXHAUSTED: "+exhausted);
                        return;
                    }
                    var assigned=_vault.Snapshot().Where(a=>a.DeviceSerial==d.Serial).ToList();
                    string reason=assigned.Any(a=>a.State=="RETRY_PENDING")
                        ? "Đang chờ thử lại lỗi kỹ thuật; vui lòng Start lại sau thời gian chờ."
                        : "Không còn tài khoản sẵn sàng; các tài khoản cần kiểm tra không bị xóa khỏi kho.";
                    State(d,assigned.Any(a=>a.State=="RETRY_PENDING")?"WAIT_RETRY":"QUEUE_EMPTY",reason);
                    return;
                }
                // Snapshot the policy at account reservation: changing a setting on
                // Phone 02 never resets an active session on Phone 03.
                var accountPolicy=_settings.AccountPolicyFor(d).Copy();
                d.AccountEmail=account.Email;
                d.StartedAt=null;
                State(d,"CONNECTING","Khởi tạo Appium session...");
                bool completed=false;
                bool reachedSessionLimit=false;
                bool unexpectedLiveReturn=false;
                DeviceAccountClock? activeClock=null;
                try
                {
                    var connected=await _adb.DevicesAsync(ct);
                    if(!connected.Contains(d.Serial,StringComparer.OrdinalIgnoreCase))
                    {
                        // Technical reconnection is safe here because no login actions have been sent.
                        if(d.Serial.Contains(':')) await _adb.ConnectAsync(d.Serial,ct);
                        connected=await _adb.DevicesAsync(ct);
                        if(!connected.Contains(d.Serial,StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException("ADB chưa kết nối đến điện thoại.");
                    }
                    await using var lease=await _sessions.CreateAsync(context,ct);
                    var session=lease.Session;
                    // FAST TEST: apply the same verified 1-second UiAutomator2 idle
                    // responsiveness as LIVE, but ONLY within this device session.
                    // The default is restored before the existing LIVE handoff.
                    await using var fastPreflight=await FastPreflightIdleScope.EnableAsync(
                        session,msg=>_log($"{d.Name}: {msg}"),ct);
                    context.Device.WorkerState=DeviceWorkerState.Running.ToString();
                    // A Google activity can be an abandoned flow from an older
                    // worker. Leave it untouched here; the Preflight reconciler
                    // performs bounded Back/restart/clear recovery before login.
                    string currentPackage=await session.CurrentPackageAsync(ct);
                    if(currentPackage!="com.ss.android.ugc.trill" &&
                       currentPackage is not ("com.google.android.gms" or "com.google.android.gsf.login" or
                                              "com.android.chrome" or "com.google.android.googlequicksearchbox"))
                    {
                        await _adb.LaunchTikTokAsync(d.Serial,ct);
                        _log($"{d.Name}: đã gửi lệnh mở TikTok; chờ UI Profile/Login tải xong (tối đa 90 giây).");
                        if(!await TikTokForegroundProbe.WaitForReadyUiAsync(session,ct,120,
                            msg=>_log($"{d.Name}: {msg}"),
                            firstLaunchOnboarding:true,firstLaunchSelectors:_selectors))
                        {
                            _vault.SetStatus(account.Id,"NEEDS_REVIEW",
                                "Chưa hoàn tất TikTok Home/Profile/Login sau thiết lập Cloud Phone. Không xóa dữ liệu.");
                            _checkpoints.Record(d.Serial,account.Id,"WAIT_ONBOARDING");
                            State(d,"WAIT_REVIEW","TikTok chưa hoàn tất thiết lập sau 120 giây; " +
                                "kiểm tra trực tiếp màn hình máy mới.");
                            return;
                        }
                    }
                    if(!await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                        session,_selectors,ct,msg=>_log($"{d.Name}: {msg}")))
                    {
                        _vault.SetStatus(account.Id,"NEEDS_REVIEW",
                            "Cloud Phone mới chưa hoàn tất TikTok Terms / onboarding; không xóa dữ liệu hoặc nhập lại tài khoản.");
                        _checkpoints.Record(d.Serial,account.Id,"WAIT_ONBOARDING");
                        State(d,"WAIT_REVIEW","Thiết lập TikTok chưa hoàn tất. " +
                            "Mở điện thoại kiểm tra và sau đó Đặt lại tài khoản khi cần.");
                        return;
                    }
                    if(rotatingAfterInvalid)
                    {
                        State(d,"ROTATE_PROFILE_HANDOFF",
                            "Tài khoản mới: đang rời Feed và mở Profile để kích hoạt đăng nhập.");
                        bool profileLoginReady=await TikTokForegroundProbe.OpenProfileAfterAccountRotationAsync(
                            session,_selectors,ct,msg=>_log($"{d.Name}: {msg}"));
                        if(!profileLoginReady)
                        {
                            context.Log("ROTATE_PROFILE_RESTART: Profile chưa mở sau 3 tap; restart TikTok một lần.");
                            await _adb.RestartTikTokAsync(d.Serial,ct);
                            if(!await TikTokForegroundProbe.HandleNewDeviceOnboardingIfPresentAsync(
                                session,_selectors,ct,msg=>_log($"{d.Name}: {msg}")))
                                throw new InvalidOperationException(
                                    "ROTATE_PROFILE_ONBOARDING_NOT_READY: TikTok chưa hoàn tất thiết lập sau restart.");
                            profileLoginReady=await TikTokForegroundProbe.OpenProfileAfterAccountRotationAsync(
                                session,_selectors,ct,msg=>_log($"{d.Name}: {msg}"));
                        }
                        if(!profileLoginReady)
                            throw new InvalidOperationException(
                                "ROTATE_PROFILE_NAVIGATION_FAILED: đã thử 2 chu kỳ có giới hạn nhưng chưa mở được Profile/Login.");
                        State(d,"ROTATE_PROFILE_READY",
                            "Profile/Login đã sẵn sàng; tiếp tục tài khoản mới trong local pool.");
                    }
                    var flow=new LoginWorkflow(_selectors,msg=>_log($"{d.Name}: {msg}"),
                        _adb,d.Serial);
                    var reconciler=new SessionReconciler(
                        new TikTokStateDetector(_selectors),
                        new TikTokStateRecovery(_adb,msg=>_log($"{d.Name}: {msg}")),
                        flow,_vault,msg=>_log($"{d.Name}: {msg}"));
                    State(d,"PREFLIGHT","TỰ ĐỘNG kiểm tra TikTok và đối chiếu handle trước khi chạy.");
                    _log($"{d.Name}: Start -> tự kiểm tra Profile và tài khoản hiện tại; không cần bấm Kiểm tra TikTok.");
                    _checkpoints.Record(d.Serial,account.Id,"PREFLIGHT");
                    var preflightWatch=System.Diagnostics.Stopwatch.StartNew();
                    var reconcile=await reconciler.ReconcileAsync(session,d,account,ct);
                    _log($"{d.Name}: FAST_PREFLIGHT_RESULT: elapsedMs={preflightWatch.ElapsedMilliseconds}; outcome={reconcile.Outcome}; idleScope=ACCOUNT_PREFLIGHT.");
                    _log($"{d.Name}: Preflight kết thúc -> {reconcile.Outcome}: {reconcile.Detail}");
                    if(reconcile.Outcome==ReconcileOutcome.AccountInvalid)
                    {
                        _vault.SetStatus(account.Id,"INVALID",reconcile.Detail);
                        _checkpoints.Record(d.Serial,account.Id,"ACCOUNT_INVALID");
                        State(d,"ACCOUNT_ROTATION_CLEAR_DATA",
                            "Tài khoản Google lỗi; đang làm sạch TikTok và Google Play Services để lấy tài khoản kế tiếp.");
                        context.Log($"ACCOUNT_INVALID: account={account.Email}; reason={reconcile.Detail}; " +
                            "workerContinues=true; nextAccount=true.");
                        completed=true;
                        rotatingAfterInvalid=true;
                        onlyAccountId=null;
                        await _adb.ClearTikTokAppDataAsync(d.Serial,ct);
                        context.Log("ACCOUNT_ROTATION_CLEAR_TIKTOK_OK: pm clear com.ss.android.ugc.trill = Success.");
                        await _adb.ClearGooglePlayServicesDataAsync(d.Serial,ct);
                        context.Log("ACCOUNT_ROTATION_CLEAR_GMS_OK: pm clear com.google.android.gms = Success.");
                        await Task.Delay(2000,ct);
                        await _adb.LaunchTikTokAsync(d.Serial,ct);
                        continue;
                    }
                    if(reconcile.Outcome==ReconcileOutcome.AccountCheckpoint)
                    {
                        _vault.SetStatus(account.Id,"ACCOUNT_CHECKPOINT",reconcile.Detail);
                        _checkpoints.Record(d.Serial,account.Id,"ACCOUNT_CHECKPOINT");
                        State(d,"ACCOUNT_CHECKPOINT","Tài khoản bị checkpoint; tự động bỏ qua và lấy tài khoản kế tiếp.");
                        context.Log($"ACCOUNT_CHECKPOINT_SKIP: account={account.Email}; " +
                            "workerContinues=true; nextAccount=true.");
                        completed=true;
                        // A user may have started one explicitly ticked account. A
                        // confirmed checkpoint is the one case where continuing
                        // that same reservation is impossible, so resume the local
                        // device queue instead of killing the worker.
                        rotatingAfterInvalid=true;
                        onlyAccountId=null;
                        continue;
                    }
                    if(reconcile.Outcome is ReconcileOutcome.Restricted)
                    {
                        _vault.SetStatus(account.Id,"RESTRICTED",reconcile.Detail);
                        _checkpoints.Record(d.Serial,account.Id,"RESTRICTED");
                        State(d,"RESTRICTED",reconcile.Detail);return;
                    }
                    if(reconcile.Outcome==ReconcileOutcome.TechnicalFailure)
                    {
                        _vault.MarkTechnicalFailure(account.Id,reconcile.Detail);
                        _checkpoints.Record(d.Serial,account.Id,"RETRY_PENDING");
                        State(d,"RETRY_PENDING",reconcile.Detail+" | Có thể Start lại sau thời gian chờ.");
                        return;
                    }
                    if(reconcile.Outcome==ReconcileOutcome.NeedsReview)
                    {
                        _vault.SetStatus(account.Id,"NEEDS_REVIEW",reconcile.Detail);
                        _checkpoints.Record(d.Serial,account.Id,"NEEDS_REVIEW");
                        State(d,"WAIT_REVIEW",reconcile.Detail+" | Sau khi kiểm tra có thể Đặt lại, không cần account mới.");return;
                    }
                    if(reconcile.Outcome is ReconcileOutcome.NeedsHuman)
                    {
                        State(d,"WAIT_VERIFY",reconcile.Detail + " | Đang tự kiểm tra lại tối đa 3 phút.");
                        _log($"{d.Name}: Google Login cần chờ xác minh: {reconcile.Detail}");
                        var verification=await reconciler.WaitForVerifiedProfileAsync(
                            session,d,account,detail=>State(d,"WAIT_VERIFY",detail),ct);
                        if(verification.Outcome==ReconcileOutcome.AccountCheckpoint)
                        {
                            _vault.SetStatus(account.Id,"ACCOUNT_CHECKPOINT",verification.Detail);
                            _checkpoints.Record(d.Serial,account.Id,"ACCOUNT_CHECKPOINT");
                            State(d,"ACCOUNT_CHECKPOINT","Tài khoản bị checkpoint; tự động bỏ qua và lấy tài khoản kế tiếp.");
                            context.Log($"ACCOUNT_CHECKPOINT_SKIP: account={account.Email}; " +
                                "workerContinues=true; nextAccount=true.");
                            completed=true;
                            rotatingAfterInvalid=true;
                            onlyAccountId=null;
                            continue;
                        }
                        if(verification.Outcome==ReconcileOutcome.Restricted)
                        {
                            _vault.SetStatus(account.Id,"RESTRICTED",verification.Detail);
                            _checkpoints.Record(d.Serial,account.Id,"RESTRICTED");
                            State(d,"RESTRICTED",verification.Detail);return;
                        }
                        if(verification.Outcome!=ReconcileOutcome.Ready)
                        {
                            if(verification.Outcome==ReconcileOutcome.TechnicalFailure)
                            {
                                _vault.MarkTechnicalFailure(account.Id,verification.Detail);
                                _checkpoints.Record(d.Serial,account.Id,"RETRY_PENDING");
                                State(d,"RETRY_PENDING",verification.Detail);
                            }
                            else
                            {
                                _vault.SetStatus(account.Id,"NEEDS_REVIEW",verification.Detail);
                                _checkpoints.Record(d.Serial,account.Id,"NEEDS_REVIEW");
                                State(d,"WAIT_REVIEW",verification.Detail);
                            }
                            return;
                        }
                    }
                    _checkpoints.Record(d.Serial,account.Id,"IDENTITY_VERIFIED");
                    var profileOptions=ProfileUpdateStore.Read();
                    if(!_settings.AutoUpdateProfile)
                    {
                        State(d,"READY","Đã xác minh tài khoản; bỏ qua cập nhật Profile theo cấu hình.");
                        _checkpoints.Record(d.Serial,account.Id,"PROFILE_UPDATE_SKIPPED");
                        _log($"{d.Name}: PROFILE_UPDATE_SKIPPED: AutoUpdateProfile=false; " +
                            "identity remains verified; continuing ready workflow.");
                    }
                    else if(profileOptions.Enabled && (profileOptions.ChangeName || profileOptions.ChangeBio || profileOptions.ChangePhoto))
                    {
                        State(d,"PROFILE_UPDATE","Đang cập nhật hồ sơ sau khi xác minh tài khoản.");
                        _checkpoints.Record(d.Serial,account.Id,"PROFILE_UPDATE");
                        try
                        {
                            var updater=new ProfileUpdateWorkflow(_selectors,msg=>_log($"{d.Name}: {msg}"));
                            var update=await updater.ExecuteAsync(session,_adb,d,account,profileOptions,_profileLedger,_vault,ct);
                            if(!update.Success)
                            {
                                if(update.Retryable)
                                {
                                    // Technical UI failure: preserve per-field journal and retry
                                    // without re-submitting a possibly successful name Save.
                                    _vault.MarkTechnicalFailure(account.Id,"Cập nhật hồ sơ gián đoạn kỹ thuật: "+update.Detail);
                                    _checkpoints.Record(d.Serial,account.Id,"RETRY_PENDING");
                                    State(d,"RETRY_PENDING",update.Detail);
                                }
                                else
                                {
                                    _vault.SetStatus(account.Id,"NEEDS_REVIEW","Cập nhật hồ sơ chưa hoàn tất: "+update.Detail);
                                    _checkpoints.Record(d.Serial,account.Id,"PROFILE_REVIEW");
                                    State(d,"PROFILE_REVIEW",update.Detail);
                                }
                                _log($"{d.Name}: chưa hoàn tất hồ sơ: {update.Detail}");
                                return;
                            }
                            _checkpoints.Record(d.Serial,account.Id,update.Deferred ? "PROFILE_PARTIAL" : "PROFILE_VERIFIED");
                            _log($"{d.Name}: {update.Detail}");
                        }
                        catch(Exception ex) when(ex is IOException or ArgumentException or InvalidOperationException or OutOfMemoryException)
                        {
                            _vault.SetStatus(account.Id,"NEEDS_REVIEW","Cập nhật hồ sơ dừng: "+ex.Message);
                            _checkpoints.Record(d.Serial,account.Id,"PROFILE_REVIEW");
                            State(d,"PROFILE_REVIEW",ex.Message);
                            _log($"{d.Name}: cập nhật hồ sơ: {ex.Message}");
                            return;
                        }
                    }
                    // A policy saved during Google login/Preflight takes effect
                    // before the verified session timer is first registered.
                    accountPolicy=_settings.AccountPolicyFor(d).Copy();
                    var deadline=_vault.StartVerifiedTimedSession(account.Id,
                        accountPolicy.SessionLimitMinutes);
                    _checkpoints.Record(d.Serial,account.Id,"ACTIVE");
                    // Deadline is a wall-clock limit, not a per-screen timeout;
                    // accounts cannot reset it by restarting UI navigation.
                    var verifiedAccount=_vault.Snapshot(d.Serial).Single(a=>a.Id==account.Id);
                    var currentClock=new DeviceAccountClock(d,_vault,account.Id,
                        verifiedAccount.SessionStartedAtUtc!.Value,deadline,accountPolicy,
                        sessionDeadline,account.ExpectedTikTokHandle,"START_ACCOUNT",_notify);
                    activeClock=currentClock;
                    if(!_activeClocks.TryAdd(d.Serial,currentClock))
                        throw new InvalidOperationException("Máy đã có bộ đếm vòng đời đang chạy.");
                    context.Log($"ACCOUNT_SESSION_STARTED: device={d.Name}; deviceId={d.DeviceId}; " +
                        $"limitMinutes={accountPolicy.SessionLimitMinutes}; " +
                        $"deadlineUtc={deadline:O}; autoRotate={accountPolicy.AutoRotateAccounts}; " +
                        $"testOnly={onlyAccountId.HasValue}; singleUse={accountPolicy.NeverReuseUsedAccounts}");
                    d.StartedAt=DateTimeOffset.Now;
                    State(d,"LIVE_SETTLING","Đã xác minh tài khoản; chờ TikTok ổn định trước khi tìm LIVE.");
                    _log($"{d.Name}: AUTO_LIVE_TRANSITION_WAIT: delayMs=2500; cancellationAware=true.");
                    await Task.Delay(2500,ct);
                    ct.ThrowIfCancellationRequested();

                    // Last bounded guard before the automatic LIVE handoff. A
                    // delayed one-time policy dialog can appear after profile
                    // verification and otherwise cover the Home Search icon.
                    if(await LivePopupHandler.DismissVirtualItemsPoliciesAsync(session,
                        msg=>_log($"{d.Name}: {msg}"),ct))
                        _log($"{d.Name}: AUTO_LIVE_HOME_POLICY_CLEANUP_COMPLETE: Search entry will be re-evaluated.");
                    ct.ThrowIfCancellationRequested();
                    // The existing LIVE engine keeps ownership of its separate
                    // SAFE_IDLE scope and safety checks; never carry preflight
                    // setting ownership into LIVE.
                    await fastPreflight.RestoreAsync();
                    ct.ThrowIfCancellationRequested();

                    var liveConfig=LiveManagerSettingsStore.Load();
                    if(!_settings.ForceLiveIdleOneSecond)
                    {
                        _settings.ForceLiveIdleOneSecond=true;
                        Storage.SaveSettings(_settings);
                    }
                    State(d,"LIVE_IDLE_1S","Đang áp dụng và xác minh Appium waitForIdleTimeout=1000ms.");
                    _log($"{d.Name}: AUTO_LIVE_IDLE_FORCED: flag=true; targetMs=1000; " +
                        "applyBefore=SEARCH_GRID.");
                    await LiveVerifiedSessionHandoff.RunAsync(session,liveConfig,_adb,d.Serial,
                        msg=>_log($"{d.Name}: {msg}"),ct,()=>
                        {
                            State(d,"SEARCH_GRID","Idle 1s đã xác minh; tự động tìm LIVE bằng từ khóa.");
                            _checkpoints.Record(d.Serial,account.Id,"AUTO_LIVE_SEARCH");
                            _log($"{d.Name}: AUTO_LIVE_TRANSITION_START: state=SEARCH_GRID; " +
                                "idleMs=1000; verified=true; session=reused; manualClick=false.");
                        },GlobalRoomSlotManager.Shared,_settings.MaxDevicesPerLiveRoom,
                        d.DeviceId.ToString("N"),()=>_settings.MaxDevicesPerLiveRoom);
                    ct.ThrowIfCancellationRequested();
                    unexpectedLiveReturn=true;
                    State(d,"WAIT_RECOVERY","LIVE đã thoát trước hạn phiên; dừng để kiểm tra, không tự đổi tài khoản.");
                }
                catch(OperationCanceledException) when(sessionDeadline.IsCancellationRequested && !workerToken.IsCancellationRequested)
                {
                    reachedSessionLimit=true;
                    completed=true;
                    accountPolicy=activeClock?.Policy ?? accountPolicy;
                    _vault.EndUsedSession(account.Id,
                        $"Đã đủ {accountPolicy.SessionLimitMinutes} phút; kết thúc phiên theo cấu hình {d.Name}",
                        accountPolicy.PurgeUsedAfterHours);
                    _checkpoints.Record(d.Serial,account.Id,"SESSION_TIME_LIMIT_REACHED");
                    State(d,"SESSION_TIME_LIMIT_REACHED","Phiên hết giờ; chuẩn bị chuyển tài khoản mới.");
                    context.Log("ACCOUNT_SESSION_EXPIRED: identity retained in history; account cannot be selected again.");
                }
                finally
                {
                    accountPolicy=activeClock?.Policy ?? accountPolicy;
                    DetachClock(d,activeClock);
                    if(!completed)
                    {
                        // In either case the next Start MUST re-check the actual
                        // TikTok identity. A user Stop is INTERRUPTED; an uncaught
                        // ADB/Appium exception is retryable, with a bounded budget.
                        if(_vault.Snapshot().Any(x=>x.Id==account.Id && (x.State is "RESERVED" or "ACTIVE")))
                        {
                            if(ct.IsCancellationRequested)
                            {
                                _vault.SetStatus(account.Id,"INTERRUPTED",
                                    "Dừng giữa phiên; đã sử dụng, không tự chọn lại khi bật Single Use");
                                _checkpoints.Record(d.Serial,account.Id,"INTERRUPTED");
                            }
                            else
                            {
                                _vault.MarkTechnicalFailure(account.Id,"Lỗi kỹ thuật chưa hoàn tất trong worker; cần kiểm tra ADB/Appium/UI");
                                _checkpoints.Record(d.Serial,account.Id,"RETRY_PENDING");
                            }
                        }
                    }
                    d.AccountEmail="";d.StartedAt=null;_notify();
                }
                if(unexpectedLiveReturn)
                {
                    context.Log("ACCOUNT_SESSION_LIVE_EXIT_UNEXPECTED: no rotation; check UI/session health.");
                    return;
                }
                if(reachedSessionLimit)
                {
                    try
                    {
                        ct=workerToken; // Must use the unexpired worker token during reset.
                        State(d,"ACCOUNT_ROTATION_CLEAR_DATA","Đã hết giờ, xóa dữ liệu TikTok/GMS trên đúng điện thoại.");
                        await _adb.ClearTikTokAppDataAsync(d.Serial,ct);
                        await _adb.ClearGooglePlayServicesDataAsync(d.Serial,ct);
                        _vault.ScheduleCompletedPurge(account.Id,accountPolicy.PurgeUsedAfterHours);
                        context.Log("ACCOUNT_ROTATION_AFTER_LIMIT: pm clear TikTok + GMS = Success");
                        if(!accountPolicy.AutoRotateAccounts || (onlyAccountId.HasValue && !manualResume))
                        {
                            State(d,"SESSION_DONE","Đã xóa dữ liệu khi hết giờ; TEST một tài khoản hoặc tự chuyển bị tắt.");
                            return;
                        }
                        // Next iteration uses the unchanged normal Google Login flow.
                        rotatingAfterInvalid=true;
                        continue;
                    }
                    catch(OperationCanceledException) when(workerToken.IsCancellationRequested){throw;}
                    catch(Exception ex)
                    {
                        State(d,"WAIT_RECOVERY","Phiên đã hết giờ nhưng xóa dữ liệu thất bại; dừng để kiểm tra.");
                        context.Log("ACCOUNT_ROTATION_CLEAR_FAILED: "+ex.GetType().Name+"; "+ex.Message);
                        return; // No blind start of another account on uncleared device.
                    }
                }
                // Explicit test mode runs exactly ONE selected account.
                if(onlyAccountId.HasValue)return;
            }
        }
    }
    async Task<bool> EnsureDeviceOnlineAsync(DeviceRecord device,CancellationToken ct)
    {
        var connected=await _adb.DevicesAsync(ct);
        if(connected.Contains(device.Serial,StringComparer.OrdinalIgnoreCase))return true;
        if(device.Serial.Contains(':'))
        {
            try{await _adb.ConnectAsync(device.Serial,ct);}catch(Exception ex)
            { new DeviceContext(_settings,device,_adb,_log).Log("ADB_RECONNECT_ERROR: "+ex.Message); }
        }
        connected=await _adb.DevicesAsync(ct);
        return connected.Contains(device.Serial,StringComparer.OrdinalIgnoreCase);
    }

    void EnsureUniqueSystemPort(DeviceRecord device)
    {
        bool conflict=device.SystemPort is <8200 or >8999 ||
            _settings.Devices.Any(other=>!ReferenceEquals(other,device) && other.SystemPort==device.SystemPort);
        if(!conflict)return;
        int previous=device.SystemPort,available=8200;
        var used=_settings.Devices.Where(other=>!ReferenceEquals(other,device))
            .Select(other=>other.SystemPort).ToHashSet();
        while(used.Contains(available))available++;
        device.SystemPort=available;
        Storage.SaveSettings(_settings);
        new DeviceContext(_settings,device,_adb,_log).Log(
            $"SYSTEM_PORT_REALLOCATED_BEFORE_START: previous={previous}; current={available}");
    }

    public void Dispose(){StopAll();_parallel.Dispose();}
}
