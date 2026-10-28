"""V5.9 static regression, does not replace Windows/.NET/Appium E2E tests."""
from pathlib import Path
import re
root=Path(__file__).parent/'AndroidMultiDeviceV1'
read=lambda f:(root/f).read_text(encoding='utf-8-sig')
store=read('Core/Storage.cs'); orch=read('Core/DeviceOrchestrator.cs'); login=read('Core/LoginWorkflow.cs'); mod=read('Core/Models.cs')
checks={
 'manual_UI_review_preview': 'EligibleForReservation(a) || IsExplicitTechnicalRetryEligible(a)' in store,
 'manual_UI_review_reservation': 'bool technicalRetry=IsExplicitTechnicalRetryEligible(a);' in store,
 'technical_status_allowlist': all(x in store for x in [
    'Quá 3 phút vẫn chưa xác minh được đúng TikTok handle',
    'Google Choose an account', 'Lỗi kỹ thuật lặp lại 3 lần']),
 'security_checkpoint_banned': all(x in store for x in [
    '"GOOGLE_2SV"', '"captcha"', '"checkpoint"', '"bảo mật"', '"sai mật khẩu"']),
 'no_auto_repeat': 'return pool.FirstOrDefault(a=>a.State=="AVAILABLE" && !WasUsed(a));' in store,
 'explicit_retry_logged': 'EXPLICIT_TECH_UI_RETRY_ACCEPTED' in orch,
 'no_clear_review_owner': 'LIVE_OWNER_TECH_REVIEW_RESUMED' in orch and 'pmClear=false' in orch,
 'strict_username_match': 'matched.Count!=1' in orch and 'seen.VerifiedHandle' in orch,
 'rebind_preserve_device_and_clock': 'ReconcileOrphanedAfterEndpointRebind(newEndpoint)' in orch and
                                     'SettleRunTimeUnsafe(a,DateTimeOffset.UtcNow)' in store,
 'strict_owner_orphan': 'ReconcileVerifiedOrphanedOwner(device.Serial,candidate.Id)' in orch,
 'technical_new_verified_window': 'a.ExplicitTechnicalRetryPending=false;' in store and
                                  'a.LegacyHistoryUnverified=true;' in store,
 'google_auth_proof': 'if(!await VerifyGoogleContext(session,ct))' in login,
 'google_header_scrolled_fix': 'IsVerifiedGoogleChooserBody(xml)' in login,
 'only_click_explicit_another': 'Where(IsUseAnotherAccountNode)' in login and
                                'VerifiedAnotherAccountPoint(xml)' in login,
 'no_cached_google_index_taps': not re.search(r'\bTapUiPointAsync\(\s*\d+\s*,\s*\d+',login),
 'google_scroll_count_truth': 'GOOGLE_ACCOUNT_PICKER_TARGET_NOT_FOUND: actualScrolls=' in login,
 'no_clock_history_reset': 'a.AccumulatedRunSeconds=0' not in store,
 'stop_start_protection': 'MANUAL_ACCOUNT_START_CANCELLED_BY_NEW_STOP' in orch,
 'models_retry_flag': 'public bool ExplicitTechnicalRetryPending { get; set; }' in mod,
}
for n,v in checks.items(): print(('PASS' if v else 'FAIL')+': '+n)
assert all(checks.values()),'V5.9 check failed'
print('V5.9 STATIC PASS: user-confirmed retry, strict owner, Google chooser, no AUTO reuse')
