from pathlib import Path
root=Path(__file__).parent
m=(root/'AndroidMultiDeviceV1/MainForm.cs').read_text(encoding='utf-8-sig')
d=(root/'AndroidMultiDeviceV1/DeviceDashboardControl.cs').read_text()
model=(root/'AndroidMultiDeviceV1/Core/Models.cs').read_text()
orches=(root/'AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig')
assert 'DeviceDashboardControl?' in m
assert 'workspace.Controls.Add(_dashboard)' in m
assert 'ShowLegacyDetails(1)' in m and 'OpenExistingDeviceDetail' in m
assert 'StartSelectedDevicesAsync()' in m
assert 'CloudInstanceName' in model and 'DisplayAlias' in model
assert 'LeaseExpiresAt' in model and 'LastSeenAt' in model
assert 'LastSeenAt=DateTimeOffset.Now' in orches
assert 'Storage.SaveSettings(_settings)' in d
assert '_target.DeviceId' in d and 'd.CloudInstanceName.Equals(CloudName' in d
assert 'Start đã tick' in d and '_startBatch(selected)' in d
assert 'FastPreflightIdleScope' in (root/'AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs').read_text(encoding='utf-8-sig') or (root/'AndroidMultiDeviceV1/Core/FastPreflightIdleScope.cs').exists()
for f in [root/'AndroidMultiDeviceV1/Core/LoginWorkflow.cs',root/'AndroidMultiDeviceV1/Core/LiveSearchEngine.cs',root/'AndroidMultiDeviceV1/Core/LiveVisualOcr.cs']:
 assert f.exists()
print('PASS: dashboard integrated, stable DeviceId metadata, manual Cloud mapping, preserved batch Start/legacy accounts, lease and refresh')
