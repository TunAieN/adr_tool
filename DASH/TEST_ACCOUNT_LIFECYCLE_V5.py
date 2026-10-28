"""Offline source checks for V5 account lifecycle; not a .NET compiler."""
from pathlib import Path
p=Path(__file__).parent/'AndroidMultiDeviceV1'
orch=(p/'Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
clock=(p/'Core/DeviceAccountClock.cs').read_text(encoding='utf-8-sig')
vault=(p/'Core/Storage.cs').read_text(encoding='utf-8-sig')
model=(p/'Core/Models.cs').read_text(encoding='utf-8-sig')
ui=(p/'MainForm.cs').read_text(encoding='utf-8-sig')
dash=(p/'DeviceDashboardControl.cs').read_text(encoding='utf-8-sig')
checks={
 'start_live_proves_owner':'VerifyLiveOwnerAsync(context,worker.Token)' in orch and 'TikTokStateDetector(_selectors).DetectAsync' in orch,
 'no_unsafe_unidentified_clear':'LIVE_IDENTITY_UNVERIFIED:' in orch and 'if(sessionExpired && !worker.Token.IsCancellationRequested)' in orch,
 'exact_per_device_match':'_vault.Snapshot(device.Serial)' in orch and 'matched.Count!=1' in orch and '_vault.ReserveSpecific(device.Serial,verified.Id)' in orch,
 'rejects_used_live_account':'_vault.PreviewSpecific(device.Serial,candidate.Id) is null' in orch,
 'direct_live_starts_clock':'new DeviceAccountClock(device,_vault,liveAccount.Id' in orch and 'LIVE_ACCOUNT_SESSION_STARTED:' in orch,
 'existing_start_uses_clock':'new DeviceAccountClock(d,_vault,account.Id' in orch and 'StartVerifiedTimedSession(account.Id,' in orch,
 'deadline_cancel_per_worker':'CancellationTokenSource.CreateLinkedTokenSource(worker.Token,timedOut.Token)' in orch and 'timedOut.IsCancellationRequested' in orch,
 'pm_clear_after_expiry':'if(sessionExpired && !worker.Token.IsCancellationRequested)' in orch and 'ClearTikTokAppDataAsync(device.Serial,worker.Token)' in orch,
 'normal_start_after_clear':'startNext=clearSucceeded' in orch and 'StartUnderLock(device,fromSwitch:true)' in orch,
 'operator_stop_blocks_handoff':'_operatorStopRequests.ContainsKey(device.Serial)' in orch and '_operatorStopRequests[serial]=0' in orch,
 'per_device_clock':'ConcurrentDictionary<string,DeviceAccountClock>' in orch and 'AccountId { get; }' in clock and 'StartedUtc { get; }' in clock,
 'apply_now_persists':'UpdateActiveSessionDeadline(AccountId, deadline)' in clock and 'UpdateActiveSessionDeadline(Guid accountId' in vault,
 'apply_now_keeps_origin':'deadline=StartedUtc.AddMinutes' in clock and 'a.SessionStartedAtUtc=now' in vault,
 'apply_next_choice':'Từ phiên tài khoản tiếp theo' in ui and 'Ngay cho phiên đã xác minh' in ui and 'ApplyLifecycleToRunning(target' in ui,
 'dashboard_distinguishes_configuration':'Bộ đếm vòng đời: CHƯA KÍCH HOẠT' in dash and 'Giới hạn đang áp dụng:' in dash and 'Interval = 1000' in dash,
 'runtime_not_persisted':'[JsonIgnore] public DateTimeOffset? LifecycleDeadlineUtc' in model,
 'no_live_engine_edit':(p/'Core/LiveSearchEngine.cs').exists() and (p/'Core/LiveAutonomousWorkflow.cs').exists(),
}
for name,passed in checks.items(): print(('PASS' if passed else 'FAIL')+': '+name)
assert all(checks.values())
print('NOTE: Source checks only, requires .NET 8 Windows build and one-device functional tests.')
