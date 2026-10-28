"""V5.4 UI/source checks. Does not compile WinForms or run phones."""
from pathlib import Path
from decimal import Decimal, ROUND_HALF_UP
root=Path(__file__).parent/'AndroidMultiDeviceV1'
ui=(root/'MainForm.cs').read_text(encoding='utf-8-sig')
model=(root/'Core/Models.cs').read_text(encoding='utf-8-sig')
dashboard=(root/'DeviceDashboardControl.cs').read_text(encoding='utf-8-sig')
fmt=(root/'Core/AccountDurationDisplay.cs').read_text(encoding='utf-8-sig')
assert 'HeaderText="Đã chạy"' in ui
assert 'HeaderText="Còn lại"' not in ui
assert 'HeaderText="Đã chạy (ngày.giờ:phút)"' not in ui
assert 'Giới hạn thời gian chạy tài khoản (giờ):' in ui
assert 'DecimalPlaces=2' in ui and 'Increment=0.25m' in ui
assert 'duration.Value=AccountDurationDisplay.MinutesToInputHours(policy.SessionLimitMinutes);' in ui
assert 'SessionLimitMinutes=AccountDurationDisplay.InputHoursToMinutes(duration.Value),' in ui
assert 'SessionLimitMinutes=(int)duration.Value' not in ui
assert 'RemainingDisplay' in model  # backend preserved
assert 'AccountDurationDisplay.FormatElapsed(EffectiveRunSeconds)' in model
assert 'TimeSpan.FromSeconds(EffectiveRunSeconds).ToString(@"dd' not in model
assert 'LegacyHistoryUnverified' in ui and 'Thời gian chỉ tính từ mốc theo dõi mới' in ui
assert 'AccountDurationDisplay.FormatHours(lifecycle.SessionLimitMinutes)' in dashboard
assert 'AccountDurationDisplay.FormatHours(d.LifecycleActiveLimitMinutes)' in dashboard
assert 'total / 3600' in fmt  # not a 24h-wrapped clock
for minutes in range(1, 10081):
    h=(Decimal(minutes)/60).quantize(Decimal('.01'),rounding=ROUND_HALF_UP)
    recovered=int((h*60).quantize(Decimal('1'),rounding=ROUND_HALF_UP))
    assert recovered==minutes,(minutes,h,recovered)
assert (48*3600+3*60+9)//3600==48
print('V5.4 UI CONTRACT PASS: hours config + unchanged minute persistence; hh:mm:ss total hours; no Remaining column; 10080 minute roundtrips.')
