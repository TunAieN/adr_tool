"""V5.1 structural regression assertions. Does NOT compile or run C# code."""
from pathlib import Path
p=Path(__file__).parent/'AndroidMultiDeviceV1'
read=lambda f:(p/f).read_text(encoding='utf-8-sig')
vault=read('Core/Storage.cs'); model=read('Core/Models.cs');orch=read('Core/DeviceOrchestrator.cs'); ui=read('MainForm.cs');clock=read('Core/DeviceAccountClock.cs')
checks={
'auto_fresh_only':'AUTO is always fresh-only' in vault and 'return pool.FirstOrDefault(a=>a.State=="AVAILABLE" && !WasUsed(a))' in vault and 'item.State=="AVAILABLE" && !WasUsed(item)' in vault,
'manual_separate':'ReserveManualResume(string serial,Guid id' in vault and 'PreviewManualResume(string serial,Guid id' in vault and 'manualResumeAccountId' in orch,
'manual_login_via_existing_preflight':'ReserveManualResume(d.Serial,onlyAccountId.Value' in orch and 'manualResume:manualResume' in orch and 'mode==DialogResult.Yes' in ui,
'owner_match_required':'MANUAL_RESUME_IDENTITY_MISMATCH' in orch and 'candidate.Id!=manualResumeAccountId.Value' in orch and 'seen.VerifiedHandle' in orch,
'limits_accumulate':'AccumulatedRunSeconds' in model and 'SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow)' in vault and 'remainingSeconds=Math.Max(0,budget*60-a.AccumulatedRunSeconds)' in vault,
'no_silent_legacy_zero':'RunDurationKnown=false' in vault and 'ConfirmLegacyRunMinutes' in vault and 'operatorReviewed=true' in ui,
'no_repeat_confirm':'if(!WasUsed(a) || a.RunDurationKnown' in vault,
'defer_purge':'a.CredentialPurgeDueAtUtc=null; // only after pm clear succeeds' in vault and 'ScheduleCompletedPurge(liveAccount.Id' in orch and 'ScheduleCompletedPurge(account.Id' in orch,
'only_purge_completed':'a.State=="COMPLETED"' in vault and 'a.CredentialPurgeDueAtUtc.HasValue' in vault,
'pause_no_purge':'PauseVerifiedSession(liveAccount.Id' in orch and 'a.CredentialPurgeDueAtUtc=null; // pause != expired' in vault,
'rotation_after_manual_expiry':'(onlyAccountId.HasValue && !manualResume)' in orch and 'rotatingAfterInvalid=true;' in orch,
'ui_manual_explicit':'Chạy tiếp TK đã dùng' in ui and 'ConfirmLegacyAccountMinutes()' in ui and 'StartManualResumeAsync()' in ui,
'reject_invalid':'"INVALID" or "RESTRICTED"' in vault and '"ACCOUNT_CHECKPOINT" or "NEEDS_REVIEW"' in vault,
'apply_now_respects_total':'account.AccumulatedRunSeconds' in clock and 'UpdateActiveSessionDeadline(AccountId, deadline, requested.SessionLimitMinutes)' in clock,
'dashboard_auto_fresh_label':'AUTO: chỉ TK chưa dùng' in read('DeviceDashboardControl.cs'),
'vault_snapshots_preserve_times':vault.count('AccumulatedRunSeconds=a.AccumulatedRunSeconds')==2,
'no_everused_reset':'EverUsed=false' not in vault,
'live_engine_unchanged':(p/'Core/LiveAutonomousWorkflow.cs').exists(),
}
for k,v in checks.items():print(('PASS' if v else 'FAIL')+': '+k)
assert all(checks.values()),'A V5.1 source invariant failed'
print('NOTE: Structural assertions only. Requires .NET 8 WinForms build and device E2E tests.')
