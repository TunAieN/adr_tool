# Android Multi-Device Manager V5.7 — Find people & LIVE UI Recovery

**Base:** AccV56.zip (full source). PATCH chỉ áp dụng lên source V5.6, không áp dụng trực tiếp lên V5.5 hoặc V4.

## Sửa lỗi

- Phát hiện màn hình phụ TikTok **Find people** từ XML accessibility theo tiêu đề + dấu hiệu thứ hai (Connect Now, Use QR code, Invite friends hoặc Search by name or username).
- Tại LIVE Search và quá trình chuyển phòng: Back tối đa 5 lần, sau mỗi Back đọc mới XML; nếu vẫn kẹt, force-stop và mở lại TikTok tối đa 1 lần, **không pm clear**.
- Khi khôi phục thành công, hủy mọi tọa độ/phòng cũ; quay lại LIVE Search Phase 1. Không điều khiển nhầm trên giao diện lạ.
- Không thao tác Back/restart trên OAuth/Login/CAPTCHA xác thực hoặc ứng dụng ngoài TikTok. Giữ nguyên handler CAPTCHA, Profile và chức năng vòng đời tài khoản.
- Nếu phục hồi tìm LIVE không thành công, trả về session supervisor; giới hạn lần tạo session lại theo MaxSessionRecoveryAttempts của máy để tránh vòng lặp vô hạn.
- Log chính: `UI_STATE_DETECTED`, `NAV_RECOVERY_START`, `NAV_RECOVERY_BACK`, `NAV_RECOVERY_APP_RESTART`, `NAV_RECOVERY_SUCCESS`, `NAV_RECOVERY_CHECKPOINT`, `WORKFLOW_RESUMED`, `NAV_RECOVERY_EXHAUSTED`.
- Lưu XML trong `%LOCALAPPDATA%\AndroidMultiDeviceV1\diagnostics\<adb_serial>\nav-find-people`.

## Cập nhật

1. Stop worker và thoát ứng dụng; sao lưu source V5.6, `settings.json`, `account-vault.json`.
2. Giải nén `P57.zip` vào thư mục gốc chứa `DASH/`; cho phép ghi đè source.
3. Chạy `DASH/BUILD_RUN.bat` trên Windows có .NET 8 SDK.
4. Kiểm tra Phone 02 và Phone 03 khi Find people đang mở; xác nhận hồi phục bằng 1 lần Start. Sau đó thử khi Phase 1 đang chạy và khi chuyển phòng.

**Giới hạn:** Bản này nhắm vào `Find people` và đường vào LIVE Search/chuyển phòng. Chưa có kiểm chứng build .NET 8 hoặc E2E Appium trong môi trường đóng gói. Trang phụ khác cần XML/screenshot để thêm dấu hiệu nhận diện đúng; không nhấn tùy ý trên trang lạ.
