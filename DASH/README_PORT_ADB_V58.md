# V5.8 – Tự hồi phục Appium systemPort + Sửa Remote ADB giữ kho

## A. Appium systemPort busy

- Khi port ban đầu (vd 8207) đã bị chiếm trên Windows hoặc Appium trả lỗi `systemPort ... busy`, factory thử tối đa 5 cổng khác.
- Chỉ chọn các cổng 8300–8999 không đang được worker nào của tool giữ, không được thiết bị khác cấu hình và không có TCP listener tại localhost.
- Tạo phiên Appium thành công **rồi mới** cập nhật SystemPort trong `settings.json`.
- Không giết/khởi động lại Appium server chung, không đụng session các máy khác. Nếu không cấp được cổng, báo lỗi riêng máy.
- Log: `APPIUM_SYSTEM_PORT_BUSY_RECOVERY`, `SYSTEM_PORT_AUTO_REALLOCATED`, `APPIUM_SESSION_CREATED`.

## B. Đổi endpoint thuê sau khi reboot CloudPhone

- Mở kho vault, vào **THIẾT BỊ** → chọn đúng Phone → **Sửa Remote ADB** (hoặc trong danh sách **TÀI KHOẢN**, click phải tên Phone → Sửa Remote ADB).
- Stop riêng Phone đó và chờ worker/Appium giải phóng. Nhập địa chỉ `adb.xcloudphone.net:PORT` mới của **cùng một máy thuê**, xác nhận.
- Tool kết nối và xác thực endpoint mới Online **trước** khi cập nhật. Nếu endpoint khác đã có tài khoản/worker riêng thì từ chối gộp.
- Giữ nguyên `DeviceId`, `Phone NN`, `SystemPort`, lịch sử, metadata CloudPhone, cấu hình vòng đời, danh sách tài khoản với mật khẩu mã hóa. Chỉ chuyển `DeviceSerial` của account pool từ endpoint cũ sang mới.
- Endpoint cũ được đưa vào danh sách bỏ qua để Refresh không tự tạo lại Phone trùng. Nếu endpoint mới đã được Refresh thêm thành dòng mới **rỗng và không có worker**, bản ghi rỗng được loại bỏ.
- Nếu lưu thất bại, cố gắng rollback cả settings và kho cục bộ.
- Log: `ADB_ENDPOINT_REBOUND`, `ADB_ENDPOINT_REBIND_FAILED`.

## Các điều kiện an toàn

- **Không đổi endpoint** khi máy đang chạy hoặc khi endpoint mới thuộc máy khác đã có tài khoản.
- Chỉ sửa đúng endpoint CÙNG phiên thuê; ADB online **không chứng minh** hai endpoint là cùng điện thoại. Người dùng đối chiếu tên phiên CloudPhone trước khi xác nhận.
- Không cần delete máy; delete/re-add có thể làm mất liên kết đến kho tài khoản cũ.
- Không có thay đổi trong Google Login, TikTok `pm clear`, CAPTCHA, LIVE Search/OCR hoặc Phase 5.

## Cài đặt

1. Stop tất cả workers và thoát tool; sao lưu source V5.7, `settings.json`, `account-vault.json`.
2. Dán PATCH vào thư mục **chứa** `DASH`, giữ đúng cấu trúc `DASH/AndroidMultiDeviceV1/...`.
3. Chạy `BUILD_RUN.bat` trên Windows có .NET 8 SDK; thử Phone 08 Start LIVE và thử sửa ADB Phone 03/05.
4. Chạy `python TEST_ADB_PORT_REBIND_V58.py` và các bài regression nếu có Python.

Source-level checks không thay thế build/running thực tế trên Windows.
