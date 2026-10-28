"""V5.2 source contracts. Structural checks only; NOT a Windows C# compilation/device test."""
from pathlib import Path
r=Path(__file__).parent/'AndroidMultiDeviceV1'
read=lambda rel:(r/rel).read_text(encoding='utf-8-sig')
o=read('Core/DeviceOrchestrator.cs'); v=read('Core/Storage.cs')
m=read('Core/Models.cs'); u=read('MainForm.cs');clock=read('Core/DeviceAccountClock.cs')
checks={
 'start_live_owner_verified':'VerifyLiveOwnerAsync(context,worker.Token' in o and 'seen.VerifiedHandle' in o and 'matched.Count!=1' in o,
 'one_button_current_used':'ReserveVerifiedLiveOwner(device.Serial,verified.Id' in o and 'AssessVerifiedLiveOwner(device.Serial' in o,
 'reserves_specific_owner_only':'x.Id==id &&' in v and 'x.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase)' in v,
 'blocks_invalid_or_unverified':'LiveOwnerDecision.Blocked' in v and 'LIVE_IDENTITY_UNVERIFIED:' in o,
 'no_auto_used_queue':'return pool.FirstOrDefault(a=>a.State=="AVAILABLE" && !WasUsed(a));' in v and 'item.State=="AVAILABLE" && !WasUsed(item)' in v,
 'legacy_clear_disclosure':'LegacyHistoryUnverified' in m and 'ACCOUNT_LEGACY_HISTORY_UNKNOWN' in o and 'từ mốc mới' in m,
 'unknown_time_only_on_verified_owner':'if(WasUsed(a!) && !a!.RunDurationKnown)' in v and 'ReserveVerifiedLiveOwner' in v and 'RunDurationKnown=true' in v,
 'no_manual_minute_entry_required':'poolActions.Controls.Add(Btn("Xác nhận phút đã chạy"' not in u and 'poolActions.Controls.Add(Btn("Chạy tiếp TK đã dùng"' not in u and 'accountMenu.Items.Add("Xác nhận tổng phút' not in u,
 'elapsed_cumulative':'SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow)' in v and 'remainingSeconds=Math.Max(0,budget*60-a.AccumulatedRunSeconds)' in v,
 'expired_handoff_original_start':'LIVE_OWNER_EXPIRED_AUTO_FRESH_HANDOFF' in o and 'StartUnderLock(device,fromSwitch:true)' in o,
 'no_unverified_clear':'LIVE_IDENTITY_UNVERIFIED' in o and 'LIVE_OWNER_NOT_ELIGIBLE' in o,
 'only_clear_after_session_expiry':'if(sessionExpired && !worker.Token.IsCancellationRequested)' in o and 'ClearTikTokAppDataAsync(device.Serial,worker.Token)' in o,
 'per_phone_policy':'AccountPolicyFor(device).Copy()' in o and 'AccountPolicyFor(serial)' in o,
 'explicit_stop_block':'_operatorStopRequests.ContainsKey(device.Serial)' in o,
 'clock_keep_total':'AccumulatedRunSeconds' in clock and 'UpdateActiveSessionDeadline' in clock,
 'live_engine_untouched':(r/'Core/LiveAutonomousWorkflow.cs').exists(),
}
for k,valid in checks.items(): print(('PASS' if valid else 'FAIL')+': '+k)
assert all(checks.values()),'V5.2 structural regression failed'
print('V5.2 STRUCTURAL PASS (not .NET compilation or phone E2E).')
