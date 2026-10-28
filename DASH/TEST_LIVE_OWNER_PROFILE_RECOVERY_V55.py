"""Static contracts for same-Start recovery after PROFILE_NAVIGATION_NOT_CONFIRMED.
These checks complement, but never replace, Windows .NET 8 and Appium E2E tests.
"""
from pathlib import Path
core=Path(__file__).parent/'AndroidMultiDeviceV1'/'Core'
s=(core/'DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
start=s.index('private async Task<TikTokObservation> DetectLiveOwnerWithV4PopupRecoveryAsync(')
end=s.index('// LIVE direct may start with an already authenticated profile.',start)
body=s[start:end]
method=s[s.index('private async Task<ManagedAccount?> VerifyLiveOwnerAsync('):s.index('// Workers registered but waiting for a permit',end)]
checks={
 'uses_original_owner_verification': 'DetectLiveOwnerWithV4PopupRecoveryAsync(' in method,
 'only_navigation_unknown_triggers_retry': 'seen.State==TikTokUiState.Unknown' in body and 'seen.Detail.Contains("PROFILE_NAVIGATION_NOT_CONFIRMED"' in body,
 'late_popup_recheck_before_restart': body.index('LIVE_OWNER_V4_LATE') < body.index('LIVE_OWNER_PROFILE_RECOVERY_START'),
 'one_navigation_recovery': body.count('RecoverNavigationAsync(')==1 and 'navigationRecoveryAttempted=true' in body and 'if(restarted || navigationRecoveryAttempted)' in body,
 'restart_without_pm_clear': 'forceRestartFirst:true' in body and 'pmClear=false' in body and 'ClearTikTokAppDataAsync' not in body and 'ClearGooglePlayServicesDataAsync' not in body,
 'rechecks_original_v4_popup': 'if(ready)continue;' in body and 'attempt<=5' in body,
 'real_challenge_remains_blocked': 'LIVE_OWNER_PROFILE_RECOVERY_BLOCKED' in body and 'TikTokUiState.SecurityChallenge' in body,
 'cancellation_and_error_guard': 'catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}' in body and 'AppiumFailureClassifier.IsFatalSessionFailure(ex)' in body,
 'exact_identity_check_retained': 'matched.Count!=1' in method and 'LIVE_IDENTITY_AMBIGUOUS' in method,
 'lifeclock_unchanged': 'StartVerifiedTimedSession(liveAccount.Id,' in s,
 'single_worker_lock_unchanged': 'if(!_deviceSessionLocks.TryAdd(device.Serial,0))' in s,
}
for name,ok in checks.items():print(('PASS' if ok else 'FAIL')+': '+name)
assert all(checks.values())
print('LIVE OWNER PROFILE RECOVERY V5.5: STATIC PASS')
