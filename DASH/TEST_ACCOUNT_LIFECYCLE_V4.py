"""Static source regression checks for per-device V4 lifecycle isolation.
Run on source tree without Windows/.NET runtime; Windows build still required.
"""
from pathlib import Path
base=Path(__file__).parent/'AndroidMultiDeviceV1'
model=(base/'Core/Models.cs').read_text(encoding='utf-8-sig')
storage=(base/'Core/Storage.cs').read_text(encoding='utf-8-sig')
orch=(base/'Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
ui=(base/'MainForm.cs').read_text(encoding='utf-8-sig')
dash=(base/'DeviceDashboardControl.cs').read_text(encoding='utf-8-sig')
checks={
 'per_device_model':'public AccountLifecyclePolicy? AccountLifecycle' in model and 'public AccountLifecyclePolicy AccountPolicyFor(DeviceRecord device)' in model,
 'isolated_copy':'public AccountLifecyclePolicy Copy()' in model and 'var accountPolicy=_settings.AccountPolicyFor(d).Copy();' in orch,
 'v3_data_migration':'oldById' in storage and 'device.AccountLifecycle=new AccountLifecyclePolicy' in storage and 'NeverReuseUsedAccounts=settings.NeverReuseUsedAccounts' in storage,
 'per_serial_single_use':'public Func<string,bool>? SingleUseForSerial' in storage and 'SingleUse(a.DeviceSerial)' in storage and 'SingleUse(serial)' in storage,
 'per_device_vault_binding':'_vault.SingleUseForSerial=serial=>_settings.AccountPolicyFor(serial).NeverReuseUsedAccounts' in orch and 'SingleUseForSerial=serial=>_settings.AccountPolicyFor(serial)' in ui,
 'per_device_session_deadline':'StartVerifiedTimedSession(account.Id,\n                        accountPolicy.SessionLimitMinutes)' in orch and 'new DeviceAccountClock(d,_vault,account.Id' in orch and 'ApplyLifecycleToRunning' in orch,
 'per_device_rotation':'!accountPolicy.AutoRotateAccounts || onlyAccountId.HasValue' in orch,
 'per_device_purge_delay':'accountPolicy.PurgeUsedAfterHours' in orch,
 'per_device_purge_toggle':'purgeEnabledForSerial(a.DeviceSerial)' in storage and 'PurgeDueUsedCredentials(now,' in ui and 'AccountPolicyFor(serial).AutoPurgeUsedAccounts' in ui,
 'purge_guard':'a.State is not ("ACTIVE" or "RESERVED")' in storage and 'a.LastEnd.HasValue' in storage,
 'ui_device_choice':'Vòng đời tài khoản riêng từng điện thoại' in ui and 'var target=orderedDevices[devices.SelectedIndex];' in ui and 'target.AccountLifecycle=new AccountLifecyclePolicy' in ui,
 'dashboard_action':'RunSelected(_editLifecycle)' in dash and 'd=>ConfigureAccountLifecycle(d)' in ui,
 'engine_still_existing':(base/'Core/LiveSearchEngine.cs').exists() and (base/'Core/LiveVisualOcr.cs').exists(),
 'pm_clear_preserved':'ClearTikTokAppDataAsync' in orch and 'ClearGooglePlayServicesDataAsync' in orch,
 'no_shared_runtime':'_settings.AccountSessionLimitMinutes' not in orch and '_settings.AutoRotateAccounts' not in orch and '_settings.PurgeUsedAfterHours' not in orch,
}
for name,ok in checks.items():print(('PASS' if ok else 'FAIL')+': '+name)
assert all(checks.values())
print('NOTE: static checks only; build with Windows .NET 8 and test one phone at a time before deploying to multiple phones.')
