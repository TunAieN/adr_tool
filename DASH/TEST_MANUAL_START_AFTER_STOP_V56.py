"""V5.6 regression guard: static source contracts + stop/start transition model.
This is NOT a Windows .NET compilation or an XCloudPhone end-to-end test.
"""
from pathlib import Path
s=(Path(__file__).parent/'AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')

def region(a,b):
    return s.split(a,1)[1].split(b,1)[0]
start=region('bool StartUnderLock(DeviceRecord device','public async Task<bool> SwitchToSelectedAccountAsync(')
switch=region('public async Task<bool> SwitchToSelectedAccountAsync(', 'public async Task<TikTokObservation?> CheckSelectedStateAsync(')
stop=region('public void Stop(string serial)','public void StopAll()')
live=region('/// <summary>Starts the existing Phase 1-5 LIVE workflow','bool StartUnderLock(DeviceRecord device')
checks={
 'stop_history_versioned':'_operatorStopVersions' in s,
 'stop_serialized_with_start':'lock(_lifecycleGate)' in stop,
 'stop_increments_version':'_operatorStopVersions.AddOrUpdate(serial,1,' in stop,
 'manual_captures_stop_version_before_wait':switch.index('stopVersionAtRequest=_operatorStopVersions')<switch.index('await Task.WhenAny(old.Work'),
 'new_stop_cancels_pending_start':'currentStopVersion!=stopVersionAtRequest' in switch and 'MANUAL_ACCOUNT_START_CANCELLED_BY_NEW_STOP' in switch,
 'explicit_switch_passes_operator_intent':'fromSwitch:true,' in switch and 'operatorInitiated:true' in switch,
 'auto_handoff_not_given_operator_intent':'StartUnderLock(device,fromSwitch:true)' in live,
 'auto_handoff_checks_old_stop':'!_operatorStopRequests.ContainsKey(device.Serial)' in live,
 'automatic_start_checks_stop':'fromSwitch && !operatorInitiated && _operatorStopRequests.ContainsKey(device.Serial)' in start,
 'no_premature_clear':start.index('_workers.TryAdd(handle)')<start.index('_operatorStopRequests.TryRemove(device.Serial,out _)'),
 'manual_and_normal_clear_successful_start':'if(!fromSwitch || operatorInitiated)' in start,
 'same_device_reservation_enforced':'_deviceSessionLocks.TryAdd(device.Serial,0)' in start and '_workers.TryAdd(handle)' in start,
 'selected_specific_account_no_fallback':'_vault.PreviewSpecific(device.Serial,onlyAccountId.Value)' in start,
 'guard_rejects_failed_auth':'_vault.PreviewSpecific(device.Serial,accountId)' in switch,
 'concurrency_manual_same_device':'_pendingSwitches.TryAdd(device.Serial,0)' in switch,
 'stop_not_misclassified_as_invalid':'LIVE_ACCOUNT_ALREADY_USED_OR_NOT_AVAILABLE' not in stop,
}
for key,ok in checks.items():print(('PASS' if ok else 'FAIL')+': '+key)
assert all(checks.values()),'V5.6 source invariants failed'

class Model:
    def __init__(self):self.stop=False;self.version=0;self.worker=False;self.lock=False
    def stop_pressed(self):self.version+=1;self.stop=True;self.worker=False;self.lock=False
    def request_manual(self):return self.version
    def complete_manual(self,captured,eligible=True):
        if captured!=self.version or not eligible or self.worker or self.lock:return False
        self.worker=True;self.lock=True;self.stop=False;return True
    def automatic(self):
        if self.stop or self.worker or self.lock:return False
        self.worker=True;self.lock=True;return True

m=Model();m.stop_pressed();v=m.request_manual();assert m.complete_manual(v)
print('PASS: prior Stop -> explicit selected-account Start')
m=Model();m.stop_pressed();v=m.request_manual();m.stop_pressed();assert not m.complete_manual(v) and m.stop
print('PASS: Stop DURING handoff cancels pending manual Start')
m=Model();m.stop_pressed();assert not m.automatic() and m.stop
print('PASS: Stop continues to block automatic rotation')
m=Model();m.stop_pressed();v=m.request_manual();assert not m.complete_manual(v,eligible=False) and m.stop
print('PASS: rejected account does not erase Stop')
phones={'02':Model(),'03':Model()};phones['02'].stop_pressed();assert phones['03'].automatic()
v=phones['02'].request_manual();assert phones['02'].complete_manual(v)
print('PASS: stop/start isolated per device')
print('V5.6 SOURCE / LOGICAL REGRESSION PASS (not Windows build)')
