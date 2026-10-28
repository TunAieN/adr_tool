# Android Multi-Device V1.5.7 — Retry policy

## Thay đổi
- `RETRY_PENDING` cho lỗi UI/ADB/không xác minh được foreground/Profile; không coi là tài khoản ban.
- Bản ghi V1.5.6 `NEEDS_REVIEW` có thông báo **rõ ràng là lỗi kỹ thuật** (bao gồm `com.android.launcher3`) được nâng sang `RETRY_PENDING` khi mở kho. Không đổi các cảnh báo bảo mật/identity khác.
- Lỗi kỹ thuật cho phép thử lại sau 10 giây, rồi 30 giây. Sau 3 lỗi kỹ thuật liên tiếp, trạng thái `NEEDS_REVIEW` và cần sửa XPath/kết nối rồi chọn **Đặt lại**; không cần tài khoản mới.
- Trước mọi lần thử lại, chạy **preflight/identity check**. Không suy đoán tình trạng TikTok từ trạng thái kho.
- `RESTRICTED` / `DISABLED` không được đưa lại vào hàng đợi tự động.
- Sau khi phiên đã xác nhận đúng danh tính (`ACTIVE`), số lần retry kỹ thuật được xóa.

## Nâng cấp
1. Đóng ứng dụng và sao lưu `%LOCALAPPDATA%\AndroidMultiDeviceV1`.
2. Giải nén PATCH vào thư mục gốc của dự án V1.5.6, đè đúng 5 file C# cùng đường dẫn.
3. Chạy `BUILD_RUN.bat` và mở kho. Không xóa `account-vault.json`.
4. Tài khoản trước đó lỗi `com.android.launcher3` được nhận dạng là `RETRY_PENDING`, có thể Start lại mà không cần nhập account mới.

## Kiểm tra an toàn
- Trước khi thử lại, yêu cầu log `PREFLIGHT` và handle chính xác.
- Nếu UI vẫn báo `launcher3` hoặc thiếu XPath, sửa lỗi gốc; không tạo vòng lặp vô hạn.
- Bản sửa chưa biên dịch trên Windows hoặc thử Appium với thiết bị thật tại đây.
