"""Static regression contracts: V4 LIVE CAPTCHA handler reinstated before V5.2 owner detector.
Does NOT replace compilation or real-device testing.
"""
from pathlib import Path
root=Path(__file__).resolve().parent
core=root/'AndroidMultiDeviceV1'/'Core'
o=(core/'DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
popup=(core/'LivePopupHandler.cs').read_text(encoding='utf-8-sig')
start=o.index('private async Task<TikTokObservation> DetectLiveOwnerWithV4PopupRecoveryAsync(')
end=o.index('// LIVE direct may start with an already authenticated profile.',start)
method=o[start:end]
verify=o[o.index('private async Task<ManagedAccount?> VerifyLiveOwnerAsync('):o.index('// Workers registered but waiting for a permit',start)]
checks={
    'owner_verify_calls_recovered_detector': 'DetectLiveOwnerWithV4PopupRecoveryAsync(' in verify and 'new TikTokStateDetector(_selectors).DetectAsync(lease.Session' not in verify,
    'reuse_existing_v4_handler_not_new_captcha_algorithm': method.count('LivePopupHandler.DismissVerifyCaptchaAsync(')==3 and 'CaptchaDismissStatus.Dismissed' in method,
    'v4_popup_cleanup_before_owner_detection': method.index('DismissVerifyCaptchaAsync(') < method.index('detector.DetectAsync('),
    'popup_during_profile_navigation_recovered': 'LIVE_OWNER_V4_POST_DETECT' in method and 'seen.State!=TikTokUiState.SecurityChallenge' in method,
    'bounded_retry_with_single_restart': 'attempt<=5' in method and 'bool restarted=false' in method and method.count('restarted=true;')==2 and 'navigationRecoveryAttempted=true' in method,
    'never_clear_data_before_owner_verified': all(x not in method for x in ['ClearTikTokAppDataAsync(', 'ClearGooglePlayServicesDataAsync(', 'ClearDataForCleanLoginAsync(', 'pm clear TikTok']),
    'restart_is_nondestructive': '_adb.RestartTikTokAsync(context.Device.Serial,ct)' in method,
    'persistent_security_not_silently_bypassed': 'TikTokUiState.SecurityChallenge' in method and 'return seen;' in method,
    'exact_owner_match_retained': 'matched.Count!=1' in verify and 'LIVE_IDENTITY_AMBIGUOUS' in verify,
    'account_budget_retained': 'StartVerifiedTimedSession(liveAccount.Id,' in o and 'AssessVerifiedLiveOwner(device.Serial' in o,
    'original_live_popup_code_present': 'CAPTCHA_DISMISSED:' in popup and 'CAPTCHA_DISMISS_FAILED:' in popup,
    'separate_worker_and_appium_ports': 'maxConcurrent={_parallel.Limit}' in o and 'systemPort={device.SystemPort}' in o,
}
for key,ok in checks.items():print(('PASS' if ok else 'FAIL')+': '+key)
assert all(checks.values()), 'Restoration regression failed'
print('V5.2 LIVE OWNER V4 CAPTCHA RESTORATION: STRUCTURAL PASS')
