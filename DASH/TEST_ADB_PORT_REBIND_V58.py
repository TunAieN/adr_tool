"""Source-level V5.8 checks. This is NOT a Windows .NET compilation or an actual Appium session test."""
from pathlib import Path
import re
r=Path(__file__).parent/'AndroidMultiDeviceV1'
f=lambda path:(r/path).read_text(encoding='utf-8-sig')
session=f('Core/DeviceExecution.cs')
appium=f('Core/AdbAndAppium.cs')
orch=f('Core/DeviceOrchestrator.cs')
vault=f('Core/Storage.cs')
main=f('MainForm.cs')
dash=f('DeviceDashboardControl.cs')
checks={
    'port_busy_classified_narrowly': all(x in session for x in ['IsBusySystemPort(Exception ex)','"systemPort"','"busy"','"already in use"']),
    'bounded_port_retry': 'MaxBusyPortRetries=5' in session and 'attempt<=MaxBusyPortRetries' in session,
    'fallback_ports_are_unique': all(x in session for x in ['FirstRecoveryPort=8300','LastRecoveryPort=8999','attempted.Contains(port)','_ports.ContainsKey(port)','context.Settings.Devices.Any','_ports.TryAdd(port,context.Serial)']),
    'avoid_occupied_os_ports':'IsTcpPortAvailable(int port)' in session and 'TcpListener' in session,
    'do_not_persist_port_before_success': session.index('await session.CreateAsync(context.Device,ct,port)') < session.index('context.Device.SystemPort=port') < session.index('Storage.SaveSettings(context.Settings)'),
    'release_on_failure': '_ports.TryRemove(new KeyValuePair<int,string>(port,context.Serial))' in session and 'await session.DisposeAsync()' in session,
    'new_session_passes_override': 'systemPortOverride??device.SystemPort' in appium,
    'rebind_method_exists': 'RebindEndpointAsync(' in orch and 'accountVaultMigrated=true' in orch,
    'verify_online_before_vault_mutation': orch.index('if(!online.Contains(newEndpoint')<orch.index('_vault.ChangeDeviceSerial(oldEndpoint,newEndpoint)'),
    'block_racing_worker': 'IsBusy(oldEndpoint) || IsBusy(newEndpoint)' in orch and 'lock(_lifecycleGate)' in orch,
    'preserve_device_identity': 'device.Serial=newEndpoint' in orch and 'device.DeviceId=' not in orch.split('RebindEndpointAsync(',1)[1].split('public bool IsRunning',1)[0],
    'prevent_auto_rediscovery_of_old': '_settings.RemovedDeviceSerials.Add(oldEndpoint)' in orch,
    'avoid_colliding_vaults': '_vault.Snapshot(newEndpoint).Count>0' in orch and 'không tự gộp dữ liệu' in orch,
    'vault_migration_rollback': 'if(vaultMoved)' in orch and '_vault.ChangeDeviceSerial(newEndpoint,oldEndpoint)' in orch,
    'vault_save_rollback': 'foreach(var a in moving)a.DeviceSerial=oldSerial' in vault,
    'settings_atomic_writes_serialized':'lock(WriteGate)' in vault,
    'dashboard_button_and_delegate': all(x in dash for x in ['Sửa Remote ADB','_editEndpoint','RunSelected(_editEndpoint)']),
    'device_list_context_action':'Sửa Remote ADB (giữ kho tài khoản)' in main,
    'ui_delegates_to_safe_rebind':'await _orchestrator.RebindEndpointAsync(device,endpoint)' in main,
    'never_auto_clear_accounts': all(x not in orch.split('RebindEndpointAsync(',1)[1].split('public bool IsRunning',1)[0] for x in ['ClearTikTokAppDataAsync','ClearGooglePlayServicesDataAsync','DeleteAccounts']),
    'live_engine_untouched':(r/'Core/LiveAutonomousWorkflow.cs').exists()
}
for name,condition in checks.items():
    assert condition,name
print(f'PASS: {len(checks)} V5.8 Appium busy-port and ADB rebind source invariants')
print('NOTE: compile and phone-level end-to-end testing require Windows .NET 8/Appium/XCloudPhone.')
