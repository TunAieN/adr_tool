# V5.3 — Khôi phục CAPTCHA/LIVE V4 cho Start LIVE V5.2

## Lỗi được xử lý

V5.2 chạy `VerifyLiveOwnerAsync` trước khi gọi engine LIVE; `TikTokStateDetector` trả về
`SecurityChallenge` và worker kết thúc **trước** lúc chạm tới `LivePopupHandler` vốn đã
được dùng trong V4. Bản vá **không xóa hoặc thay đổi code CAPTCHA V4**.

## Thay đổi

Chỉ chỉnh `DASH/AndroidMultiDeviceV1/Core/DeviceOrchestrator.cs`:

1. Trước khi đọc chủ tài khoản, gọi **nguyên vẹn handler CAPTCHA của LIVE V4**
   (`LivePopupHandler.DismissVerifyCaptchaAsync`) để đóng popup `Verify to continue`
   bằng nút X/Close; đọc lại XML rồi xác minh username.
2. Nếu popup xuất hiện trong quá trình tìm Profile, gọi lại đúng handler V4 rồi
   thử xác minh username một lần nữa.
3. Khi handler V4 báo không thể đóng popup, hỗ trợ **một** force-stop/relaunch
   TikTok không xóa dữ liệu (`RestartTikTokAsync`) rồi kiểm tra lại. Tối đa 5
   lượt đọc UI; CAPTCHA/xác minh vẫn hiện thì dừng và giữ nguyên tài khoản.
4. Không `pm clear` trong đường xử lý CAPTCHA trước khi xác minh danh tính;
   đường `pm clear` khi **hết hạn phiên đã xác minh** giữ nguyên V5.2.

## Những gì không đổi

`LivePopupHandler.cs`, `LiveAutonomousWorkflow.cs`, `LiveSearchEngine.cs`,
`LiveRoomContinuousCoordinator.cs`, `TikTokStateManager.cs`, Google Login,
Profile/FAST, OCR Viewer, Phase 5, lịch vòng đời tài khoản và chính sách Auto Fresh Only.

## Áp dụng PATCH

Dừng workers rồi đóng phần mềm. Sao lưu source và
`%LOCALAPPDATA%\\AndroidMultiDeviceV1\\settings.json`,
`%LOCALAPPDATA%\\AndroidMultiDeviceV1\\account-vault.json`.
Giải nén ZIP patch vào **thư mục gốc chứa `DASH/`** của bản AccV52,
sau đó chạy `DASH/BUILD_RUN.bat` (hoặc BAT build trước đó bạn đang dùng).

## Log mong đợi

`LIVE_OWNER_V4 CAPTCHA_DETECTED` → `LIVE_OWNER_V4 CAPTCHA_DISMISSED` →
`LIVE_OWNER_V4_CAPTCHA_DISMISSED` → `LIVE_OWNER_VERIFY` →
`LIVE_OWNER_VERIFIED` → `LIVE_ACCOUNT_SESSION_STARTED`.

Nếu CAPTCHA thật không thể đóng, log `LIVE_OWNER_V4_SECURITY_UNRESOLVED`
hoặc `LIVE_OWNER_V4_CAPTCHA_UNRESOLVED`; không tự xóa dữ liệu.

## Kiểm tra

`python DASH/TEST_LIVE_OWNER_V4_CAPTCHA_RESTORE.py`
`python DASH/TEST_ACCOUNT_LIFECYCLE_V52.py`

Các kiểm tra trên là kiểm tra source; cần build .NET 8 và kiểm tra với điện thoại thật.
