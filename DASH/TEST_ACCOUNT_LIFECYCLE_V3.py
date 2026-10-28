from pathlib import Path
p=Path(__file__).parent/'AndroidMultiDeviceV1'
model=(p/'Core/Models.cs').read_text(encoding='utf-8-sig')
vault=(p/'Core/Storage.cs').read_text(encoding='utf-8-sig')
orch=(p/'Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
ui=(p/'MainForm.cs').read_text(encoding='utf-8-sig')
status=(p/'Core/AccountLifecycleLabels.cs').read_text()
checks={
  'defaults':all(x in model for x in ['AccountSessionLimitMinutes { get; set; } = 360','AutoRotateAccounts { get; set; } = true','NeverReuseUsedAccounts { get; set; } = true','PurgeUsedAfterHours { get; set; } = 24']),
  'ledger':all(x in model for x in ['EverUsed','SessionDeadlineUtc','EmailFingerprint']),
  'single_use':all(x in vault for x in ['EnforceSingleUse','!WasUsed(a)','EligibleForReservation(a)','!WasUsed(item)','(!SingleUse(x.DeviceSerial) || !WasUsed(x))']),
  'no_reimport':all(x in vault for x in ['IsRetired(a) &&','a.EmailFingerprint==Fingerprint(email)','a.HandleFingerprint==Fingerprint(username)']),
  'scheduler':all(x in orch for x in ['new DeviceAccountClock','CancellationTokenSource.CreateLinkedTokenSource','StartVerifiedTimedSession','ACCOUNT_SESSION_EXPIRED','ClearTikTokAppDataAsync','ClearGooglePlayServicesDataAsync','ReserveNextAvailable']),
  'no_cross_device_fallback': 'item.DeviceSerial.Equals(serial,StringComparison.OrdinalIgnoreCase)' in vault,
  'time_is_verified':orch.index('var deadline=_vault.StartVerifiedTimedSession(account.Id,')>orch.index('var reconcile=await reconciler.ReconcileAsync(') and 'LIVE_OWNER_VERIFIED:' in orch,
  'test_isolation':'if(onlyAccountId.HasValue)return;' in orch and 'if(!accountPolicy.AutoRotateAccounts || onlyAccountId.HasValue)' in orch,
  'early_live_return_stops':'ACCOUNT_SESSION_LIVE_EXIT_UNEXPECTED: no rotation' in orch,
  'purge_guard': 'a.State is not ("ACTIVE" or "RESERVED")' in vault and 'a.LastEnd.HasValue' in vault,
  'purge_hidden': 'Where(a=>!IsRetired(a))' in vault,
  'ui_localization': all(x in status for x in ['"AVAILABLE"','"INTERRUPTED"','"INVALID"','"ACTIVE"','"RETRY_PENDING"']),
  'ui_account_settings':all(x in ui for x in ['ConfigureAccountLifecycle','Vòng đời TK','Vòng đời tài khoản','CleanupUsedAccountsIfDue','UsedDisplay','RemainingDisplay','StateVi']),
  'legacy_preserved': (p/'Core/LiveSearchEngine.cs').exists() and (p/'Core/LiveVisualOcr.cs').exists(),
}
for key,value in checks.items():print(('PASS' if value else 'FAIL')+': '+key)
assert all(checks.values())
