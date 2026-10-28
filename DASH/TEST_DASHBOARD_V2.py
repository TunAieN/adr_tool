"""Non-Windows V2 source invariants. Actual WinForms compilation and runtime require .NET 8 Windows."""
from pathlib import Path
import re
base=Path(__file__).resolve().parent
code=(base/'AndroidMultiDeviceV1/DeviceDashboardControl.cs').read_text('utf-8')
models=(base/'AndroidMultiDeviceV1/Core/Models.cs').read_text('utf-8')
main=(base/'AndroidMultiDeviceV1/MainForm.cs').read_text('utf-8')
assert 'Dashboard V2' in code
assert 'readonly HashSet<Guid> _checkedIds = new()' in code
assert 'Start đã tick' in code and '_startBatch(selected)' in code
assert 'Xác nhận Start' in code and 'Xác nhận Stop' in code
assert '_grid.ReadOnly=false' in code and '_grid.Columns["Checked"]!.ReadOnly=false' in code
assert re.search(r'AddText\("Account","Tài khoản",\d+\)',code)
assert 'StageLabel' in code and 'row.Cells["Phase"].ToolTipText=fullPhase' in code
assert 'RichTextBox' in code and 'Lỗi gần nhất' in code
assert 'Cột hiển thị' in code and 'BuildColumnsMenu' in code
assert 'SaveColumnLayoutIfChanged' in code and 'ApplyLayoutPreferences' in code
assert '_settings.DeviceDashboardColumnWidths' in code
assert '_settings.DeviceDashboardColumnVisibility' in code
assert 'DeviceDashboardSplitterDistance' in code
assert '_split.Orientation=Orientation.Horizontal' in code
assert all(s in models for s in ['DeviceDashboardColumnWidths','DeviceDashboardColumnVisibility','DeviceDashboardSplitterDistance'])
assert 'workspace.Controls.Add(_dashboard)' in main
assert 'OpenExistingDeviceDetail' in main and 'StartSelectedDevicesAsync()' in main
assert 'pm clear' not in code.lower() and 'session.Create' not in code
assert 'd.CloudInstanceName.Equals(CloudName' in code
assert 'd.DisplayAlias=dialog.Alias' in code
print('PASS: V2 full-width master/detail, persisted width/visibility/split, explicit bulk actions, stable mapping')
print('NOTE: requires Windows .NET 8 SDK for compiling and visual/runtime verification')
